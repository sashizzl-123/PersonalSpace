using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace PersonalSpace.Core;

/// <summary>
/// Draws the bubble boundary as a ring on the ground (XZ plane at the player's height),
/// projected through <see cref="Dalamud.Plugin.Services.IGameGui.WorldToScreen(Vector3, out Vector2, out bool)"/>
/// onto ImGui's background draw list. No depth testing: the ring shows through walls and over the HUD.
/// </summary>
public sealed class RingRenderer
{
    private const int Segments = 128;
    private const long FlashMs = 3000;
    private const long FlashFadeMs = 600;

    /// <summary>Bisection steps used to find where a segment crosses behind the camera.</summary>
    private const int ClipSteps = 8;

    private static readonly Vector2[] UnitCircle = BuildUnitCircle();

    private readonly Plugin plugin;
    private readonly Configuration config;
    private readonly ActiveSettings settings;
    private readonly BubbleTracker tracker;

    // Reused every frame: no allocations while drawing.
    private readonly Vector3[] world = new Vector3[Segments];
    private readonly Vector2[] screen = new Vector2[Segments];
    private readonly bool[] inFront = new bool[Segments];

    public RingRenderer(Plugin plugin, BubbleTracker tracker)
    {
        this.plugin = plugin;
        this.tracker = tracker;
        config = plugin.Configuration;
        settings = plugin.Settings;
    }

    public void Draw()
    {
        if (!tracker.IsActive)
            return;

        var alphaScale = Visibility();
        if (alphaScale <= 0f)
            return;

        var center = tracker.LocalPosition;
        var radius = settings.Radius;
        var anyInFront = false;
        var allInFront = true;

        for (var i = 0; i < Segments; i++)
        {
            world[i] = new Vector3(center.X + UnitCircle[i].X * radius, center.Y, center.Z + UnitCircle[i].Y * radius);
            inFront[i] = Plugin.GameGui.WorldToScreen(world[i], out screen[i], out _);
            anyInFront |= inFront[i];
            allInFront &= inFront[i];
        }

        if (!anyInFront)
            return;

        var drawList = ImGui.GetBackgroundDrawList();
        var ringColor = config.RingColor;
        var lineColor = ImGui.GetColorU32(ringColor with { W = ringColor.W * alphaScale });
        var thickness = config.RingThickness;

        if (allInFront)
        {
            for (var i = 0; i < Segments; i++)
                drawList.PathLineTo(screen[i]);
            drawList.PathStroke(lineColor, ImDrawFlags.Closed, thickness);
            return;
        }

        // Walk the ring starting just after a point behind the camera, so each visible run is one open path.
        var start = FirstBehind();
        var open = false;
        for (var step = 1; step <= Segments; step++)
        {
            var prev = (start + step - 1) % Segments;
            var cur = (start + step) % Segments;

            if (inFront[cur])
            {
                if (!open)
                {
                    drawList.PathLineTo(ClipToCamera(cur, prev));
                    open = true;
                }

                drawList.PathLineTo(screen[cur]);
            }
            else if (open)
            {
                drawList.PathLineTo(ClipToCamera(prev, cur));
                drawList.PathStroke(lineColor, ImDrawFlags.None, thickness);
                open = false;
            }
        }
    }

    /// <summary>Returns a 0–1 alpha multiplier, or 0 when the ring shouldn't be drawn.</summary>
    private float Visibility()
    {
        var sinceChange = Environment.TickCount64 - plugin.RadiusChangedTick;
        var flash = sinceChange is >= 0 and < FlashMs
            ? Math.Clamp((FlashMs - sinceChange) / (float)FlashFadeMs, 0f, 1f)
            : 0f;

        var steady = settings.RingMode switch
        {
            RingVisibility.Always => 1f,
            RingVisibility.WhileSettingsOpen when plugin.IsSettingsOpen => 1f,
            _ => 0f,
        };

        return Math.Max(flash, steady);
    }

    /// <summary>
    /// Finds the screen position where the world segment from a visible point to a point behind the camera
    /// stops being visible, by bisection.
    /// </summary>
    private Vector2 ClipToCamera(int frontIndex, int backIndex)
    {
        var front = world[frontIndex];
        var back = world[backIndex];
        var best = screen[frontIndex];

        for (var i = 0; i < ClipSteps; i++)
        {
            var mid = Vector3.Lerp(front, back, 0.5f);
            if (Plugin.GameGui.WorldToScreen(mid, out var midScreen, out _))
            {
                front = mid;
                best = midScreen;
            }
            else
            {
                back = mid;
            }
        }

        return best;
    }

    private int FirstBehind()
    {
        for (var i = 0; i < Segments; i++)
        {
            if (!inFront[i])
                return i;
        }

        return 0;
    }

    private static Vector2[] BuildUnitCircle()
    {
        var points = new Vector2[Segments];
        for (var i = 0; i < Segments; i++)
        {
            var angle = MathF.Tau * i / Segments;
            points[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        return points;
    }
}
