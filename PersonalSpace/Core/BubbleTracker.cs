using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using World = Lumina.Excel.Sheets.World;

namespace PersonalSpace.Core;

/// <summary>
/// Per-frame distance checks with hysteresis. Decides who is inside or outside the bubble
/// and when the plugin should stand down entirely (cutscenes, GPose, PvP, zoning…).
/// </summary>
public sealed unsafe class BubbleTracker : IDisposable
{
    /// <summary>A player becomes "outside" at radius + this, and "inside" again at radius.</summary>
    public const float Hysteresis = 1f;

    /// <summary>A player already translucent keeps their spot until this many closer players have pushed past them.</summary>
    private const int CapRankMargin = 3;

    /// <summary>Object table slots [0, 199] belong to the character manager; 0 is always the local player.</summary>
    private const int CharacterManagerSlots = 200;

    private readonly Configuration config;
    private readonly ActiveSettings settings;
    private readonly Dictionary<ulong, TrackedPlayer> players = new(128);
    private readonly HashSet<string> alwaysShow = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TrackedPlayer> capCandidates = new(128);
    private static readonly Comparison<TrackedPlayer> ByDistance = static (a, b) => a.DistanceSq.CompareTo(b.DistanceSq);
    private int alwaysShowVersion;
    private uint frame;

    public BubbleTracker(Configuration config, ActiveSettings settings)
    {
        this.config = config;
        this.settings = settings;
        RefreshAlwaysShow();
        Plugin.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    /// <summary>Call after the Always-show list in the config changes.</summary>
    public void RefreshAlwaysShow()
    {
        alwaysShow.Clear();
        foreach (var key in config.AlwaysShow)
            alwaysShow.Add(key);

        alwaysShowVersion++;
    }

    /// <summary>"Name@World" for a player, matching the format stored in the Always-show list.</summary>
    public static string? PlayerKey(Character* chara)
    {
        var name = chara->GameObject.NameString;
        if (string.IsNullOrEmpty(name))
            return null;

        var world = Plugin.DataManager.GetExcelSheet<World>().GetRowOrDefault(chara->HomeWorld);
        return world is null ? null : $"{name}@{world.Value.Name.ExtractText()}";
    }

    /// <summary>All players seen this frame. Struct enumerator, so iterating does not allocate.</summary>
    public Dictionary<ulong, TrackedPlayer>.ValueCollection Players => players.Values;

    /// <summary>False while the plugin is off or suspended. Nobody should be faded while inactive.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Why tracking is suspended, for the UI. Null while active.</summary>
    public string? SuspendReason { get; private set; } = "Not logged in";

    public Vector3 LocalPosition { get; private set; }
    public int InsideCount { get; private set; }
    public int HiddenCount { get; private set; }

    /// <summary>How many of <see cref="HiddenCount"/> are fully hidden for performance (far or over the cap).</summary>
    public int FullyHiddenCount { get; private set; }

    /// <summary>
    /// Raised when tracking stops or is reset (turned off, cutscene, GPose, PvP, zone change, unload).
    /// Controllers must restore everyone they've changed.
    /// </summary>
    public event Action? Reset;

    /// <summary>Raised when a player leaves the object table, so controllers can drop their state for them.</summary>
    public event Action<TrackedPlayer>? PlayerRemoved;

    public void Dispose()
    {
        Plugin.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Clear();
    }

    public void Update()
    {
        var reason = GetSuspendReason();
        if (reason is not null)
        {
            SuspendReason = reason;
            if (IsActive || players.Count > 0)
                Clear();
            return;
        }

        var local = (GameObject*)Plugin.ObjectTable.GetObjectAddress(0);
        LocalPosition = local->Position;
        SuspendReason = null;
        IsActive = true;

        frame++;
        var now = Environment.TickCount64;
        var radius = settings.Radius;
        var insideSq = radius * radius;
        var outsideRadius = radius + Hysteresis;
        var outsideSq = outsideRadius * outsideRadius;
        var hideBeyond = settings.HideBeyond > 0f;
        var farRadius = radius + settings.HideBeyond;
        var farInSq = farRadius * farRadius;
        var farOutSq = (farRadius + Hysteresis) * (farRadius + Hysteresis);
        var inside = 0;
        var hidden = 0;
        var target = (nint)TargetSystem.Instance()->Target;

        for (var i = 1; i < CharacterManagerSlots; i++)
        {
            var address = Plugin.ObjectTable.GetObjectAddress(i);
            if (address == nint.Zero)
                continue;

            var obj = (GameObject*)address;
            if (obj->ObjectKind != ObjectKind.Pc)
                continue;

            Vector3 pos = obj->Position;
            var dx = pos.X - LocalPosition.X;
            var dz = pos.Z - LocalPosition.Z;
            var distSq = dx * dx + dz * dz;

            var id = obj->GetGameObjectId().Id;
            if (!players.TryGetValue(id, out var player))
            {
                player = new TrackedPlayer(id);
                players.Add(id, player);
                SetOutside(player, distSq > outsideSq, now);
            }
            else if (player.IsOutside ? distSq <= insideSq : distSq > outsideSq)
            {
                SetOutside(player, !player.IsOutside, now);
            }

            if (!hideBeyond)
                player.IsFar = false;
            else if (player.IsFar ? distSq <= farInSq : distSq > farOutSq)
                player.IsFar = !player.IsFar;

            player.Address = address;
            player.DistanceSq = distSq;
            player.LastSeenFrame = frame;
            player.IsExempt = IsExempt(player, (Character*)address, target);

            if (player.ShouldHide)
                hidden++;
            else
                inside++;
        }

        // Drop players who despawned or walked out of the object table.
        // Removing during enumeration is allowed for Dictionary since .NET Core 3.0.
        foreach (var (id, player) in players)
        {
            if (player.LastSeenFrame == frame)
                continue;

            players.Remove(id);
            PlayerRemoved?.Invoke(player);
        }

        ApplyTranslucentCap();

        InsideCount = inside;
        HiddenCount = hidden;
    }

    /// <summary>Keeps only the nearest <see cref="ActiveSettings.MaxTranslucent"/> outside players translucent; the rest are fully hidden.</summary>
    private void ApplyTranslucentCap()
    {
        var cap = settings.MaxTranslucent;
        capCandidates.Clear();
        foreach (var player in players.Values)
        {
            if (cap > 0 && player.ShouldHide && !player.IsFar)
                capCandidates.Add(player);
            else
                player.IsCapped = false;
        }

        if (cap > 0)
        {
            capCandidates.Sort(ByDistance);
            for (var rank = 0; rank < capCandidates.Count; rank++)
            {
                // Hysteresis in rank rather than distance, so two players swapping places don't flicker.
                if (rank < cap)
                    capCandidates[rank].IsCapped = false;
                else if (rank >= cap + CapRankMargin)
                    capCandidates[rank].IsCapped = true;
            }
        }

        var fullyHidden = 0;
        foreach (var player in players.Values)
        {
            if (player.ShouldHideFully)
                fullyHidden++;
        }

        FullyHiddenCount = fullyHidden;
    }

    /// <summary>Stops tracking everyone and tells controllers to restore them.</summary>
    public void Clear()
    {
        IsActive = false;
        InsideCount = 0;
        HiddenCount = 0;
        FullyHiddenCount = 0;
        players.Clear();
        Reset?.Invoke();
    }

    private bool IsExempt(TrackedPlayer player, Character* chara, nint target)
    {
        if (settings.ExemptTarget && (nint)chara == target)
            return true;
        if (settings.ExemptParty && chara->IsPartyMember)
            return true;
        if (settings.ExemptFriends && chara->IsFriend)
            return true;

        if (player.AlwaysShowVersion != alwaysShowVersion)
        {
            if (alwaysShow.Count == 0)
            {
                player.IsAlwaysShown = false;
                player.AlwaysShowVersion = alwaysShowVersion;
            }
            else if (PlayerKey(chara) is { } key)
            {
                player.IsAlwaysShown = alwaysShow.Contains(key);
                player.AlwaysShowVersion = alwaysShowVersion;
            }

            // No name yet (just spawned): leave the version stale and try again next frame.
        }

        return player.IsAlwaysShown;
    }

    private static void SetOutside(TrackedPlayer player, bool outside, long now)
    {
        player.IsOutside = outside;
        player.OutsideSinceTick = outside ? now : 0;
    }

    private string? GetSuspendReason()
    {
        if (!settings.Enabled)
            return config.Enabled ? $"Turned off by preset \"{settings.ActivePreset?.Name}\"" : "Turned off";

        var condition = Plugin.Condition;
        var clientState = Plugin.ClientState;

        if (!clientState.IsLoggedIn || Plugin.ObjectTable.GetObjectAddress(0) == nint.Zero || condition[ConditionFlag.LoggingOut])
            return "Not logged in";

        if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
            return "Changing zones";

        if (clientState.IsGPosing)
            return "In GPose";

        if (condition[ConditionFlag.OccupiedInCutSceneEvent] || condition[ConditionFlag.WatchingCutscene] || condition[ConditionFlag.WatchingCutscene78])
            return "In a cutscene";

        if (clientState.IsPvP)
            return "In PvP";

        return null;
    }

    private void OnTerritoryChanged(uint territoryId) => Clear();
}
