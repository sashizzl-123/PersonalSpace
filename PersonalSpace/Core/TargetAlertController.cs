using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace PersonalSpace.Core;

/// <summary>
/// Notices other players who have you targeted: prints a chat line when they start and outlines them with the game's own
/// object highlight. The outline lingers for a few seconds after they look away. Runs whether or not the bubble is on,
/// but stands down in PvP, cutscenes and GPose, and optionally in combat or duties, where being targeted is routine or
/// nobody else is shown.
/// </summary>
public sealed unsafe class TargetAlertController : IDisposable
{
    /// <summary>Object table slots [0, 199] belong to the character manager; 0 is always the local player.</summary>
    private const int CharacterManagerSlots = 200;

    /// <summary>Someone who targets you again within this long of their last chat line doesn't get another one.</summary>
    private const long NotifyCooldownMs = 30_000;

    private readonly Configuration config;
    private readonly Dictionary<ulong, Watcher> watchers = new(16);
    private readonly Dictionary<ulong, long> lastNotified = new(16);
    private readonly List<ulong> expired = new(16);

    public TargetAlertController(Configuration config)
    {
        this.config = config;
        Plugin.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        Plugin.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Clear();
    }

    /// <summary>Whether this player is currently outlined: targeting you now, or within the linger time.</summary>
    public bool IsOutlined(ulong gameObjectId) => config.TargetAlertOutline && watchers.ContainsKey(gameObjectId);

    public void Update()
    {
        if (!config.TargetAlertChat && !config.TargetAlertOutline || IsSuspended())
        {
            Clear();
            return;
        }

        var local = (GameObject*)Plugin.ObjectTable.GetObjectAddress(0);
        var localId = local->GetGameObjectId();
        var now = Environment.TickCount64;

        for (var i = 1; i < CharacterManagerSlots; i++)
        {
            var address = Plugin.ObjectTable.GetObjectAddress(i);
            if (address == nint.Zero)
                continue;

            var chara = (Character*)address;
            if (chara->GameObject.ObjectKind != ObjectKind.Pc || chara->TargetId != localId)
                continue;
            if (config.TargetAlertIgnoreParty && chara->IsPartyMember)
                continue;
            if (config.TargetAlertIgnoreFriends && chara->IsFriend)
                continue;

            var id = chara->GameObject.GetGameObjectId().Id;
            if (!watchers.TryGetValue(id, out var watcher))
            {
                watcher = new Watcher(id);
                watchers.Add(id, watcher);
                Notify(chara, id, now);
            }

            watcher.ObjectIndex = chara->GameObject.ObjectIndex;
            watcher.LastTargetingTick = now;
        }

        var lingerMs = (long)(config.TargetAlertLingerSeconds * 1000f);
        foreach (var watcher in watchers.Values)
        {
            var obj = Resolve(watcher);
            if (obj == null)
            {
                // Gone from the object table, and their highlight with them.
                expired.Add(watcher.GameObjectId);
                continue;
            }

            if (now - watcher.LastTargetingTick > lingerMs)
            {
                SetOutline(obj, watcher, false);
                expired.Add(watcher.GameObjectId);
                continue;
            }

            SetOutline(obj, watcher, config.TargetAlertOutline);
        }

        foreach (var id in expired)
            watchers.Remove(id);
        expired.Clear();
    }

    /// <summary>Removes every outline we've set and forgets who was looking.</summary>
    public void Clear()
    {
        foreach (var watcher in watchers.Values)
        {
            var obj = Resolve(watcher);
            if (obj != null)
                SetOutline(obj, watcher, false);
        }

        watchers.Clear();
    }

    private bool IsSuspended()
    {
        if (BubbleTracker.GameSuspendReason() is not null)
            return true;

        // Party and alliance members, healers especially, target you constantly in content and in fights.
        var condition = Plugin.Condition;
        if (config.TargetAlertPauseInCombat && condition[ConditionFlag.InCombat])
            return true;

        return config.TargetAlertPauseInDuties
            && (condition[ConditionFlag.BoundByDuty] || condition[ConditionFlag.BoundByDuty56] || condition[ConditionFlag.BoundByDuty95]);
    }

    private void Notify(Character* chara, ulong id, long now)
    {
        if (!config.TargetAlertChat)
            return;

        if (lastNotified.TryGetValue(id, out var last) && now - last < NotifyCooldownMs)
            return;

        var name = chara->GameObject.NameString;
        if (string.IsNullOrEmpty(name))
            return;

        // Prune here rather than every frame: this only runs when someone new starts looking.
        foreach (var (key, tick) in lastNotified)
        {
            if (now - tick >= NotifyCooldownMs)
                lastNotified.Remove(key);
        }

        lastNotified[id] = now;

        // A player link, so the name can be clicked for the usual context menu.
        var message = new SeStringBuilder()
            .Add(new PlayerPayload(name, chara->HomeWorld))
            .AddUiForeground(name, 500)
            .Add(RawPayload.LinkTerminator)
            .AddText(" is looking at you.")
            .Build();
        Plugin.ChatGui.Print(message, "Personal Space");
    }

    /// <summary>The watcher's object, if their slot still holds them.</summary>
    private static GameObject* Resolve(Watcher watcher)
    {
        var address = Plugin.ObjectTable.GetObjectAddress(watcher.ObjectIndex);
        if (address == nint.Zero)
            return null;

        var obj = (GameObject*)address;
        return obj->GetGameObjectId().Id == watcher.GameObjectId ? obj : null;
    }

    /// <summary>
    /// Re-applied every frame while wanted, since the game resets the highlight when the mouse leaves them. Only cleared
    /// if we set it.
    /// </summary>
    private void SetOutline(GameObject* obj, Watcher watcher, bool want)
    {
        if (want)
        {
            obj->Highlight((ObjectHighlightColor)config.TargetAlertColor);
            watcher.Outlined = true;
        }
        else if (watcher.Outlined)
        {
            obj->Highlight(ObjectHighlightColor.None);
            watcher.Outlined = false;
        }
    }

    private void OnTerritoryChanged(uint territoryId) => Clear();

    private sealed class Watcher(ulong gameObjectId)
    {
        public ulong GameObjectId { get; } = gameObjectId;
        public ushort ObjectIndex { get; set; }
        public long LastTargetingTick { get; set; }

        /// <summary>We set the highlight on them and haven't cleared it yet.</summary>
        public bool Outlined { get; set; }
    }
}
