using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using PersonalSpace.Presets;
using PersonalSpace.Sync;

namespace PersonalSpace.Windows;

public class MainWindow : Window, IDisposable
{
    private const float BaseWidth = 380f;

    /// <summary>Preset rows shown before the list starts scrolling.</summary>
    private const int VisiblePresetRows = 5;

    private static readonly string[] RingModeLabels =
    [
        "Only while settings are open",
        "Always",
        "Never",
    ];

    private readonly Plugin plugin;
    private readonly Configuration config;

    private string manualEntry = string.Empty;

    public MainWindow(Plugin plugin)
        : base("Personal Space###PersonalSpaceMain", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        config = plugin.Configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Fixed width, height follows content. Dalamud applies the global UI scale to these constraints.
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(BaseWidth, 0),
            MaximumSize = new Vector2(BaseWidth, 4000),
        };

        Ui.PushTheme();
    }

    public override void PostDraw() => Ui.PopTheme();

    public override void Draw()
    {
        // Header stays visible on every tab.
        DrawHeader();
        ImGuiHelpers.ScaledDummy(2f);

        using var tabs = ImRaii.TabBar("##tabs");
        if (!tabs.Success)
            return;

        DrawTab("Bubble", DrawBubbleTab);
        DrawTab("Performance", DrawPerformanceTab);
        DrawTab("Sync", DrawSyncTab);
        DrawTab("Exemptions", DrawExemptionsTab);
        DrawTab("Presets", DrawPresetsTab);
    }

    private static void DrawTab(string label, Action draw)
    {
        using var tab = ImRaii.TabItem(label);
        if (!tab.Success)
            return;

        ImGuiHelpers.ScaledDummy(2f);
        draw();
    }

    // ── Header ──────────────────────────────────────────────────────────

    private void DrawHeader()
    {
        var toggleWidth = ImGui.GetFrameHeight() * 1.55f;
        var rightEdge = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Ui.Accent, "Personal Space");
        ImGui.SameLine();
        Ui.Muted("Your bubble. Your rules.", wrap: false);

        ImGui.SameLine(rightEdge - toggleWidth);
        var enabled = config.Enabled;
        if (ImGuiComponents.ToggleButton("##masterToggle", ref enabled))
            plugin.SetEnabled(enabled);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(enabled ? "Personal Space is on. Click to turn it off." : "Personal Space is off. Click to turn it on.");

        var tracker = plugin.Tracker;
        var settings = plugin.Settings;
        if (!settings.Enabled)
        {
            Ui.Muted(config.Enabled ? $"Turned off by preset \"{settings.ActivePreset?.Name}\". Everyone looks normal." : "Turned off. Everyone looks normal.");
        }
        else if (!tracker.IsActive)
        {
            Ui.Muted($"Paused: {tracker.SuspendReason?.ToLowerInvariant()}. Everyone looks normal.");
        }
        else
        {
            ImGui.TextUnformatted($"In bubble: {tracker.InsideCount}");
            ImGui.SameLine(0, 4);
            Ui.Muted("·", wrap: false);
            ImGui.SameLine(0, 4);
            ImGui.TextUnformatted($"{(settings.FullyHide ? "Hidden" : "Faded")}: {tracker.HiddenCount}");
        }

        if (settings.ActivePreset is { } preset)
        {
            ImGui.TextColored(Ui.Accent, $"Preset: {preset.Name}{(plugin.Presets.IsForced ? " (forced)" : "")}");
            Ui.Tooltip(preset.Overrides.Describe());
        }
    }

    /// <summary>Reminds the user that the controls on this tab edit the base values while a preset is overriding some of them.</summary>
    private void PresetNote(params (bool Overridden, string Label)[] items)
    {
        if (plugin.Settings.ActivePreset is not { } preset)
            return;

        var overridden = items.Where(i => i.Overridden).Select(i => i.Label).ToList();
        if (overridden.Count == 0)
            return;

        Ui.Muted($"Preset \"{preset.Name}\" is currently overriding {string.Join(", ", overridden)}. The values here are your base settings and apply when no preset does.");
    }

    // ── Bubble tab ──────────────────────────────────────────────────────

    private void DrawBubbleTab()
    {
        var o = plugin.Settings.Overrides;
        PresetNote((o?.Enabled.HasValue ?? false, "the on/off switch"), (o?.Radius.HasValue ?? false, "the radius"),
                   (o?.RingMode.HasValue ?? false, "the preview ring"), (o?.OutsideOpacity.HasValue ?? false, "the opacity"),
                   (o?.FullyHide.HasValue ?? false, "fully hide"), (o?.HideNames.HasValue ?? false, "hide names"));

        DrawBubbleSection();
        DrawOutsideSection();

        Ui.Section("Window");
        Checkbox("Open at startup", config.OpenOnStartup, v => config.OpenOnStartup = v,
                 "Opens this window when you log in after starting the game.");
    }

    private void DrawBubbleSection()
    {
        Ui.Section("Radius");

        var inputWidth = 80f * ImGuiHelpers.GlobalScale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        var radius = config.Radius;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - inputWidth - spacing);
        if (ImGui.SliderFloat("##radiusSlider", ref radius, Configuration.MinRadius, Configuration.MaxRadius, "%.1f yalms"))
            plugin.SetRadius(MathF.Round(radius * 2f) / 2f); // 0.5 yalm steps
        SaveOnRelease();

        ImGui.SameLine();
        radius = config.Radius;
        ImGui.SetNextItemWidth(inputWidth);
        if (ImGui.InputFloat("##radiusInput", ref radius, 0f, 0f, "%.1f"))
            plugin.SetRadius(radius);
        SaveOnRelease();
        Ui.Tooltip("Type an exact radius in yalms (2–100).");

        var mode = (int)config.RingMode;
        ImGui.SetNextItemWidth(Ui.ControlWidth("Preview ring"));
        if (ImGui.BeginCombo("Preview ring", RingModeLabels[mode]))
        {
            for (var i = 0; i < RingModeLabels.Length; i++)
            {
                if (ImGui.Selectable(RingModeLabels[i], i == mode))
                {
                    config.RingMode = (RingVisibility)i;
                    config.Save();
                }
            }

            ImGui.EndCombo();
        }

        Ui.Tooltip("When to show a ring on the ground at the edge of your bubble. It also flashes for 3 seconds whenever you change the radius.");
    }

    private void DrawOutsideSection()
    {
        Ui.Section("Outside the bubble");

        using (ImRaii.Disabled(config.FullyHide))
        {
            var opacity = config.OutsideOpacity * 100f;
            ImGui.SetNextItemWidth(Ui.ControlWidth("Opacity"));
            if (ImGui.SliderFloat("Opacity", ref opacity, Configuration.MinOpacity * 100f, Configuration.MaxOpacity * 100f, "%.0f%% visible"))
                config.OutsideOpacity = Math.Clamp(opacity / 100f, Configuration.MinOpacity, Configuration.MaxOpacity);
            SaveOnRelease();
        }

        Checkbox("Fully hide instead of fade", config.FullyHide, v => config.FullyHide = v,
                 "Players outside your bubble aren't drawn at all, instead of being faded. Their nameplates, mounts and minions are hidden too.");

        Checkbox("Hide names", config.HideNames, v => config.HideNames = v,
                 "Hides the name, title, FC tag and status icon above players outside your bubble. They come back as soon as the player steps inside.");
    }

    // ── Performance tab ─────────────────────────────────────────────────

    private void DrawPerformanceTab()
    {
        var o = plugin.Settings.Overrides;
        PresetNote((o?.DisableShadowsOutside.HasValue ?? false, "shadows"), (o?.HideAttachmentsOutside.HasValue ?? false, "mounts and minions"),
                   (o?.HideBeyond.HasValue ?? false, "hide beyond"), (o?.MaxTranslucent.HasValue ?? false, "the translucent cap"));

        Ui.Section("While translucent");
        Ui.Muted(config.FullyHide
            ? "Not needed while \"Fully hide instead of fade\" is on: nobody outside your bubble is drawn."
            : "Cheaper ways to draw the crowd outside your bubble while they stay faded.");

        using (ImRaii.Disabled(config.FullyHide))
        {
            Checkbox("Drop their shadows", config.DisableShadowsOutside, v => config.DisableShadowsOutside = v,
                     "Faded players, and their mounts and minions, stop casting shadows. A translucent character is otherwise drawn into every shadow pass just like an opaque one, so this is usually the biggest saving short of hiding them.\n\nSame switch the game's own Shadows → Other NPCs → Hide uses, but only outside your bubble.");

            Checkbox("Hide mounts, minions and accessories", config.HideAttachmentsOutside, v => config.HideAttachmentsOutside = v,
                     "Faded players keep their translucent body, but whatever they're riding or carrying isn't drawn. Mounts are the heaviest single models in a crowd.");

            var beyond = config.HideBeyond;
            ImGui.SetNextItemWidth(Ui.ControlWidth("Hide beyond"));
            if (ImGui.SliderFloat("Hide beyond", ref beyond, 0f, Configuration.MaxHideBeyond, beyond <= 0f ? "Never" : "bubble + %.0f yalms"))
                config.HideBeyond = MathF.Round(Math.Clamp(beyond, 0f, Configuration.MaxHideBeyond));
            SaveOnRelease();
            Ui.Tooltip("Players this far past the edge of your bubble aren't drawn at all; only the band in between stays translucent. In a plaza most of the crowd is far away, so this removes most of the cost while the people near you keep their faded presence.");

            var cap = config.MaxTranslucent;
            ImGui.SetNextItemWidth(Ui.ControlWidth("Translucent cap"));
            if (ImGui.SliderInt("Translucent cap", ref cap, 0, Configuration.MaxTranslucentCap, cap <= 0 ? "No cap" : "%d nearest"))
                config.MaxTranslucent = Math.Clamp(cap, 0, Configuration.MaxTranslucentCap);
            SaveOnRelease();
            Ui.Tooltip("Only the nearest N outside players stay translucent; everyone further out isn't drawn. Adapts to how dense the crowd is rather than how far it spreads. Works together with \"Hide beyond\": whichever is stricter wins.");

            if (plugin.Tracker.IsActive && (plugin.Settings.HideBeyond > 0f || plugin.Settings.MaxTranslucent > 0))
                Ui.Muted($"{plugin.Tracker.FullyHiddenCount} of {plugin.Tracker.HiddenCount} outside players are fully hidden right now.");
        }

        Ui.Muted("Want these to kick in only when it's busy or the frame rate drops? Set up a preset on the Presets tab.");
    }

    // ── Sync tab ────────────────────────────────────────────────────────

    private void DrawSyncTab()
    {
        var o = plugin.Settings.Overrides;
        PresetNote((o?.SyncShield.HasValue ?? false, "Sync Shield"));

        Ui.Section("Sync Shield");

        Checkbox("Show synced players outside the bubble as vanilla", config.SyncShield, v => config.SyncShield = v,
                 "Synced pairs who stay outside your bubble for 3 seconds get an empty Penumbra collection assigned instead of their sync client's, so their mods resolve to vanilla. Their own collection is put back the moment they step inside. They are still faded or hidden like everyone else, according to the Bubble and Performance settings.\n\nThis only affects your game: nothing is sent to the sync server and the other player is never notified. Uses Penumbra's public IPC only. Glamourer and Customize+ changes are left alone; downloads aren't affected.");

        DrawPenumbraStatus();

        Ui.Section("Sync clients");
        DrawSyncClientTable();

        Ui.Muted("Hover a client for details. Files still download in the background; only applying them is affected. Swapping costs one redraw out and one back in.");
    }

    private void DrawPenumbraStatus()
    {
        var shield = plugin.PenumbraShield;
        var (icon, color, label) = shield.IsAvailable
            ? (FontAwesomeIcon.Check, Ui.Good, shield.LastError is null ? "Penumbra connected" : $"Penumbra: {shield.LastError}")
            : (FontAwesomeIcon.Times, Ui.Bad, shield.LastError ?? "Penumbra not detected");

        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(color, icon.ToIconString());
        ImGui.SameLine();
        ImGui.TextColored(color, label);

        if (shield.IsAvailable && plugin.Settings.SyncShield)
        {
            ImGui.SameLine();
            Ui.Muted($"· {shield.ActiveOverrides} shown as vanilla", wrap: false);
        }
    }

    /// <summary>One row per sync client: name, status, pairs nearby. A table so long statuses don't run off the edge.</summary>
    private void DrawSyncClientTable()
    {
        using var table = ImRaii.Table("##syncClients", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("Client", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Nearby", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        foreach (var adapter in plugin.Sync.Adapters)
            DrawSyncClientRow(adapter);
    }

    private void DrawSyncClientRow(ISyncAdapter adapter)
    {
        var penumbraOk = plugin.PenumbraShield.IsAvailable;
        var (icon, color, label) = !adapter.IsAvailable
            ? (FontAwesomeIcon.Times, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], "Not detected")
            : adapter.Support switch
            {
                SyncSupport.Supported => (FontAwesomeIcon.Check, Ui.Good, "Supported"),
                SyncSupport.PenumbraSwap when penumbraOk => (FontAwesomeIcon.ShieldAlt, Ui.Good, "Vanilla"),
                SyncSupport.PenumbraSwap => (FontAwesomeIcon.ExclamationTriangle, Ui.Warn, "Needs Penumbra"),
                _ => (FontAwesomeIcon.Times, Ui.Bad, "No effect"),
            };

        if (adapter.IsAvailable && !adapter.Verified)
            label += " (unverified)";

        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(color, icon.ToIconString());
        var hovered = ImGui.IsItemHovered();
        ImGui.SameLine();
        ImGui.TextUnformatted(adapter.Name);
        hovered |= ImGui.IsItemHovered();

        ImGui.TableNextColumn();
        ImGui.TextColored(color, label);
        hovered |= ImGui.IsItemHovered();

        ImGui.TableNextColumn();
        var polling = plugin.Settings.SyncShield || plugin.Sync.PollPairsForPresets;
        if (adapter.IsAvailable && adapter.CanReadPairs && adapter.Support != SyncSupport.NoEffect && polling)
        {
            ImGui.TextUnformatted(adapter.NearbyPairs.ToString());
            hovered |= ImGui.IsItemHovered();
        }

        if (hovered)
        {
            using var tooltip = ImRaii.Tooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
            ImGui.TextUnformatted(adapter.IsAvailable ? adapter.Note : $"{adapter.Name} isn't installed or isn't loaded.");
            if (adapter.IsAvailable && adapter.Support == SyncSupport.PenumbraSwap)
                ImGui.TextUnformatted("\"Vanilla\": shielded pairs are shown with their mods swapped out through Penumbra.");
            ImGui.PopTextWrapPos();
        }
    }

    // ── Exemptions tab ──────────────────────────────────────────────────

    private void DrawExemptionsTab()
    {
        var o = plugin.Settings.Overrides;
        PresetNote((o?.ExemptFriends.HasValue ?? false, "friends"), (o?.ExemptParty.HasValue ?? false, "party"), (o?.ExemptTarget.HasValue ?? false, "target"));

        Ui.Section("Always look normal");
        Ui.Muted("Exempt players are never faded, name-hidden or sync-paused.");

        Checkbox("Friends", config.ExemptFriends, v => config.ExemptFriends = v, "Players on your friend list always look normal.");
        ImGui.SameLine();
        Checkbox("Party", config.ExemptParty, v => config.ExemptParty = v, "Members of your party always look normal.");
        ImGui.SameLine();
        Checkbox("Target", config.ExemptTarget, v => config.ExemptTarget = v, "Whoever you currently have targeted always looks normal.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Always show");
        Ui.HelpMarker("Players on this list always look normal, wherever they are. Stored as Name@World.");

        DrawAlwaysShowList();
        DrawAlwaysShowAdd();
    }

    private void DrawAlwaysShowList()
    {
        var list = config.AlwaysShow;
        if (list.Count == 0)
        {
            Ui.Muted("No one yet.");
            return;
        }

        var rowHeight = ImGui.GetFrameHeightWithSpacing();
        var height = Math.Min(list.Count, 6) * rowHeight + ImGui.GetStyle().WindowPadding.Y;
        using var child = ImRaii.Child("##alwaysShowList", new Vector2(-1, height), true);
        if (!child.Success)
            return;

        var removeAt = -1;
        for (var i = 0; i < list.Count; i++)
        {
            using var id = ImRaii.PushId(i);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
                removeAt = i;
            Ui.Tooltip("Remove");
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(list[i]);
        }

        if (removeAt >= 0)
        {
            list.RemoveAt(removeAt);
            config.Save();
            plugin.Tracker.RefreshAlwaysShow();
        }
    }

    private void DrawAlwaysShowAdd()
    {
        var target = TargetedPlayerKey();
        using (ImRaii.Disabled(target is null))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Crosshairs, "Add current target"))
                AddAlwaysShow(target!);
        }

        if (target is null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Target another player first.");

        var addWidth = ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.Plus, "Add");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - addWidth - ImGui.GetStyle().ItemSpacing.X);
        var submitted = ImGui.InputTextWithHint("##manualEntry", "Name Surname@World", ref manualEntry, 64, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();

        var valid = IsValidKey(manualEntry);
        using (ImRaii.Disabled(!valid))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "Add"))
                submitted = true;
        }

        if (submitted && valid)
        {
            AddAlwaysShow(manualEntry.Trim());
            manualEntry = string.Empty;
        }
    }

    private void AddAlwaysShow(string key)
    {
        if (config.AlwaysShow.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
            return;

        config.AlwaysShow.Add(key);
        config.Save();
        plugin.Tracker.RefreshAlwaysShow();
    }

    private static string? TargetedPlayerKey()
    {
        if (Plugin.TargetManager.Target is not IPlayerCharacter pc)
            return null;

        return $"{pc.Name.TextValue}@{pc.HomeWorld.Value.Name.ExtractText()}";
    }

    private static bool IsValidKey(string key)
    {
        var at = key.IndexOf('@');
        return at > 0 && at < key.Length - 1 && key.IndexOf('@', at + 1) < 0 && key.Trim().Length == key.Length;
    }

    // ── Presets tab ─────────────────────────────────────────────────────

    private void DrawPresetsTab()
    {
        var engine = plugin.Presets;

        Ui.Section("Adaptive presets");
        Checkbox("Switch settings automatically", config.PresetsEnabled, v => { config.PresetsEnabled = v; engine.Reevaluate(); },
                 "A preset changes some settings while its conditions hold: a low frame rate, a crowd, lots of synced players, a kind of place, an exact zone, world or house. Presets are checked top to bottom and the first match wins. Turn this off to always use your base settings.");

        DrawPresetStatus(engine);
        DrawForceCombo(engine);

        Ui.Section("Right now");
        DrawContext(engine);

        Ui.Section("Presets");
        DrawPresetList(engine);
        DrawPresetAdd();

        Ui.Section("Timing");
        DrawPresetTiming();
    }

    private void DrawPresetStatus(PresetEngine engine)
    {
        if (!config.PresetsEnabled)
        {
            Ui.Muted("Off. Your base settings always apply.");
            return;
        }

        if (engine.Active is { } active)
        {
            ImGui.TextColored(Ui.Accent, $"Active: {active.Name}{(engine.IsForced ? " (forced)" : "")}");
            Ui.Muted(active.Overrides.Describe());
        }
        else
        {
            Ui.Muted(config.Presets.Count == 0 ? "No presets yet. Your base settings apply." : "No preset matches. Your base settings apply.");
        }

        if (engine.IsPendingSwitch)
            Ui.Muted($"Switching to {engine.Pending?.Name ?? "base settings"} in {engine.PendingSecondsLeft:0}s…");
    }

    private void DrawForceCombo(PresetEngine engine)
    {
        using var disabled = ImRaii.Disabled(!config.PresetsEnabled || config.Presets.Count == 0);

        var forced = config.ForcedPresetId.HasValue ? config.Presets.FirstOrDefault(p => p.Id == config.ForcedPresetId) : null;
        ImGui.SetNextItemWidth(Ui.ControlWidth("Force"));
        using (var combo = ImRaii.Combo("Force", forced?.Name ?? "Automatic"))
        {
            if (combo.Success)
            {
                if (ImGui.Selectable("Automatic", forced is null))
                    plugin.ForcePreset(null);

                foreach (var preset in config.Presets)
                {
                    if (ImGui.Selectable(preset.Name, ReferenceEquals(preset, forced)))
                        plugin.ForcePreset(preset);
                }
            }
        }

        Ui.Tooltip("Apply one preset regardless of its conditions, for example to try it out. \"Automatic\" lets the conditions decide again. Also: /pspace preset <name> and /pspace preset auto.");
    }

    private void DrawContext(PresetEngine engine)
    {
        var ctx = engine.Context;
        if (!ctx.LoggedIn)
        {
            Ui.Muted("Not logged in.");
            return;
        }

        using var table = ImRaii.Table("##context", 2, ImGuiTableFlags.SizingFixedFit);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch);

        var countingPairs = plugin.Sync.PollPairsForPresets || plugin.Settings.SyncShield;
        Row("Frame rate", $"{ctx.Fps:0} fps");
        Row("Players nearby", countingPairs ? $"{ctx.Players} ({ctx.ModdedPlayers} synced)" : $"{ctx.Players}");
        if (!countingPairs)
            Ui.Tooltip("Synced players are only counted while a preset uses that condition or Sync Shield is on.");
        Row("Zone", $"{Location.ZoneName(ctx.TerritoryId)} · {Location.Describe(ctx.Place)}");
        Row("World", Location.WorldName(ctx.WorldId));
        if (ctx.InHousing)
            Row("Housing", ctx.Plot > 0 ? $"ward {ctx.Ward}, plot {ctx.Plot}" : $"ward {ctx.Ward}");

        static void Row(string label, string value)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Ui.Muted(label, wrap: false);
            ImGui.TableNextColumn();
            ImGui.TextWrapped(value);
        }
    }

    private void DrawPresetList(PresetEngine engine)
    {
        var list = config.Presets;
        if (list.Count == 0)
        {
            Ui.Muted("None yet. Try one that fully hides the crowd when the frame rate drops, or widens the bubble inside your own house.");
            return;
        }

        var rowHeight = ImGui.GetFrameHeight() + ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.Y * 2f;
        var height = Math.Min(list.Count, VisiblePresetRows) * rowHeight + ImGui.GetStyle().WindowPadding.Y;
        using var child = ImRaii.Child("##presetList", new Vector2(-1, height), true);
        if (!child.Success)
            return;

        var moveFrom = -1;
        var moveTo = -1;
        var removeAt = -1;
        Preset? duplicate = null;

        for (var i = 0; i < list.Count; i++)
        {
            var preset = list[i];
            using var id = ImRaii.PushId(i);

            var enabled = preset.Enabled;
            if (ImGui.Checkbox("##enabled", ref enabled))
            {
                preset.Enabled = enabled;
                plugin.PresetsChanged();
            }

            Ui.Tooltip(enabled ? "Enabled. Untick to skip this preset without deleting it." : "Disabled.");

            ImGui.SameLine();
            var isActive = ReferenceEquals(engine.Active, preset);
            var editing = plugin.PresetEditor.IsEditing(preset);
            ImGui.AlignTextToFramePadding();
            if (isActive)
                ImGui.TextColored(Ui.Accent, preset.Name);
            else if (!enabled)
                Ui.Muted(preset.Name, wrap: false);
            else
                ImGui.TextUnformatted(preset.Name);

            // Buttons right-aligned on the same line.
            var buttonWidth = ImGui.GetFrameHeight();
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var buttonsWidth = buttonWidth * 5 + spacing * 4;
            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - buttonsWidth);

            using (ImRaii.Disabled(i == 0))
            {
                if (ImGui.ArrowButton("##up", ImGuiDir.Up))
                {
                    moveFrom = i;
                    moveTo = i - 1;
                }
            }

            Ui.Tooltip("Move up. Earlier presets win when several match.");
            ImGui.SameLine();

            using (ImRaii.Disabled(i == list.Count - 1))
            {
                if (ImGui.ArrowButton("##down", ImGuiDir.Down))
                {
                    moveFrom = i;
                    moveTo = i + 1;
                }
            }

            Ui.Tooltip("Move down.");
            ImGui.SameLine();

            if (ImGuiComponents.IconButton("##edit", editing ? FontAwesomeIcon.PenSquare : FontAwesomeIcon.Pen))
                plugin.PresetEditor.Open(preset);
            Ui.Tooltip("Edit conditions and settings.");
            ImGui.SameLine();

            if (ImGuiComponents.IconButton("##copy", FontAwesomeIcon.Copy))
                duplicate = preset;
            Ui.Tooltip("Duplicate.");
            ImGui.SameLine();

            var ctrl = ImGui.GetIO().KeyCtrl;
            using (ImRaii.Disabled(!ctrl))
            {
                if (ImGuiComponents.IconButton("##delete", FontAwesomeIcon.Trash))
                    removeAt = i;
            }

            Ui.Tooltip(ctrl ? "Delete this preset." : "Hold Ctrl and click to delete.");

            using (ImRaii.PushIndent(1))
                Ui.Muted($"{preset.DescribeConditions()}  →  {preset.Overrides.Describe()}", wrap: false);
        }

        if (moveFrom >= 0 && moveTo >= 0 && moveTo < list.Count)
        {
            (list[moveFrom], list[moveTo]) = (list[moveTo], list[moveFrom]);
            plugin.PresetsChanged();
        }
        else if (removeAt >= 0)
        {
            list.RemoveAt(removeAt);
            plugin.PresetsChanged();
        }
        else if (duplicate is not null)
        {
            var copy = duplicate.Clone();
            copy.Name = $"{duplicate.Name} (copy)";
            list.Insert(list.IndexOf(duplicate) + 1, copy);
            plugin.PresetsChanged();
            plugin.PresetEditor.Open(copy);
        }
    }

    private void DrawPresetAdd()
    {
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "Add preset"))
        {
            var preset = new Preset { Name = UniquePresetName("New preset") };
            config.Presets.Add(preset);
            plugin.PresetsChanged();
            plugin.PresetEditor.Open(preset);
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Magic, "Add examples"))
        {
            AddExamplePresets();
            plugin.PresetsChanged();
        }

        Ui.Tooltip("Adds three starting points, disabled so nothing changes until you look them over: a low-frame-rate preset, a crowded-city preset and an inside-your-house preset.");
    }

    private void AddExamplePresets()
    {
        config.Presets.Add(new Preset
        {
            Name = UniquePresetName("Low frame rate"),
            Enabled = false,
            Fps = new RangeCondition { Enabled = true, Max = 40f },
            Players = new RangeCondition { Enabled = true, Min = 15f },
            Overrides = new PresetOverrides { FullyHide = true, SyncShield = true },
        });

        config.Presets.Add(new Preset
        {
            Name = UniquePresetName("Crowded city"),
            Enabled = false,
            Players = new RangeCondition { Enabled = true, Min = 40f },
            Places = PlaceKind.City | PlaceKind.HousingDistrict,
            Overrides = new PresetOverrides { Radius = 12f, HideBeyond = 20f, MaxTranslucent = 15, DisableShadowsOutside = true },
        });

        config.Presets.Add(new Preset
        {
            Name = UniquePresetName("Inside a house"),
            Enabled = false,
            Places = PlaceKind.InsideHouse | PlaceKind.Inn,
            Overrides = new PresetOverrides { Enabled = false },
        });
    }

    private string UniquePresetName(string baseName)
    {
        var name = baseName;
        var n = 2;
        while (config.Presets.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {n++}";
        return name;
    }

    private void DrawPresetTiming()
    {
        var activate = config.PresetActivateSeconds;
        ImGui.SetNextItemWidth(Ui.ControlWidth("Switch after"));
        if (ImGui.SliderFloat("Switch after", ref activate, 0f, Configuration.MaxPresetDelaySeconds, activate <= 0f ? "Immediately" : "%.0f s"))
            config.PresetActivateSeconds = MathF.Round(Math.Clamp(activate, 0f, Configuration.MaxPresetDelaySeconds));
        SaveOnRelease();
        Ui.Tooltip("A preset's conditions must hold this long before it takes over. Stops a one-second stutter or someone walking past from flipping your settings.");

        var deactivate = config.PresetDeactivateSeconds;
        ImGui.SetNextItemWidth(Ui.ControlWidth("Return after"));
        if (ImGui.SliderFloat("Return after", ref deactivate, 0f, Configuration.MaxPresetDelaySeconds, deactivate <= 0f ? "Immediately" : "%.0f s"))
            config.PresetDeactivateSeconds = MathF.Round(Math.Clamp(deactivate, 0f, Configuration.MaxPresetDelaySeconds));
        SaveOnRelease();
        Ui.Tooltip("The active preset's conditions must fail this long before your base settings return. Longer keeps a frame-rate preset from bouncing on and off as the frame rate recovers because of it.");

        var margin = config.PresetFpsMargin;
        ImGui.SetNextItemWidth(Ui.ControlWidth("Frame-rate margin"));
        if (ImGui.SliderFloat("Frame-rate margin", ref margin, 0f, Configuration.MaxPresetFpsMargin, margin <= 0f ? "None" : "%.0f fps"))
            config.PresetFpsMargin = MathF.Round(Math.Clamp(margin, 0f, Configuration.MaxPresetFpsMargin));
        SaveOnRelease();
        Ui.Tooltip("Once a preset with a frame-rate condition is active, its limits are relaxed by this much. Hiding the crowd raises the frame rate, so without a margin a \"below 40 fps\" preset would switch itself off as soon as it worked, then straight back on.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private void Checkbox(string label, bool value, Action<bool> set, string? help = null)
    {
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            config.Save();
        }

        if (help is not null)
            Ui.Tooltip(help);
    }

    /// <summary>Saves once when a drag or text edit is finished, not every frame of the drag.</summary>
    private void SaveOnRelease()
    {
        if (ImGui.IsItemDeactivatedAfterEdit())
            config.Save();
    }
}
