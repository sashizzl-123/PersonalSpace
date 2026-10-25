using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace PersonalSpace.Windows;

/// <summary>Colours, theme push/pop and small widgets shared by the plugin's windows.</summary>
internal static class Ui
{
    public static readonly Vector4 Accent = new(0.36f, 0.78f, 0.85f, 1f);
    public static readonly Vector4 AccentHover = new(0.46f, 0.85f, 0.92f, 1f);
    public static readonly Vector4 AccentDim = new(0.36f, 0.78f, 0.85f, 0.35f);

    public static readonly Vector4 Good = new(0.45f, 0.85f, 0.45f, 1f);
    public static readonly Vector4 Warn = new(0.95f, 0.78f, 0.35f, 1f);
    public static readonly Vector4 Bad = new(0.95f, 0.45f, 0.45f, 1f);

    private const int ThemeColors = 8;
    private const int ThemeVars = 2;

    /// <summary>One accent colour for active states and generous spacing. Pair with <see cref="PopTheme"/>.</summary>
    public static void PushTheme()
    {
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, AccentHover);
        ImGui.PushStyleColor(ImGuiCol.Header, AccentDim);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, AccentDim with { W = 0.5f });
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, AccentDim with { W = 0.65f });
        ImGui.PushStyleColor(ImGuiCol.TabActive, AccentDim with { W = 0.55f });
        ImGui.PushStyleColor(ImGuiCol.TabHovered, AccentDim with { W = 0.7f });

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 7f) * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14f, 12f) * ImGuiHelpers.GlobalScale);
    }

    public static void PopTheme()
    {
        ImGui.PopStyleVar(ThemeVars);
        ImGui.PopStyleColor(ThemeColors);
    }

    /// <summary>
    /// Section header: label followed by a line to the right edge.
    /// Substitute for ImGui.SeparatorText, which Dalamud's bundled cimgui does not export.
    /// </summary>
    public static void Section(string title)
    {
        ImGuiHelpers.ScaledDummy(4f);
        ImGui.TextUnformatted(title);
        ImGui.SameLine();

        var start = ImGui.GetCursorScreenPos();
        var y = start.Y + ImGui.GetTextLineHeight() * 0.5f;
        var end = start.X + ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddLine(new Vector2(start.X, y), new Vector2(end, y), ImGui.GetColorU32(ImGuiCol.Separator), ImGuiHelpers.GlobalScale);
        ImGui.NewLine();
    }

    public static void HelpMarker(string text)
    {
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(text);
    }

    public static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(text);
    }

    public static void Muted(string text, bool wrap = true)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled)))
        {
            if (wrap)
                ImGui.TextWrapped(text);
            else
                ImGui.TextUnformatted(text);
        }
    }

    public static float LabelWidth(string label) => ImGui.CalcTextSize(label).X;

    /// <summary>Width left for a control that has a label to its right.</summary>
    public static float ControlWidth(string label) => ImGui.GetContentRegionAvail().X - LabelWidth(label) - ImGui.GetStyle().ItemSpacing.X;
}
