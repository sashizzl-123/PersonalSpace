using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using PersonalSpace.Core;
using PersonalSpace.Sync.Adapters;

namespace PersonalSpace.Sync;

/// <summary>
/// Sync Shield. No sync client offers a local pause, so instead: synced pairs who stay outside the bubble for
/// <see cref="ShieldDelayMs"/> are flagged, and <see cref="PenumbraShield"/> swaps their Penumbra collection for an empty
/// one so they show as vanilla. Nothing is sent to any sync server and the other player is never notified.
/// This class only works out <em>who</em> is a pair, through each client's public GetHandledAddresses IPC.
/// </summary>
public sealed class SyncManager : IDisposable
{
    /// <summary>A pair must stay outside this long before being shielded. Re-entry un-shields immediately.</summary>
    public const long ShieldDelayMs = 3000;

    /// <summary>Sync clients are asked for their pairs at most this often.</summary>
    private const long PollIntervalMs = 1000;

    private readonly ActiveSettings settings;
    private readonly BubbleTracker tracker;
    private readonly HashSet<nint> pairAddresses = new(64);
    private long nextPollTick;
    private volatile bool detectPending = true;

    public SyncManager(ActiveSettings settings, BubbleTracker tracker)
    {
        this.settings = settings;
        this.tracker = tracker;

        // Behaviour checked against each client on 2026-10-07 (Lightless 3.3 and Snowcloak 4.1 from source,
        // MareSempiterne 1.15 decompiled). Re-check when a client has a major update.
        Adapters =
        [
            new HandledAddressesAdapter("Lightless Sync", "LightlessSync", "LightlessSync", SyncSupport.PenumbraSwap, verified: true,
                "Lightless applies mods through a Penumbra temporary collection per pair. Shielded pairs get an empty collection "
                + "instead, so they show as vanilla until they step back inside. After an outside redraw Lightless only re-applies "
                + "Glamourer, so the swap holds.\n\nFiles are still downloaded in the background; only applying them is affected."),
            new HandledAddressesAdapter("Snowcloak", "Snowcloak", "Snowcloak", SyncSupport.PenumbraSwap, verified: true,
                "Snowcloak applies mods through a Penumbra temporary collection per pair. Shielded pairs get an empty collection "
                + "instead, so they show as vanilla until they step back inside. Snowcloak re-assigns its collection when new "
                + "data arrives; the swap is re-applied within a second.\n\nFiles are still downloaded in the background."),
            new HandledAddressesAdapter("PlayerSync", "MareSempiterne", "PlayerSync", SyncSupport.PenumbraSwap, verified: false,
                "Closed source, checked by decompiling. PlayerSync applies mods through a Penumbra temporary collection per "
                + "pair and doesn't react to outside redraws, so shielded pairs show as vanilla until they step back inside. "
                + "It re-assigns its collection when new data arrives; the swap is re-applied within a second."),
        ];

        Plugin.PluginInterface.ActivePluginsChanged += OnActivePluginsChanged;
    }

    public IReadOnlyList<ISyncAdapter> Adapters { get; }

    /// <summary>
    /// Keep polling sync clients for their pairs even while Sync Shield is off, because an adaptive preset counts
    /// synced players nearby. Set by <see cref="Presets.PresetEngine"/>.
    /// </summary>
    public bool PollPairsForPresets { get; set; }

    /// <summary>Whether a game object address was reported as a pair by any sync client at the last poll.</summary>
    public bool IsPair(nint address) => pairAddresses.Contains(address);

    /// <summary>Pairs reported at the last poll (loaded pairs only, so effectively "synced players nearby").</summary>
    public int PairCount => pairAddresses.Count;

    public void Dispose()
    {
        Plugin.PluginInterface.ActivePluginsChanged -= OnActivePluginsChanged;
    }

    public void Update()
    {
        if (detectPending)
            Detect();

        var shieldOn = settings.SyncShield;
        var now = Environment.TickCount64;
        var pollNeeded = shieldOn || PollPairsForPresets;

        if (pollNeeded && now >= nextPollTick)
        {
            nextPollTick = now + PollIntervalMs;
            pairAddresses.Clear();
            foreach (var adapter in Adapters)
            {
                if (adapter.Support != SyncSupport.NoEffect)
                    adapter.CollectPairs(pairAddresses);
            }
        }
        else if (!pollNeeded && pairAddresses.Count > 0)
        {
            pairAddresses.Clear();
        }

        if (!tracker.IsActive)
            return;

        foreach (var player in tracker.Players)
        {
            player.IsSyncShielded = shieldOn
                && player.ShouldHide
                && now - player.OutsideSinceTick >= ShieldDelayMs
                && pairAddresses.Contains(player.Address);
        }
    }

    private void Detect()
    {
        detectPending = false;
        foreach (var adapter in Adapters)
            adapter.IsAvailable = false;

        foreach (var plugin in Plugin.PluginInterface.InstalledPlugins)
        {
            if (!plugin.IsLoaded)
                continue;

            foreach (var adapter in Adapters)
            {
                if (string.Equals(plugin.InternalName, adapter.InternalName, StringComparison.Ordinal))
                    adapter.IsAvailable = true;
            }
        }

        // Re-poll straight away with the new set of clients.
        nextPollTick = 0;
    }

    private void OnActivePluginsChanged(IActivePluginsChangedEventArgs args) => detectPending = true;
}
