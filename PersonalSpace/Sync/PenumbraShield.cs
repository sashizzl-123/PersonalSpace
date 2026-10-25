using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Penumbra.Api.Enums;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;
using PersonalSpace.Core;

namespace PersonalSpace.Sync;

/// <summary>
/// The universal half of Sync Shield. Hiding a pair only <em>delays</em> some clients (Snowcloak and MareSempiterne wait
/// a few seconds, then apply anyway). Every Mare-family client, though, applies mods through a Penumbra temporary
/// collection assigned to the pair, so this class force-assigns an <em>empty</em> temporary collection to shielded pairs
/// instead and redraws them: their mod files resolve to vanilla for every client alike. On re-entry the client's own
/// collection is assigned back and the pair redrawn, so their mods return without the client having to do anything.
/// <para>
/// Only Penumbra's public IPC is used. Glamourer/Customize+ state is left alone (it's cheap and not what Penumbra
/// collections carry). Downloads are not affected either.
/// </para>
/// </summary>
public sealed unsafe class PenumbraShield : IDisposable
{
    private const string Identity = "PersonalSpace";
    private const int RequiredBreakingVersion = 5;

    /// <summary>How often an overridden pair's collection is re-checked, in case the client re-assigned its own.</summary>
    private const long RecheckIntervalMs = 1000;

    /// <summary>Minimum gap between two forced re-assignments (each costs a redraw) of the same pair.</summary>
    private const long ReapplyCooldownMs = 5000;

    /// <summary>Penumbra is probed this often while unavailable.</summary>
    private const long AvailabilityRetryMs = 5000;

    /// <summary>Redraws per frame cap, so a crowd crossing the boundary doesn't hitch.</summary>
    private const int MaxOpsPerFrame = 2;

    private readonly ActiveSettings settings;
    private readonly BubbleTracker tracker;
    private readonly Dictionary<ulong, Override> overrides = new(32);

    private readonly ApiVersion apiVersion;
    private readonly CreateTemporaryCollection createCollection;
    private readonly DeleteTemporaryCollection deleteCollection;
    private readonly AssignTemporaryCollection assignCollection;
    private readonly GetCollectionForObject getCollectionForObject;
    private readonly GetCollections getCollections;
    private readonly RedrawObject redrawObject;
    private readonly EventSubscriber penumbraInitialized;
    private readonly EventSubscriber penumbraDisposed;

    private long nextAvailabilityCheck;
    private long nextRecheckTick;
    private bool loggedFailure;

    /// <summary>Set from Penumbra's Disposed event (any thread); handled on the next framework update.</summary>
    private volatile bool penumbraGone;

    public PenumbraShield(ActiveSettings settings, BubbleTracker tracker)
    {
        this.settings = settings;
        this.tracker = tracker;

        var pi = Plugin.PluginInterface;
        apiVersion = new ApiVersion(pi);
        createCollection = new CreateTemporaryCollection(pi);
        deleteCollection = new DeleteTemporaryCollection(pi);
        assignCollection = new AssignTemporaryCollection(pi);
        getCollectionForObject = new GetCollectionForObject(pi);
        getCollections = new GetCollections(pi);
        redrawObject = new RedrawObject(pi);
        penumbraInitialized = Initialized.Subscriber(pi, () => nextAvailabilityCheck = 0);
        penumbraDisposed = Disposed.Subscriber(pi, () => penumbraGone = true);

        tracker.Reset += RestoreAll;
        tracker.PlayerRemoved += OnPlayerRemoved;
    }

    /// <summary>Penumbra is loaded and speaks the API version we were built against.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Pairs currently showing vanilla instead of their mods.</summary>
    public int ActiveOverrides => overrides.Count;

    /// <summary>Last IPC failure, for the status line. Null when everything is fine.</summary>
    public string? LastError { get; private set; }

    public void Dispose()
    {
        tracker.Reset -= RestoreAll;
        tracker.PlayerRemoved -= OnPlayerRemoved;
        RestoreAll();
        penumbraInitialized.Dispose();
        penumbraDisposed.Dispose();
    }

    public void Update()
    {
        var now = Environment.TickCount64;

        if (penumbraGone)
        {
            // Penumbra took its temporary collections with it; there's nothing left to restore.
            penumbraGone = false;
            IsAvailable = false;
            overrides.Clear();
            nextAvailabilityCheck = now + AvailabilityRetryMs;
        }

        if (!IsAvailable)
        {
            if (now < nextAvailabilityCheck)
                return;

            nextAvailabilityCheck = now + AvailabilityRetryMs;
            IsAvailable = Probe();
            if (!IsAvailable)
                return;
        }

        var active = tracker.IsActive && settings.SyncShield;
        if (!active)
        {
            if (overrides.Count > 0)
                RestoreAll();
            return;
        }

        var recheck = now >= nextRecheckTick;
        if (recheck)
            nextRecheckTick = now + RecheckIntervalMs;

        var ops = 0;
        foreach (var player in tracker.Players)
        {
            if (ops >= MaxOpsPerFrame)
                break;

            var want = player.IsSyncShielded;
            var objectIndex = ((GameObject*)player.Address)->ObjectIndex;

            if (overrides.TryGetValue(player.GameObjectId, out var ov))
            {
                ov.ObjectIndex = objectIndex;

                if (!want)
                {
                    Restore(ov, actorPresent: true);
                    overrides.Remove(player.GameObjectId);
                    ops++;
                }
                else if (recheck && ov.CanVerify && now >= ov.NextReapplyTick)
                {
                    // The client re-applied (new data arrived) and force-assigned its own collection over ours.
                    if (TryGetEffective(objectIndex, out var current) && current.Id != ov.Collection)
                    {
                        ov.Original = current.Id;
                        Apply(ov, now);
                        ops++;
                    }
                }
            }
            else if (want)
            {
                Begin(player, objectIndex, now);
                ops++;
            }
        }
    }

    private void Begin(TrackedPlayer player, ushort objectIndex, long now)
    {
        try
        {
            var current = getCollectionForObject.Invoke(objectIndex);
            if (!current.ObjectValid)
                return;

            var ec = createCollection.Invoke(Identity, $"Personal Space shield {player.GameObjectId:X}", out var collection);
            if (ec != PenumbraApiEc.Success)
            {
                Fail($"CreateTemporaryCollection: {ec}");
                return;
            }

            var ov = new Override(player.GameObjectId)
            {
                ObjectIndex = objectIndex,
                Collection = collection,
                Original = current.EffectiveCollection.Id,
                // With a permanent per-player collection set, Penumbra reports that one rather than the temporary
                // collection that actually renders, so we can't tell afterwards whether the client re-assigned.
                CanVerify = !current.IndividualSet,
            };

            overrides.Add(player.GameObjectId, ov);
            Apply(ov, now);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void Apply(Override ov, long now)
    {
        ov.NextReapplyTick = now + ReapplyCooldownMs;
        try
        {
            var ec = assignCollection.Invoke(ov.Collection, ov.ObjectIndex, forceAssignment: true);
            if (ec != PenumbraApiEc.Success)
            {
                Fail($"AssignTemporaryCollection: {ec}");
                return;
            }

            redrawObject.Invoke(ov.ObjectIndex, RedrawType.Redraw);
            LastError = null;
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private void Restore(Override ov, bool actorPresent)
    {
        try
        {
            if (actorPresent)
            {
                // Hand the slot back to the client's own temporary collection if that's what was there. A permanent
                // collection comes back by itself once ours is gone.
                if (ov.Original != Guid.Empty && IsTemporary(ov.Original))
                    assignCollection.Invoke(ov.Original, ov.ObjectIndex, forceAssignment: true);
            }

            deleteCollection.Invoke(ov.Collection);

            if (actorPresent)
                redrawObject.Invoke(ov.ObjectIndex, RedrawType.Redraw);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    /// <summary>Restores every overridden pair that is still around and forgets the rest.</summary>
    public void RestoreAll()
    {
        foreach (var ov in overrides.Values)
        {
            var address = Plugin.ObjectTable.GetObjectAddress(ov.ObjectIndex);
            var present = address != nint.Zero && ((GameObject*)address)->GetGameObjectId().Id == ov.GameObjectId;
            Restore(ov, present);
        }

        overrides.Clear();
    }

    /// <summary>The pair left the object table. Their client drops its own collection too, so just drop ours.</summary>
    private void OnPlayerRemoved(TrackedPlayer player)
    {
        if (overrides.Remove(player.GameObjectId, out var ov))
            Restore(ov, actorPresent: false);
    }

    private bool TryGetEffective(ushort objectIndex, out (Guid Id, string Name) effective)
    {
        try
        {
            var result = getCollectionForObject.Invoke(objectIndex);
            effective = result.EffectiveCollection;
            return result.ObjectValid;
        }
        catch (Exception ex)
        {
            Fail(ex);
            effective = default;
            return false;
        }
    }

    /// <summary>Temporary collections aren't listed by GetCollections; permanent ones are.</summary>
    private bool IsTemporary(Guid collection) => !getCollections.Invoke().ContainsKey(collection);

    private bool Probe()
    {
        try
        {
            var (breaking, _) = apiVersion.Invoke();
            if (breaking != RequiredBreakingVersion)
            {
                LastError = $"Penumbra API v{breaking} (need v{RequiredBreakingVersion})";
                return false;
            }

            LastError = null;
            loggedFailure = false;
            return true;
        }
        catch
        {
            // Penumbra isn't loaded (yet).
            return false;
        }
    }

    private void Fail(Exception ex) => Fail($"{ex.GetType().Name}: {ex.Message}");

    private void Fail(string message)
    {
        LastError = message;
        if (!loggedFailure)
        {
            Plugin.Log.Warning($"Penumbra shield: {message}");
            loggedFailure = true;
        }
    }

    private sealed class Override(ulong gameObjectId)
    {
        public ulong GameObjectId { get; } = gameObjectId;
        public ushort ObjectIndex { get; set; }

        /// <summary>Our empty temporary collection.</summary>
        public Guid Collection { get; set; }

        /// <summary>What Penumbra reported for the pair before we stepped in, normally the client's temporary collection.</summary>
        public Guid Original { get; set; }

        public bool CanVerify { get; set; }
        public long NextReapplyTick { get; set; }
    }
}
