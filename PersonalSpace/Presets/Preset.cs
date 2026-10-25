using System;
using System.Collections.Generic;
using System.Text;

namespace PersonalSpace.Presets;

/// <summary>Generic kinds of place, worked out from the zone and the housing manager. Flags, so a preset can match several.</summary>
[Flags]
public enum PlaceKind
{
    None = 0,

    /// <summary>A city or town: the three city-states, Ishgard, Kugane, the Crystarium, Old Sharlayan…</summary>
    City = 1 << 0,

    /// <summary>An open-world field zone.</summary>
    Overworld = 1 << 1,

    /// <summary>Outdoors in a residential district (Mist, the Lavender Beds, the Goblet, Shirogane, Empyreum).</summary>
    HousingDistrict = 1 << 2,

    /// <summary>Inside a house, apartment, FC room or private chambers.</summary>
    InsideHouse = 1 << 3,

    /// <summary>An inn room.</summary>
    Inn = 1 << 4,

    /// <summary>Bound by duty: dungeons, trials, raids, deep dungeons, field operations.</summary>
    Duty = 1 << 5,

    /// <summary>The Island Sanctuary.</summary>
    IslandSanctuary = 1 << 6,

    /// <summary>Anything not covered above: Gold Saucer, barracks, Hall of the Novice…</summary>
    Other = 1 << 7,

    All = City | Overworld | HousingDistrict | InsideHouse | Inn | Duty | IslandSanctuary | Other,
}

/// <summary>A numeric condition: value must be at least <see cref="Min"/> and, if <see cref="Max"/> is above zero, at most <see cref="Max"/>.</summary>
[Serializable]
public sealed class RangeCondition
{
    public bool Enabled { get; set; }
    public float Min { get; set; }

    /// <summary>Upper bound, inclusive. 0 or less means no upper bound.</summary>
    public float Max { get; set; }

    /// <param name="margin">Widens both bounds, for hysteresis once a preset is already active.</param>
    public bool Matches(float value, float margin = 0f) => !Enabled || (value >= Min - margin && (Max <= 0f || value <= Max + margin));

    /// <summary>"≤ 40 fps", "30+ players", "5–10 players".</summary>
    public string Describe(string unit)
    {
        if (Max > 0f && Min > 0f)
            return $"{Min:0.#}–{Max:0.#} {unit}";
        if (Max > 0f)
            return $"≤ {Max:0.#} {unit}";
        return $"{Min:0.#}+ {unit}";
    }
}

/// <summary>
/// Settings a preset changes while it is active. Null means "leave the base setting alone".
/// Mirrors the overridable properties on <see cref="Configuration"/>.
/// </summary>
[Serializable]
public sealed class PresetOverrides
{
    public bool? Enabled { get; set; }
    public float? Radius { get; set; }
    public RingVisibility? RingMode { get; set; }
    public float? OutsideOpacity { get; set; }
    public bool? FullyHide { get; set; }
    public bool? HideNames { get; set; }
    public bool? DisableShadowsOutside { get; set; }
    public bool? HideAttachmentsOutside { get; set; }
    public float? HideBeyond { get; set; }
    public int? MaxTranslucent { get; set; }
    public bool? SyncShield { get; set; }
    public bool? ExemptFriends { get; set; }
    public bool? ExemptParty { get; set; }
    public bool? ExemptTarget { get; set; }

    public bool Any =>
        Enabled.HasValue || Radius.HasValue || RingMode.HasValue || OutsideOpacity.HasValue || FullyHide.HasValue
        || HideNames.HasValue || DisableShadowsOutside.HasValue || HideAttachmentsOutside.HasValue || HideBeyond.HasValue
        || MaxTranslucent.HasValue || SyncShield.HasValue || ExemptFriends.HasValue || ExemptParty.HasValue || ExemptTarget.HasValue;

    /// <summary>Short list for the UI: "radius 10 · fully hide · names shown".</summary>
    public string Describe()
    {
        var parts = new List<string>(8);
        if (Enabled is { } enabled) parts.Add(enabled ? "bubble on" : "bubble off");
        if (Radius is { } radius) parts.Add($"radius {radius:0.#}");
        if (OutsideOpacity is { } opacity) parts.Add($"opacity {opacity * 100f:0}%");
        if (FullyHide is { } hide) parts.Add(hide ? "fully hide" : "fade");
        if (HideNames is { } names) parts.Add(names ? "names hidden" : "names shown");
        if (DisableShadowsOutside is { } shadows) parts.Add(shadows ? "no shadows" : "shadows");
        if (HideAttachmentsOutside is { } attachments) parts.Add(attachments ? "no mounts" : "mounts");
        if (HideBeyond is { } beyond) parts.Add(beyond > 0f ? $"hide beyond +{beyond:0}" : "no hide beyond");
        if (MaxTranslucent is { } cap) parts.Add(cap > 0 ? $"cap {cap}" : "no cap");
        if (SyncShield is { } shield) parts.Add(shield ? "sync shield" : "no sync shield");
        if (RingMode is { } ring) parts.Add($"ring: {ring}");
        if (ExemptFriends is { } friends) parts.Add(friends ? "friends exempt" : "friends not exempt");
        if (ExemptParty is { } party) parts.Add(party ? "party exempt" : "party not exempt");
        if (ExemptTarget is { } target) parts.Add(target ? "target exempt" : "target not exempt");
        return parts.Count == 0 ? "changes nothing" : string.Join(" · ", parts);
    }

    public PresetOverrides Clone() => (PresetOverrides)MemberwiseClone();
}

/// <summary>
/// A user-defined preset: a set of conditions (all of which must hold) and the settings to apply while they do.
/// Presets are checked in list order; the first match wins. A preset with no conditions always matches, so one
/// at the bottom of the list acts as a fallback.
/// </summary>
[Serializable]
public sealed class Preset
{
    public const int MaxWard = 30;
    public const int MaxPlot = 60;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New preset";
    public bool Enabled { get; set; } = true;

    // ── Conditions ──────────────────────────────────────────────────────

    /// <summary>Smoothed frame rate.</summary>
    public RangeCondition Fps { get; set; } = new() { Max = 40f };

    /// <summary>Other players loaded nearby (everyone in the object table, inside or outside the bubble).</summary>
    public RangeCondition Players { get; set; } = new() { Min = 30f };

    /// <summary>Nearby players a sync client reports as pairs, i.e. the ones whose mods you're downloading.</summary>
    public RangeCondition ModdedPlayers { get; set; } = new() { Min = 10f };

    /// <summary>World (server) row ids. Empty = any world. Matched against the world you're currently on, travel included.</summary>
    public List<uint> Worlds { get; set; } = [];

    /// <summary>TerritoryType row ids. Empty = any zone.</summary>
    public List<uint> Zones { get; set; } = [];

    /// <summary>Generic place kinds. <see cref="PlaceKind.None"/> = anywhere.</summary>
    public PlaceKind Places { get; set; } = PlaceKind.None;

    /// <summary>Only match in a residential district, optionally a specific ward and plot.</summary>
    public bool MatchHousing { get; set; }

    /// <summary>1–30, or 0 for any ward.</summary>
    public int Ward { get; set; }

    /// <summary>1–60 (31–60 are the subdivision), or 0 for any plot. Apartments report plot 0.</summary>
    public int Plot { get; set; }

    // ── Effect ──────────────────────────────────────────────────────────

    public PresetOverrides Overrides { get; set; } = new();

    public bool HasConditions =>
        Fps.Enabled || Players.Enabled || ModdedPlayers.Enabled || Worlds.Count > 0 || Zones.Count > 0
        || Places != PlaceKind.None || MatchHousing;

    /// <param name="fpsMargin">Relaxes the frame-rate bounds; used for the preset that is already active.</param>
    public bool Matches(in PresetContext ctx, float fpsMargin = 0f)
    {
        if (!Enabled)
            return false;
        if (!Fps.Matches(ctx.Fps, fpsMargin) || !Players.Matches(ctx.Players) || !ModdedPlayers.Matches(ctx.ModdedPlayers))
            return false;
        if (Worlds.Count > 0 && !Worlds.Contains(ctx.WorldId))
            return false;
        if (Zones.Count > 0 && !Zones.Contains(ctx.TerritoryId))
            return false;
        if (Places != PlaceKind.None && (Places & ctx.Place) == 0)
            return false;

        if (MatchHousing)
        {
            if (!ctx.InHousing)
                return false;
            if (Ward > 0 && ctx.Ward != Ward)
                return false;
            if (Plot > 0 && ctx.Plot != Plot)
                return false;
        }

        return true;
    }

    /// <summary>One line for the preset list: "≤ 40 fps · 30+ players · City · 2 zones · ward 5, plot 12".</summary>
    public string DescribeConditions()
    {
        var parts = new List<string>(8);
        if (Fps.Enabled) parts.Add(Fps.Describe("fps"));
        if (Players.Enabled) parts.Add(Players.Describe("players"));
        if (ModdedPlayers.Enabled) parts.Add(ModdedPlayers.Describe("synced"));
        if (Places != PlaceKind.None) parts.Add(Location.Describe(Places));
        if (Zones.Count > 0) parts.Add(Zones.Count == 1 ? Location.ZoneName(Zones[0]) : $"{Zones.Count} zones");
        if (Worlds.Count > 0) parts.Add(Worlds.Count == 1 ? Location.WorldName(Worlds[0]) : $"{Worlds.Count} worlds");
        if (MatchHousing)
        {
            var sb = new StringBuilder("housing");
            if (Ward > 0) sb.Append($" ward {Ward}");
            if (Plot > 0) sb.Append($"{(Ward > 0 ? "," : "")} plot {Plot}");
            parts.Add(sb.ToString());
        }

        return parts.Count == 0 ? "always (no conditions)" : string.Join(" · ", parts);
    }

    public Preset Clone()
    {
        return new Preset
        {
            Name = Name,
            Enabled = Enabled,
            Fps = new RangeCondition { Enabled = Fps.Enabled, Min = Fps.Min, Max = Fps.Max },
            Players = new RangeCondition { Enabled = Players.Enabled, Min = Players.Min, Max = Players.Max },
            ModdedPlayers = new RangeCondition { Enabled = ModdedPlayers.Enabled, Min = ModdedPlayers.Min, Max = ModdedPlayers.Max },
            Worlds = [.. Worlds],
            Zones = [.. Zones],
            Places = Places,
            MatchHousing = MatchHousing,
            Ward = Ward,
            Plot = Plot,
            Overrides = Overrides.Clone(),
        };
    }
}

/// <summary>Everything a preset can be matched against, sampled a few times a second by <see cref="PresetEngine"/>.</summary>
public readonly record struct PresetContext(
    bool LoggedIn,
    float Fps,
    int Players,
    int ModdedPlayers,
    uint WorldId,
    uint TerritoryId,
    PlaceKind Place,
    bool InHousing,
    int Ward,
    int Plot);
