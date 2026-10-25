using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace PersonalSpace.Presets;

/// <summary>
/// Where the player is, in the two forms presets care about: the exact zone and world, and a generic
/// <see cref="PlaceKind"/>. Also resolves names for the UI and builds the pick lists for the editor.
/// </summary>
public static unsafe class Location
{
    /// <summary>A zone or world the editor can pick from.</summary>
    public readonly record struct Entry(uint Id, string Name, string Detail);

    private static readonly Dictionary<uint, string> ZoneNames = new();
    private static readonly Dictionary<uint, string> WorldNames = new();
    private static List<Entry>? zones;
    private static List<Entry>? worlds;

    // TerritoryIntendedUse rows. Only the unambiguous ones are used; everything else falls back to the duty flag or Other.
    private const uint UseTown = 0;
    private const uint UseOpenWorld = 1;
    private const uint UseInn = 2;
    private const uint UseHousingArea = 13;
    private const uint UseHousingInterior = 14;
    private const uint UseIslandSanctuary = 49;

    /// <summary>Classifies the current location. Housing comes from the game's housing manager, so it's exact even in instanced wards.</summary>
    public static PlaceKind Classify(uint territoryId, out bool inHousing, out int ward, out int plot)
    {
        inHousing = false;
        ward = 0;
        plot = 0;

        var housing = HousingManager.Instance();
        if (housing != null && housing->CurrentTerritory != null)
        {
            var currentWard = housing->GetCurrentWard();
            if (currentWard >= 0)
            {
                inHousing = true;
                ward = currentWard + 1;
                var currentPlot = housing->GetCurrentPlot();
                plot = currentPlot >= 0 ? currentPlot + 1 : 0;
            }

            if (housing->IsInside())
            {
                inHousing = true;
                return PlaceKind.InsideHouse;
            }

            if (inHousing)
                return PlaceKind.HousingDistrict;
        }

        var use = IntendedUse(territoryId);
        switch (use)
        {
            case UseTown:
                return PlaceKind.City;
            case UseOpenWorld:
                return PlaceKind.Overworld;
            case UseInn:
                return PlaceKind.Inn;
            case UseIslandSanctuary:
                return PlaceKind.IslandSanctuary;
            case UseHousingArea:
                inHousing = true;
                return PlaceKind.HousingDistrict;
            case UseHousingInterior:
                inHousing = true;
                return PlaceKind.InsideHouse;
        }

        var condition = Plugin.Condition;
        if (condition[ConditionFlag.BoundByDuty] || condition[ConditionFlag.BoundByDuty56] || condition[ConditionFlag.BoundByDuty95])
            return PlaceKind.Duty;

        return PlaceKind.Other;
    }

    private static uint IntendedUse(uint territoryId)
    {
        var row = Plugin.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId);
        return row?.TerritoryIntendedUse.RowId ?? uint.MaxValue;
    }

    public static string ZoneName(uint territoryId)
    {
        if (territoryId == 0)
            return "No zone";

        if (ZoneNames.TryGetValue(territoryId, out var cached))
            return cached;

        var row = Plugin.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId);
        var name = row?.PlaceName.ValueNullable?.Name.ExtractText();
        if (string.IsNullOrEmpty(name))
            name = $"Zone #{territoryId}";

        ZoneNames[territoryId] = name;
        return name;
    }

    public static string WorldName(uint worldId)
    {
        if (worldId == 0)
            return "No world";

        if (WorldNames.TryGetValue(worldId, out var cached))
            return cached;

        var row = Plugin.DataManager.GetExcelSheet<World>().GetRowOrDefault(worldId);
        var name = row?.Name.ExtractText();
        if (string.IsNullOrEmpty(name))
            name = $"World #{worldId}";

        WorldNames[worldId] = name;
        return name;
    }

    /// <summary>Every named zone, sorted by name. Built once on first use (the sheet has a few thousand rows).</summary>
    public static IReadOnlyList<Entry> AllZones => zones ??= BuildZones();

    /// <summary>Every public world, grouped by data centre.</summary>
    public static IReadOnlyList<Entry> AllWorlds => worlds ??= BuildWorlds();

    private static List<Entry> BuildZones()
    {
        var list = new List<Entry>(1024);
        foreach (var row in Plugin.DataManager.GetExcelSheet<TerritoryType>())
        {
            var name = row.PlaceName.ValueNullable?.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var detail = $"#{row.RowId}";
            var region = row.PlaceNameRegion.ValueNullable?.Name.ExtractText();
            if (!string.IsNullOrEmpty(region) && region != name)
                detail = $"{region} · #{row.RowId}";

            list.Add(new Entry(row.RowId, name, detail));
            ZoneNames.TryAdd(row.RowId, name);
        }

        list.Sort(static (a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.Id.CompareTo(b.Id);
        });
        return list;
    }

    private static List<Entry> BuildWorlds()
    {
        var list = new List<Entry>(128);
        foreach (var row in Plugin.DataManager.GetExcelSheet<World>())
        {
            if (!row.IsPublic || row.DataCenter.RowId == 0)
                continue;

            var name = row.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var dc = row.DataCenter.ValueNullable?.Name.ExtractText() ?? string.Empty;
            list.Add(new Entry(row.RowId, name, dc));
            WorldNames.TryAdd(row.RowId, name);
        }

        return list
            .OrderBy(static e => e.Detail, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string Describe(PlaceKind kind)
    {
        if (kind == PlaceKind.None)
            return "anywhere";
        if (kind == PlaceKind.All)
            return "any place";

        var parts = new List<string>(4);
        foreach (var (flag, label) in PlaceLabels)
        {
            if ((kind & flag) != 0)
                parts.Add(label);
        }

        return string.Join(", ", parts);
    }

    public static readonly (PlaceKind Kind, string Label)[] PlaceLabels =
    [
        (PlaceKind.City, "City"),
        (PlaceKind.Overworld, "Overworld"),
        (PlaceKind.HousingDistrict, "Housing district"),
        (PlaceKind.InsideHouse, "Inside a house"),
        (PlaceKind.Inn, "Inn room"),
        (PlaceKind.Duty, "Duty"),
        (PlaceKind.IslandSanctuary, "Island Sanctuary"),
        (PlaceKind.Other, "Other"),
    ];

    public static string PlaceTooltip(PlaceKind kind) => kind switch
    {
        PlaceKind.City => "Cities and towns: Limsa Lominsa, Gridania, Ul'dah, Ishgard, Kugane, the Crystarium, Eulmore, Old Sharlayan, Radz-at-Han, Tuliyollal, Solution Nine and the smaller settlements.",
        PlaceKind.Overworld => "Open-world field zones.",
        PlaceKind.HousingDistrict => "Outdoors in a residential district: Mist, the Lavender Beds, the Goblet, Shirogane, Empyreum. Instanced wards included.",
        PlaceKind.InsideHouse => "Inside any house, apartment, FC room or private chambers.",
        PlaceKind.Inn => "Inn rooms.",
        PlaceKind.Duty => "Anything you're bound to by duty: dungeons, trials, raids, deep dungeons, Eureka, Bozja and the like. PvP arenas pause the plugin anyway.",
        PlaceKind.IslandSanctuary => "Your Island Sanctuary.",
        PlaceKind.Other => "Everything else: the Gold Saucer, barracks, the Hall of the Novice, quest instances…",
        _ => string.Empty,
    };
}
