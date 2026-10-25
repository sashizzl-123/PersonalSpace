using System;
using PersonalSpace.Presets;

namespace PersonalSpace.Core;

/// <summary>
/// The settings actually in effect this frame: the base <see cref="Configuration"/> with the active preset's
/// overrides laid on top. Everything that fades, hides or shields players reads from here, never from the
/// configuration directly, so a preset can change behaviour without touching what the user saved.
/// </summary>
public sealed class ActiveSettings
{
    public bool Enabled { get; private set; } = true;
    public float Radius { get; private set; } = 20f;
    public RingVisibility RingMode { get; private set; }
    public float OutsideOpacity { get; private set; } = 0.15f;
    public bool FullyHide { get; private set; }
    public bool HideNames { get; private set; } = true;
    public bool DisableShadowsOutside { get; private set; } = true;
    public bool HideAttachmentsOutside { get; private set; }
    public float HideBeyond { get; private set; }
    public int MaxTranslucent { get; private set; }
    public bool SyncShield { get; private set; }
    public bool ExemptFriends { get; private set; } = true;
    public bool ExemptParty { get; private set; } = true;
    public bool ExemptTarget { get; private set; } = true;

    /// <summary>The preset whose overrides are applied, or null for plain base settings.</summary>
    public Preset? ActivePreset { get; private set; }

    /// <summary>Convenience for the UI: the active preset's overrides, or null.</summary>
    public PresetOverrides? Overrides => ActivePreset?.Overrides;

    /// <summary>Recomputes every value. Cheap, so it runs each frame: the UI edits the base config live.</summary>
    public void Apply(Configuration config, Preset? preset)
    {
        ActivePreset = preset;
        var o = preset?.Overrides;

        Enabled = o?.Enabled ?? config.Enabled;
        Radius = Math.Clamp(o?.Radius ?? config.Radius, Configuration.MinRadius, Configuration.MaxRadius);
        RingMode = o?.RingMode ?? config.RingMode;
        OutsideOpacity = Math.Clamp(o?.OutsideOpacity ?? config.OutsideOpacity, Configuration.MinOpacity, Configuration.MaxOpacity);
        FullyHide = o?.FullyHide ?? config.FullyHide;
        HideNames = o?.HideNames ?? config.HideNames;
        DisableShadowsOutside = o?.DisableShadowsOutside ?? config.DisableShadowsOutside;
        HideAttachmentsOutside = o?.HideAttachmentsOutside ?? config.HideAttachmentsOutside;
        HideBeyond = Math.Clamp(o?.HideBeyond ?? config.HideBeyond, 0f, Configuration.MaxHideBeyond);
        MaxTranslucent = Math.Clamp(o?.MaxTranslucent ?? config.MaxTranslucent, 0, Configuration.MaxTranslucentCap);
        SyncShield = o?.SyncShield ?? config.SyncShield;
        ExemptFriends = o?.ExemptFriends ?? config.ExemptFriends;
        ExemptParty = o?.ExemptParty ?? config.ExemptParty;
        ExemptTarget = o?.ExemptTarget ?? config.ExemptTarget;
    }
}
