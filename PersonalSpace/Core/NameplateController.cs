using System;
using System.Collections.Generic;
using Dalamud.Game.Gui.NamePlate;

namespace PersonalSpace.Core;

/// <summary>
/// Empties the nameplates of players outside the bubble: name, title, FC tag and online-status icon.
/// Marker icons (e.g. party signs) are left alone.
/// </summary>
public sealed class NameplateController : IDisposable
{
    private readonly ActiveSettings settings;
    private readonly BubbleTracker tracker;

    /// <summary>Players whose names should currently be hidden. Read by the nameplate hook.</summary>
    private readonly HashSet<ulong> hiddenNames = new(128);

    private bool redrawPending;

    public NameplateController(ActiveSettings settings, BubbleTracker tracker)
    {
        this.settings = settings;
        this.tracker = tracker;
        tracker.Reset += OnReset;
        tracker.PlayerRemoved += OnPlayerRemoved;
        Plugin.NamePlateGui.OnNamePlateUpdate += OnNamePlateUpdate;
    }

    public void Dispose()
    {
        Plugin.NamePlateGui.OnNamePlateUpdate -= OnNamePlateUpdate;
        tracker.Reset -= OnReset;
        tracker.PlayerRemoved -= OnPlayerRemoved;

        // Bring every hidden name back.
        if (hiddenNames.Count > 0)
            Plugin.NamePlateGui.RequestRedraw();
        hiddenNames.Clear();
    }

    /// <summary>
    /// Works out whose names should be hidden this frame. Nameplates only re-run their update when the game thinks
    /// something changed, so a redraw is requested whenever anyone crosses the boundary.
    /// </summary>
    public void Update()
    {
        if (tracker.IsActive)
        {
            foreach (var player in tracker.Players)
            {
                var hide = settings.HideNames && player.ShouldHide;
                var changed = hide ? hiddenNames.Add(player.GameObjectId) : hiddenNames.Remove(player.GameObjectId);
                redrawPending |= changed;
            }
        }

        if (redrawPending)
        {
            redrawPending = false;
            Plugin.NamePlateGui.RequestRedraw();
        }
    }

    private void OnNamePlateUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (hiddenNames.Count == 0)
            return;

        // Indexed loop: no enumerator allocation.
        for (var i = 0; i < handlers.Count; i++)
        {
            var handler = handlers[i];
            if (handler.NamePlateKind != NamePlateKind.PlayerCharacter || !hiddenNames.Contains(handler.GameObjectId))
                continue;

            handler.RemoveName();
            handler.RemoveTitle();
            handler.RemoveFreeCompanyTag();

            // The online-status icon (roleplaying, mentor, new adventurer…) and the bits around the name.
            handler.NameIconId = -1;
            handler.RemoveStatusPrefix();
            handler.RemoveLevelPrefix();
            handler.RemoveTargetSuffix();
        }
    }

    private void OnReset()
    {
        if (hiddenNames.Count == 0)
            return;

        hiddenNames.Clear();
        redrawPending = true;
    }

    /// <summary>Their nameplate is gone with them, so no redraw is needed.</summary>
    private void OnPlayerRemoved(TrackedPlayer player) => hiddenNames.Remove(player.GameObjectId);
}
