using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using PersonalSpace.Presets;

namespace PersonalSpace;

public enum RingVisibility
{
    WhileSettingsOpen,
    Always,
    Never,
}

/// <summary>The game's object highlight colours. Values match FFXIVClientStructs' ObjectHighlightColor.</summary>
public enum OutlineColor : byte
{
    Red = 1,
    Green = 2,
    Blue = 3,
    Yellow = 4,
    Orange = 5,
    Magenta = 6,
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const float MinRadius = 2f;
    public const float MaxRadius = 100f;
    public const float MinOpacity = 0.05f;
    public const float MaxOpacity = 1f;

    public int Version { get; set; } = 1;

    // Master switch
    public bool Enabled { get; set; } = true;

    // Bubble
    public float Radius { get; set; } = 20f;
    public RingVisibility RingMode { get; set; } = RingVisibility.WhileSettingsOpen;

    // Not shown in the UI; the defaults are what's wanted. Still editable in the config JSON.
    public Vector4 RingColor { get; set; } = new(0.36f, 0.78f, 0.85f, 0.85f);
    public float RingThickness { get; set; } = 2f;

    // Outside the bubble
    public float OutsideOpacity { get; set; } = 0.15f;
    public bool FullyHide { get; set; } = false;
    public bool HideNames { get; set; } = true;

    // Performance: cheaper ways to draw the crowd outside the bubble while keeping them translucent.
    public const float MaxHideBeyond = 100f;
    public const int MaxTranslucentCap = 50;

    /// <summary>Stop faded players (and their mounts/minions) casting shadows.</summary>
    public bool DisableShadowsOutside { get; set; } = true;

    /// <summary>Hide faded players' mounts, minions and fashion accessories while the player stays translucent.</summary>
    public bool HideAttachmentsOutside { get; set; } = false;

    /// <summary>Fully hide players more than this many yalms past the bubble edge. 0 = never.</summary>
    public float HideBeyond { get; set; } = 0f;

    /// <summary>Keep at most this many nearest outside players translucent; fully hide the rest. 0 = no cap.</summary>
    public int MaxTranslucent { get; set; } = 0;

    // Sync Shield
    public bool SyncShield { get; set; } = false;

    // Exemptions
    public bool ExemptFriends { get; set; } = true;
    public bool ExemptParty { get; set; } = true;
    public bool ExemptTarget { get; set; } = true;

    /// <summary>Players that are never faded, stored as "Name@World".</summary>
    public List<string> AlwaysShow { get; set; } = [];

    // Target alerts: someone else has you targeted.
    public const float MaxTargetAlertLingerSeconds = 30f;

    /// <summary>Print a chat line when another player targets you.</summary>
    public bool TargetAlertChat { get; set; } = true;

    /// <summary>Outline players who have you targeted.</summary>
    public bool TargetAlertOutline { get; set; } = true;

    /// <summary>How long the outline stays after they stop targeting you.</summary>
    public float TargetAlertLingerSeconds { get; set; } = 5f;

    public OutlineColor TargetAlertColor { get; set; } = OutlineColor.Yellow;

    /// <summary>Players being outlined aren't faded or hidden, so the outline can be seen.</summary>
    public bool TargetAlertShowNormally { get; set; } = true;

    /// <summary>Party members targeting you don't trigger alerts.</summary>
    public bool TargetAlertIgnoreParty { get; set; } = true;

    /// <summary>Friends targeting you don't trigger alerts.</summary>
    public bool TargetAlertIgnoreFriends { get; set; } = false;

    /// <summary>No alerts while you're in combat.</summary>
    public bool TargetAlertPauseInCombat { get; set; } = true;

    /// <summary>No alerts while you're in a duty or other instanced content.</summary>
    public bool TargetAlertPauseInDuties { get; set; } = true;

    // Adaptive presets
    public const float MaxPresetDelaySeconds = 60f;

    /// <summary>Master switch for adaptive presets. Off = the base settings above are always used.</summary>
    public bool PresetsEnabled { get; set; } = true;

    /// <summary>User-defined presets, in priority order: the first whose conditions hold is applied.</summary>
    public List<Preset> Presets { get; set; } = [];

    /// <summary>A preset to apply regardless of its conditions, or null for automatic.</summary>
    public Guid? ForcedPresetId { get; set; }

    /// <summary>A preset's conditions must hold this long before it takes over.</summary>
    public float PresetActivateSeconds { get; set; } = 3f;

    /// <summary>The active preset's conditions must fail this long before it lets go.</summary>
    public float PresetDeactivateSeconds { get; set; } = 8f;

    public const float MaxPresetFpsMargin = 30f;

    /// <summary>
    /// An active preset's frame-rate limits are relaxed by this much, so a preset that raises the frame rate by hiding
    /// the crowd doesn't immediately stop matching and switch itself off.
    /// </summary>
    public float PresetFpsMargin { get; set; } = 10f;

    // Window
    /// <summary>Open the settings window on the first login after the game starts.</summary>
    public bool OpenOnStartup { get; set; } = false;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
