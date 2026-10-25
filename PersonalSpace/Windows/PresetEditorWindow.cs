using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using PersonalSpace.Presets;

namespace PersonalSpace.Windows;

/// <summary>
/// Edits one preset: its conditions ("when") and the settings it overrides ("then"). Changes are saved once the
/// control being edited is released, so dragging a slider doesn't write the config every frame.
/// </summary>
public class PresetEditorWindow : Window, IDisposable
{
    private const int MaxPickerResults = 60;

    private static readonly string[] RingModeLabels =
    [
        "Only while settings are open",
        "Always",
        "Never",
    ];

    private readonly Plugin plugin;
    private readonly Configuration config;

    private Preset? preset;
    private string zoneFilter = string.Empty;
    private string worldFilter = string.Empty;
    private bool dirty;

    public PresetEditorWindow(Plugin plugin)
        : base("Edit preset###PersonalSpacePresetEditor")
    {
        this.plugin = plugin;
        config = plugin.Configuration;

        Size = new Vector2(520f, 640f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460f, 320f),
            MaximumSize = new Vector2(1000f, 2000f),
        };
    }

    public void Dispose() { }

    public void Open(Preset target)
    {
        preset = target;
        zoneFilter = string.Empty;
        worldFilter = string.Empty;
        IsOpen = true;
        BringToFront();
    }

    public bool IsEditing(Preset target) => IsOpen && ReferenceEquals(preset, target);

    public override void PreDraw() => Ui.PushTheme();

    public override void PostDraw() => Ui.PopTheme();

    public override void OnClose()
    {
        Flush(force: true);
    }

    public override void Draw()
    {
        if (preset is null || !config.Presets.Contains(preset))
        {
            IsOpen = false;
            return;
        }

        DrawHeader(preset);

        using (var child = ImRaii.Child("##presetBody", new Vector2(-1, -1), false))
        {
            if (child.Success)
            {
                DrawConditions(preset);
                DrawOverrides(preset);
            }
        }

        Flush(force: false);
    }

    /// <summary>Saves pending edits. Unless forced, waits until no control is being dragged or typed into.</summary>
    private void Flush(bool force)
    {
        if (!dirty || (!force && ImGui.IsAnyItemActive()))
            return;

        dirty = false;
        plugin.PresetsChanged();
    }

    // ── Header ──────────────────────────────────────────────────────────

    private void DrawHeader(Preset p)
    {
        var name = p.Name;
        ImGui.SetNextItemWidth(Ui.ControlWidth("Name"));
        if (ImGui.InputText("Name", ref name, 48))
        {
            p.Name = name;
            dirty = true;
        }

        var enabled = p.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            p.Enabled = enabled;
            dirty = true;
        }

        Ui.Tooltip("A disabled preset is skipped, but kept in the list.");

        ImGui.SameLine();
        DrawMatchStatus(p);
        ImGuiHelpers.ScaledDummy(2f);
    }

    private void DrawMatchStatus(Preset p)
    {
        var engine = plugin.Presets;
        var (color, text) = !config.PresetsEnabled
            ? (ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], "Adaptive presets are turned off")
            : ReferenceEquals(engine.Active, p)
                ? (Ui.Good, engine.IsForced ? "Active now (forced)" : "Active now")
                : !p.Enabled
                    ? (ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], "Disabled")
                    : p.Matches(engine.Context)
                        ? (Ui.Warn, engine.Active is null ? "Conditions hold right now" : "Conditions hold, but an earlier preset wins")
                        : (ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], "Conditions don't hold right now");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(color, text);
    }

    // ── Conditions ──────────────────────────────────────────────────────

    private void DrawConditions(Preset p)
    {
        Ui.Section("When");
        Ui.Muted("Every ticked condition must hold. With none ticked the preset always applies, which makes a good fallback at the bottom of the list.");

        var ctx = plugin.Presets.Context;

        DrawRange("Frame rate", p.Fps, "fps", integer: false, $"Smoothed over about a second and a half. Right now: {ctx.Fps:0} fps.");
        DrawRange("Players nearby", p.Players, "players", integer: true,
                  $"Other players loaded around you, inside or outside the bubble. Right now: {ctx.Players}.");
        DrawRange("Synced players nearby", p.ModdedPlayers, "synced", integer: true,
                  $"Players a sync client (Lightless, Snowcloak, PlayerSync…) reports as pairs: the ones whose mods you're actually loading. Needs a sync client; without one this is always 0. Right now: {ctx.ModdedPlayers}.");

        DrawPlaces(p);
        DrawZones(p, ctx);
        DrawWorlds(p, ctx);
        DrawHousing(p, ctx);
    }

    private void DrawRange(string label, RangeCondition range, string unit, bool integer, string help)
    {
        var on = range.Enabled;
        if (ImGui.Checkbox(label, ref on))
        {
            range.Enabled = on;
            dirty = true;
        }

        Ui.Tooltip(help);

        using var disabled = ImRaii.Disabled(!on);
        using var indent = ImRaii.PushIndent(1);

        var inputWidth = 70f * ImGuiHelpers.GlobalScale;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("at least");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(inputWidth);
        var min = range.Min;
        if (DrawNumber($"##{label}min", ref min, integer))
        {
            range.Min = Math.Max(0f, min);
            dirty = true;
        }

        ImGui.SameLine();
        ImGui.TextUnformatted("and at most");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(inputWidth);
        var max = range.Max;
        if (DrawNumber($"##{label}max", ref max, integer))
        {
            range.Max = Math.Max(0f, max);
            dirty = true;
        }

        ImGui.SameLine();
        Ui.Muted(range.Max <= 0f ? $"{unit} (no upper limit)" : unit, wrap: false);
    }

    private static bool DrawNumber(string id, ref float value, bool integer)
    {
        if (integer)
        {
            var i = (int)MathF.Round(value);
            if (!ImGui.InputInt(id, ref i, 1, 10))
                return false;

            value = Math.Max(0, i);
            return true;
        }

        return ImGui.InputFloat(id, ref value, 1f, 10f, "%.0f");
    }

    private void DrawPlaces(Preset p)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Kind of place");
        Ui.HelpMarker("Generic places, worked out from the zone and the game's housing state. Tick several to match any of them. Leave all unticked to match anywhere.");
        ImGui.SameLine();
        Ui.Muted(p.Places == PlaceKind.None ? "anywhere" : Location.Describe(p.Places), wrap: false);

        using var indent = ImRaii.PushIndent(1);
        using var table = ImRaii.Table("##places", 2, ImGuiTableFlags.SizingStretchSame);
        if (!table.Success)
            return;

        foreach (var (kind, label) in Location.PlaceLabels)
        {
            ImGui.TableNextColumn();
            var on = (p.Places & kind) != 0;
            if (ImGui.Checkbox(label, ref on))
            {
                p.Places = on ? p.Places | kind : p.Places & ~kind;
                dirty = true;
            }

            Ui.Tooltip(Location.PlaceTooltip(kind));
        }
    }

    private void DrawZones(Preset p, in PresetContext ctx)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Zones");
        Ui.HelpMarker("Exact zones (territories). The preset only applies in one of these. Leave empty for any zone. Instanced copies of a zone count as the same zone.");

        using var indent = ImRaii.PushIndent(1);

        DrawChosen("zones", p.Zones, Location.ZoneName);

        using (ImRaii.Disabled(!ctx.LoggedIn || ctx.TerritoryId == 0 || p.Zones.Contains(ctx.TerritoryId)))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.MapMarkerAlt, $"Add current zone"))
            {
                p.Zones.Add(ctx.TerritoryId);
                dirty = true;
            }
        }

        Ui.Tooltip(ctx.LoggedIn ? $"Adds {Location.ZoneName(ctx.TerritoryId)}." : "Log in first.");

        DrawPicker("zone", Location.AllZones, p.Zones, ref zoneFilter, "Type to search zones…");
    }

    private void DrawWorlds(Preset p, in PresetContext ctx)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Worlds");
        Ui.HelpMarker("Exact servers. Matched against the world you're on right now, so it follows you when you world-travel. Leave empty for any world.");

        using var indent = ImRaii.PushIndent(1);

        DrawChosen("worlds", p.Worlds, Location.WorldName);

        using (ImRaii.Disabled(!ctx.LoggedIn || ctx.WorldId == 0 || p.Worlds.Contains(ctx.WorldId)))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Globe, "Add current world"))
            {
                p.Worlds.Add(ctx.WorldId);
                dirty = true;
            }
        }

        Ui.Tooltip(ctx.LoggedIn ? $"Adds {Location.WorldName(ctx.WorldId)}." : "Log in first.");

        DrawPicker("world", Location.AllWorlds, p.Worlds, ref worldFilter, "Type to search worlds…");
    }

    private void DrawChosen(string id, List<uint> chosen, Func<uint, string> nameOf)
    {
        if (chosen.Count == 0)
        {
            Ui.Muted("Any.", wrap: false);
            return;
        }

        var removeAt = -1;
        for (var i = 0; i < chosen.Count; i++)
        {
            using var pushId = ImRaii.PushId($"{id}{i}");
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
                removeAt = i;
            Ui.Tooltip("Remove");
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(nameOf(chosen[i]));
        }

        if (removeAt >= 0)
        {
            chosen.RemoveAt(removeAt);
            dirty = true;
        }
    }

    /// <summary>A filter box; while it has text, a list of matching entries that aren't chosen yet. Clicking one adds it.</summary>
    private void DrawPicker(string id, IReadOnlyList<Location.Entry> all, List<uint> chosen, ref string filter, string hint)
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint($"##{id}filter", hint, ref filter, 64);
        if (filter.Length == 0)
            return;

        var rowHeight = ImGui.GetTextLineHeightWithSpacing();
        using var child = ImRaii.Child($"##{id}results", new Vector2(-1, rowHeight * 8 + ImGui.GetStyle().WindowPadding.Y), true);
        if (!child.Success)
            return;

        var shown = 0;
        uint? picked = null;
        foreach (var entry in all)
        {
            if (chosen.Contains(entry.Id))
                continue;
            if (!entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) && !entry.Detail.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (ImGui.Selectable($"{entry.Name}##{id}{entry.Id}"))
                picked = entry.Id;

            if (entry.Detail.Length > 0)
            {
                ImGui.SameLine();
                Ui.Muted(entry.Detail, wrap: false);
            }

            if (++shown >= MaxPickerResults)
            {
                Ui.Muted("More matches: keep typing to narrow it down.");
                break;
            }
        }

        if (shown == 0)
            Ui.Muted("No matches.");

        if (picked is { } add)
        {
            chosen.Add(add);
            filter = string.Empty;
            dirty = true;
        }
    }

    private void DrawHousing(Preset p, in PresetContext ctx)
    {
        ImGui.Spacing();
        var on = p.MatchHousing;
        if (ImGui.Checkbox("Only in a housing district", ref on))
        {
            p.MatchHousing = on;
            dirty = true;
        }

        Ui.Tooltip("Only applies in a residential district (outdoors or inside a house), optionally in one ward and plot. Add the district as a zone too (Mist, the Goblet…) to pin a specific house, since every district has wards 1–30.");

        using var disabled = ImRaii.Disabled(!on);
        using var indent = ImRaii.PushIndent(1);

        var inputWidth = 70f * ImGuiHelpers.GlobalScale;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Ward");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(inputWidth);
        var ward = p.Ward;
        if (ImGui.InputInt("##ward", ref ward, 1, 5))
        {
            p.Ward = Math.Clamp(ward, 0, Preset.MaxWard);
            dirty = true;
        }

        ImGui.SameLine();
        ImGui.TextUnformatted("Plot");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(inputWidth);
        var plot = p.Plot;
        if (ImGui.InputInt("##plot", ref plot, 1, 5))
        {
            p.Plot = Math.Clamp(plot, 0, Preset.MaxPlot);
            dirty = true;
        }

        ImGui.SameLine();
        Ui.Muted("0 = any", wrap: false);

        using (ImRaii.Disabled(!ctx.InHousing))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Home, "Use current ward and plot"))
            {
                p.Ward = ctx.Ward;
                p.Plot = ctx.Plot;
                dirty = true;
            }
        }

        Ui.Tooltip(ctx.InHousing
            ? ctx.Plot > 0 ? $"Ward {ctx.Ward}, plot {ctx.Plot}." : $"Ward {ctx.Ward}, no plot (apartment or ward street)."
            : "You're not in a housing district right now.");
    }

    // ── Overrides ───────────────────────────────────────────────────────

    private void DrawOverrides(Preset p)
    {
        Ui.Section("Then");
        Ui.Muted("Tick a setting to change it while this preset is active. Unticked settings keep whatever is set on the other tabs.");

        var o = p.Overrides;

        Ui.Section("Bubble");
        OverrideBool("Bubble enabled", o.Enabled, v => o.Enabled = v, config.Enabled,
                     "Turn the whole bubble off (everyone looks normal) or on while this preset is active.");
        OverrideSlider("Radius", o.Radius, v => o.Radius = v, config.Radius, Configuration.MinRadius, Configuration.MaxRadius, "%.1f yalms",
                       "The bubble radius while this preset is active.");
        OverrideRingMode(o);

        Ui.Section("Outside the bubble");
        OverrideOpacity(o);
        OverrideBool("Fully hide instead of fade", o.FullyHide, v => o.FullyHide = v, config.FullyHide,
                     "Players outside the bubble aren't drawn at all instead of being faded.");
        OverrideBool("Hide names", o.HideNames, v => o.HideNames = v, config.HideNames,
                     "Hide the nameplates of players outside the bubble.");

        Ui.Section("Performance");
        OverrideBool("Drop their shadows", o.DisableShadowsOutside, v => o.DisableShadowsOutside = v, config.DisableShadowsOutside,
                     "Faded players stop casting shadows.");
        OverrideBool("Hide mounts, minions and accessories", o.HideAttachmentsOutside, v => o.HideAttachmentsOutside = v, config.HideAttachmentsOutside,
                     "Faded players keep their body, but whatever they're riding or carrying isn't drawn.");
        OverrideSlider("Hide beyond", o.HideBeyond, v => o.HideBeyond = v is { } f ? MathF.Round(f) : null, config.HideBeyond, 0f, Configuration.MaxHideBeyond,
                       (o.HideBeyond ?? config.HideBeyond) <= 0f ? "Never" : "bubble + %.0f yalms",
                       "Fully hide players this far past the bubble edge.");
        OverrideIntSlider("Translucent cap", o.MaxTranslucent, v => o.MaxTranslucent = v, config.MaxTranslucent, 0, Configuration.MaxTranslucentCap,
                          (o.MaxTranslucent ?? config.MaxTranslucent) <= 0 ? "No cap" : "%d nearest",
                          "Only the nearest N outside players stay translucent; the rest aren't drawn.");

        Ui.Section("Sync");
        OverrideBool("Sync Shield", o.SyncShield, v => o.SyncShield = v, config.SyncShield,
                     "Show synced pairs outside the bubble as vanilla through Penumbra.");

        Ui.Section("Exemptions");
        OverrideBool("Friends look normal", o.ExemptFriends, v => o.ExemptFriends = v, config.ExemptFriends, "Friends are never faded or hidden.");
        OverrideBool("Party looks normal", o.ExemptParty, v => o.ExemptParty = v, config.ExemptParty, "Party members are never faded or hidden.");
        OverrideBool("Target looks normal", o.ExemptTarget, v => o.ExemptTarget = v, config.ExemptTarget, "Your current target is never faded or hidden.");

        ImGuiHelpers.ScaledDummy(6f);
    }

    /// <summary>The "override this?" tick box in front of a control. Returns whether the control should be enabled.</summary>
    private bool OverrideToggle(string id, bool has, Action<bool> set)
    {
        if (ImGui.Checkbox($"##override{id}", ref has))
        {
            set(has);
            dirty = true;
        }

        Ui.Tooltip(has ? "Overridden while this preset is active. Untick to leave the setting alone." : "Tick to change this setting while the preset is active.");
        ImGui.SameLine();
        return has;
    }

    private void OverrideBool(string label, bool? current, Action<bool?> set, bool baseValue, string help)
    {
        var has = OverrideToggle(label, current.HasValue, on => set(on ? baseValue : null));
        using var disabled = ImRaii.Disabled(!has);

        var value = current ?? baseValue;
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            dirty = true;
        }

        Ui.Tooltip(has ? help : $"{help}\n\nCurrently not overridden: the base setting ({(baseValue ? "on" : "off")}) applies.");
    }

    private void OverrideSlider(string label, float? current, Action<float?> set, float baseValue, float min, float max, string format, string help)
    {
        var has = OverrideToggle(label, current.HasValue, on => set(on ? baseValue : null));
        using var disabled = ImRaii.Disabled(!has);

        var value = current ?? baseValue;
        ImGui.SetNextItemWidth(Ui.ControlWidth(label));
        if (ImGui.SliderFloat(label, ref value, min, max, format))
        {
            set(Math.Clamp(value, min, max));
            dirty = true;
        }

        Ui.Tooltip(help);
    }

    private void OverrideIntSlider(string label, int? current, Action<int?> set, int baseValue, int min, int max, string format, string help)
    {
        var has = OverrideToggle(label, current.HasValue, on => set(on ? baseValue : null));
        using var disabled = ImRaii.Disabled(!has);

        var value = current ?? baseValue;
        ImGui.SetNextItemWidth(Ui.ControlWidth(label));
        if (ImGui.SliderInt(label, ref value, min, max, format))
        {
            set(Math.Clamp(value, min, max));
            dirty = true;
        }

        Ui.Tooltip(help);
    }

    private void OverrideOpacity(PresetOverrides o)
    {
        const string label = "Opacity";
        var has = OverrideToggle(label, o.OutsideOpacity.HasValue, on => o.OutsideOpacity = on ? config.OutsideOpacity : null);
        using var disabled = ImRaii.Disabled(!has);

        var percent = (o.OutsideOpacity ?? config.OutsideOpacity) * 100f;
        ImGui.SetNextItemWidth(Ui.ControlWidth(label));
        if (ImGui.SliderFloat(label, ref percent, Configuration.MinOpacity * 100f, Configuration.MaxOpacity * 100f, "%.0f%% visible"))
        {
            o.OutsideOpacity = Math.Clamp(percent / 100f, Configuration.MinOpacity, Configuration.MaxOpacity);
            dirty = true;
        }

        Ui.Tooltip("How visible faded players are. Ignored while \"Fully hide\" is on.");
    }

    private void OverrideRingMode(PresetOverrides o)
    {
        const string label = "Preview ring";
        var has = OverrideToggle(label, o.RingMode.HasValue, on => o.RingMode = on ? config.RingMode : null);
        using var disabled = ImRaii.Disabled(!has);

        var mode = (int)(o.RingMode ?? config.RingMode);
        ImGui.SetNextItemWidth(Ui.ControlWidth(label));
        using var combo = ImRaii.Combo(label, RingModeLabels[mode]);
        if (!combo.Success)
            return;

        for (var i = 0; i < RingModeLabels.Length; i++)
        {
            if (ImGui.Selectable(RingModeLabels[i], i == mode))
            {
                o.RingMode = (RingVisibility)i;
                dirty = true;
            }
        }
    }
}
