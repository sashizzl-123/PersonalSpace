using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace PersonalSpace.Core;

/// <summary>
/// Fades (or fully hides) players outside the bubble and restores them when they come back.
/// Also trims what a faded player costs to draw: no shadow casting, optionally no mount/minion/accessory, and a full
/// hide past a second radius or beyond a translucent-count cap (decided by <see cref="BubbleTracker"/>).
/// Only players this controller has changed are ever touched on restore.
/// </summary>
public sealed unsafe class FadeController : IDisposable
{
    /// <summary>Time for a full 0 → 1 alpha transition.</summary>
    private const float FadeSeconds = 0.25f;

    /// <summary>
    /// Same flags the Visibility plugin uses to stop the game drawing a character and its nameplate. Keep both bits:
    /// Mare-family sync clients read RenderFlags to decide whether a character is still loading, and setting only one bit
    /// would trip a client's *global* draw block (Snowcloak checks for exactly Model, the Mare lineage for exactly 0x800),
    /// stalling mod application for every pair. The combination matches neither exact check.
    /// </summary>
    private const VisibilityFlags HideFlags = VisibilityFlags.Model | VisibilityFlags.Nameplate;

    /// <summary>Bits in the per-player ownership masks: the player and their three attachment slots.</summary>
    private const int SelfSlot = 0;
    private const int MountSlot = 1;
    private const int MinionSlot = 2;
    private const int OrnamentSlot = 3;

    private readonly ActiveSettings settings;
    private readonly BubbleTracker tracker;
    private readonly Dictionary<ulong, FadeState> states = new(128);

    public FadeController(ActiveSettings settings, BubbleTracker tracker)
    {
        this.settings = settings;
        this.tracker = tracker;
        tracker.Reset += RestoreAll;
        tracker.PlayerRemoved += OnPlayerRemoved;
    }

    public void Dispose()
    {
        RestoreAll();
        tracker.Reset -= RestoreAll;
        tracker.PlayerRemoved -= OnPlayerRemoved;
    }

    public void Update(float deltaSeconds)
    {
        if (!tracker.IsActive)
            return;

        // Clamp so a hitch doesn't snap the fade.
        var step = Math.Min(deltaSeconds, 0.1f) / FadeSeconds;

        foreach (var player in tracker.Players)
        {
            var hide = player.ShouldHide;
            if (!states.TryGetValue(player.GameObjectId, out var state))
            {
                if (!hide)
                    continue;

                state = new FadeState(player.GameObjectId);
                states.Add(player.GameObjectId, state);
            }

            var chara = (Character*)player.Address;
            state.ObjectIndex = chara->GameObject.ObjectIndex;

            var fullyHide = settings.FullyHide || player.ShouldHideFully;
            var target = !hide ? 1f : fullyHide ? 0f : settings.OutsideOpacity;
            state.Alpha = MoveTowards(state.Alpha, target, step);

            // Fully hide: fade out first, then stop the game drawing them. Undo it the moment they shouldn't be hidden.
            var wantHidden = hide && fullyHide && state.Alpha <= 0f;
            SetHideFlags(&chara->GameObject, state, SelfSlot, wantHidden);

            // A translucent player still costs full shadow passes; a hidden one casts none anyway.
            var wantShadowsOff = hide && !wantHidden && settings.DisableShadowsOutside;
            SetShadows(chara, state, SelfSlot, wantShadowsOff);

            // Attachments: hidden with the player, or on their own when the option is on. Shadows follow the player.
            var wantAttachmentsHidden = hide && (wantHidden || settings.HideAttachmentsOutside);
            ApplyToAttachment(chara->Mount.MountObject, state, MountSlot, wantAttachmentsHidden, wantShadowsOff);
            ApplyToAttachment((Character*)chara->CompanionData.CompanionObject, state, MinionSlot, wantAttachmentsHidden, wantShadowsOff);
            ApplyToAttachment((Character*)chara->OrnamentData.OrnamentObject, state, OrnamentSlot, wantAttachmentsHidden, wantShadowsOff);

            // Re-applied every frame because the game can reset it.
            SetAlpha(chara, state.Alpha);

            // Fully faded back in and nothing owned any more: hand the player back to the game.
            if (!hide && state.Alpha >= 1f && state.HideOwned == 0 && state.ShadowOwned == 0)
                states.Remove(player.GameObjectId);
        }
    }

    /// <summary>Restores every player this controller has changed, if they're still in the object table.</summary>
    public void RestoreAll()
    {
        foreach (var state in states.Values)
        {
            var address = Plugin.ObjectTable.GetObjectAddress(state.ObjectIndex);
            if (address == nint.Zero)
                continue;

            var chara = (Character*)address;

            // The slot may have been reused by someone else since we last saw this player.
            if (chara->GameObject.GetGameObjectId().Id != state.GameObjectId)
                continue;

            SetHideFlags(&chara->GameObject, state, SelfSlot, false);
            SetShadows(chara, state, SelfSlot, false);
            ApplyToAttachment(chara->Mount.MountObject, state, MountSlot, false, false);
            ApplyToAttachment((Character*)chara->CompanionData.CompanionObject, state, MinionSlot, false, false);
            ApplyToAttachment((Character*)chara->OrnamentData.OrnamentObject, state, OrnamentSlot, false, false);
            SetAlpha(chara, 1f);
        }

        states.Clear();
    }

    /// <summary>The player left the object table, so there's nothing left to restore.</summary>
    private void OnPlayerRemoved(TrackedPlayer player) => states.Remove(player.GameObjectId);

    private static void ApplyToAttachment(Character* attachment, FadeState state, int slot, bool hidden, bool shadowsOff)
    {
        if (attachment == null)
        {
            // Dismounted / minion put away: the object is gone and took our flags with it.
            state.HideOwned &= (byte)~(1 << slot);
            state.ShadowOwned &= (byte)~(1 << slot);
            return;
        }

        SetHideFlags(&attachment->GameObject, state, slot, hidden);
        SetShadows(attachment, state, slot, shadowsOff);
    }

    /// <summary>Sets or clears the hide render flags, remembering whether we were the ones who set them.</summary>
    private static void SetHideFlags(GameObject* obj, FadeState state, int slot, bool want)
    {
        var bit = (byte)(1 << slot);
        var owned = (state.HideOwned & bit) != 0;

        if (want)
        {
            if (owned)
                obj->RenderFlags |= HideFlags; // Re-assert: the game can reset flags on a redraw.
            else if ((obj->RenderFlags & HideFlags) != HideFlags)
            {
                obj->RenderFlags |= HideFlags;
                state.HideOwned |= bit;
            }
        }
        else if (owned)
        {
            obj->RenderFlags &= ~HideFlags;
            state.HideOwned &= (byte)~bit;
        }
    }

    /// <summary>
    /// Sets or clears the draw object's "casts no shadows" state, the same bit the game's own Shadows → Other NPCs
    /// setting uses. Only cleared again if we set it, so a global "hide" setting is left alone.
    /// </summary>
    private static void SetShadows(Character* chara, FadeState state, int slot, bool want)
    {
        var bit = (byte)(1 << slot);
        var owned = (state.ShadowOwned & bit) != 0;

        var drawObject = chara->GameObject.DrawObject;
        if (drawObject == null || drawObject->GetObjectType() != FFXIVClientStructs.FFXIV.Client.Graphics.Scene.ObjectType.CharacterBase)
        {
            // No draw object right now (loading / redrawing). It comes back with the game's default, so we own nothing.
            state.ShadowOwned &= (byte)~bit;
            return;
        }

        var characterBase = (CharacterBase*)drawObject;
        if (want)
        {
            if (owned)
                characterBase->StateFlags |= CharacterBase.StateFlag.ShadowsDisabled; // Re-assert after a redraw.
            else if ((characterBase->StateFlags & CharacterBase.StateFlag.ShadowsDisabled) == 0)
            {
                characterBase->StateFlags |= CharacterBase.StateFlag.ShadowsDisabled;
                state.ShadowOwned |= bit;
            }
        }
        else if (owned)
        {
            characterBase->StateFlags &= ~CharacterBase.StateFlag.ShadowsDisabled;
            state.ShadowOwned &= (byte)~bit;
        }
    }

    /// <summary>Applies alpha to the character and everything attached to them: mount, minion, fashion accessory.</summary>
    private static void SetAlpha(Character* chara, float alpha)
    {
        chara->Alpha = alpha;

        var mount = chara->Mount.MountObject;
        if (mount != null)
            mount->Alpha = alpha;

        var minion = chara->CompanionData.CompanionObject;
        if (minion != null)
            ((Character*)minion)->Alpha = alpha;

        var ornament = chara->OrnamentData.OrnamentObject;
        if (ornament != null)
            ((Character*)ornament)->Alpha = alpha;

        var child = chara->ChildObject;
        if (child != null)
            child->Alpha = alpha;
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta)
            return target;

        return current + MathF.Sign(target - current) * maxDelta;
    }

    private sealed class FadeState(ulong gameObjectId)
    {
        public ulong GameObjectId { get; } = gameObjectId;
        public ushort ObjectIndex { get; set; }
        public float Alpha { get; set; } = 1f;

        /// <summary>Per-slot bits: which objects' hide flags we set.</summary>
        public byte HideOwned { get; set; }

        /// <summary>Per-slot bits: which objects' shadow-disabled state we set.</summary>
        public byte ShadowOwned { get; set; }
    }
}
