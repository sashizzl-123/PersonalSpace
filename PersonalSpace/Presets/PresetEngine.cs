using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using PersonalSpace.Core;
using PersonalSpace.Sync;

namespace PersonalSpace.Presets;

/// <summary>
/// Samples frame rate, crowd size, synced-pair count and location a few times a second, picks the first preset
/// whose conditions hold, and lays its overrides over the base configuration in <see cref="ActiveSettings"/>.
/// Switching is debounced: a preset must match for a while before it takes over, and the active one must stop
/// matching for a while before it lets go, so a frame-rate dip or two people walking past doesn't flip settings.
/// </summary>
public sealed unsafe class PresetEngine
{
    /// <summary>How often conditions are re-evaluated.</summary>
    private const long EvalIntervalMs = 250;

    /// <summary>Frame-rate smoothing window. Shorter reacts faster, longer ignores stutter better.</summary>
    private const float FpsWindowSeconds = 1.5f;

    /// <summary>Object table slots [0, 199] belong to the character manager; 0 is always the local player.</summary>
    private const int CharacterManagerSlots = 200;

    private readonly Configuration config;
    private readonly ActiveSettings settings;
    private readonly SyncManager sync;

    private long nextEvalTick;
    private bool hasPending;
    private Preset? pending;
    private long pendingSinceTick;

    public PresetEngine(Configuration config, ActiveSettings settings, SyncManager sync)
    {
        this.config = config;
        this.settings = settings;
        this.sync = sync;
    }

    /// <summary>Smoothed frames per second.</summary>
    public float Fps { get; private set; }

    /// <summary>What the last evaluation saw.</summary>
    public PresetContext Context { get; private set; }

    /// <summary>The preset currently applied, or null for base settings.</summary>
    public Preset? Active { get; private set; }

    /// <summary>The preset about to take over (or null for "about to return to base"), while the dwell timer runs.</summary>
    public Preset? Pending => hasPending ? pending : null;

    /// <summary>True while a switch is waiting on its dwell timer.</summary>
    public bool IsPendingSwitch => hasPending;

    /// <summary>Seconds until the pending switch happens.</summary>
    public float PendingSecondsLeft
    {
        get
        {
            if (!hasPending)
                return 0f;

            var delay = pending is null ? config.PresetDeactivateSeconds : config.PresetActivateSeconds;
            return Math.Max(0f, delay - (Environment.TickCount64 - pendingSinceTick) / 1000f);
        }
    }

    /// <summary>True when the manually forced preset is in effect.</summary>
    public bool IsForced => config.PresetsEnabled && config.ForcedPresetId.HasValue;

    public void Update(IFramework framework)
    {
        UpdateFps((float)framework.UpdateDelta.TotalSeconds);

        var now = Environment.TickCount64;
        if (now >= nextEvalTick)
        {
            nextEvalTick = now + EvalIntervalMs;
            Evaluate(now);
        }

        settings.Apply(config, Active);
    }

    /// <summary>Drops the dwell timer so the next evaluation applies immediately. Used when the user edits presets.</summary>
    public void Reevaluate()
    {
        hasPending = false;
        nextEvalTick = 0;
    }

    /// <summary>Finds a preset by name, case-insensitively. Null if there's no such preset.</summary>
    public Preset? Find(string name)
    {
        foreach (var preset in config.Presets)
        {
            if (string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
                return preset;
        }

        return null;
    }

    private Preset? Find(Guid id)
    {
        foreach (var preset in config.Presets)
        {
            if (preset.Id == id)
                return preset;
        }

        return null;
    }

    private void UpdateFps(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
            return;

        var instant = 1f / deltaSeconds;
        if (Fps <= 0f)
        {
            Fps = instant;
            return;
        }

        // Exponential moving average with a time constant of FpsWindowSeconds.
        var weight = Math.Min(1f, deltaSeconds / FpsWindowSeconds);
        Fps += (instant - Fps) * weight;
    }

    private void Evaluate(long now)
    {
        Context = Gather();

        // Sync clients only need to be polled for pairs when some preset counts them (Sync Shield polls on its own).
        sync.PollPairsForPresets = config.PresetsEnabled && AnyPresetCountsPairs();

        Preset? candidate = null;
        var immediate = false;

        if (config.PresetsEnabled)
        {
            if (config.ForcedPresetId is { } forcedId)
            {
                candidate = Find(forcedId);
                if (candidate is null)
                {
                    // The forced preset was deleted: fall back to automatic.
                    config.ForcedPresetId = null;
                    config.Save();
                }
                else
                {
                    immediate = true;
                }
            }

            if (!immediate)
            {
                foreach (var preset in config.Presets)
                {
                    // The active preset gets slack on its frame-rate limits: hiding the crowd raises the frame rate,
                    // and without this it would stop matching the moment it worked.
                    var fpsMargin = ReferenceEquals(preset, Active) ? config.PresetFpsMargin : 0f;
                    if (preset.Matches(Context, fpsMargin))
                    {
                        candidate = preset;
                        break;
                    }
                }
            }
        }
        else
        {
            immediate = true; // Turning presets off drops back to base settings straight away.
        }

        if (ReferenceEquals(candidate, Active))
        {
            hasPending = false;
            return;
        }

        if (immediate)
        {
            Switch(candidate);
            return;
        }

        if (!hasPending || !ReferenceEquals(pending, candidate))
        {
            hasPending = true;
            pending = candidate;
            pendingSinceTick = now;
            return;
        }

        var delaySeconds = candidate is null ? config.PresetDeactivateSeconds : config.PresetActivateSeconds;
        if (now - pendingSinceTick >= (long)(delaySeconds * 1000f))
            Switch(candidate);
    }

    private void Switch(Preset? preset)
    {
        hasPending = false;
        if (ReferenceEquals(preset, Active))
            return;

        Active = preset;
        Plugin.Log.Debug(preset is null ? "Preset: back to base settings" : $"Preset: \"{preset.Name}\" active");
    }

    private bool AnyPresetCountsPairs()
    {
        foreach (var preset in config.Presets)
        {
            if (preset.Enabled && preset.ModdedPlayers.Enabled)
                return true;
        }

        return false;
    }

    private PresetContext Gather()
    {
        var loggedIn = Plugin.ClientState.IsLoggedIn && Plugin.ObjectTable.GetObjectAddress(0) != nint.Zero;
        if (!loggedIn)
            return new PresetContext(false, Fps, 0, 0, 0, 0, PlaceKind.Other, false, 0, 0);

        var local = (Character*)Plugin.ObjectTable.GetObjectAddress(0);
        uint worldId = local->CurrentWorld;

        var players = 0;
        var modded = 0;
        for (var i = 1; i < CharacterManagerSlots; i++)
        {
            var address = Plugin.ObjectTable.GetObjectAddress(i);
            if (address == nint.Zero || ((GameObject*)address)->ObjectKind != ObjectKind.Pc)
                continue;

            players++;
            if (sync.IsPair(address))
                modded++;
        }

        var territoryId = Plugin.ClientState.TerritoryType;
        var place = Location.Classify(territoryId, out var inHousing, out var ward, out var plot);

        return new PresetContext(true, Fps, players, modded, worldId, territoryId, place, inHousing, ward, plot);
    }
}
