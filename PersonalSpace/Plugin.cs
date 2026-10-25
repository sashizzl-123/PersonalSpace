using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PersonalSpace.Core;
using PersonalSpace.Presets;
using PersonalSpace.Sync;
using PersonalSpace.Windows;

namespace PersonalSpace;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static INamePlateGui NamePlateGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/pspace";
    private const string CommandAlias = "/bubble";

    public Configuration Configuration { get; init; }

    /// <summary>Base configuration with the active preset applied. What every controller actually reads.</summary>
    public ActiveSettings Settings { get; init; }

    public readonly WindowSystem WindowSystem = new("PersonalSpace");
    private MainWindow MainWindow { get; init; }
    public PresetEditorWindow PresetEditor { get; init; }

    public BubbleTracker Tracker { get; init; }
    public SyncManager Sync { get; init; }
    public PenumbraShield PenumbraShield { get; init; }
    public PresetEngine Presets { get; init; }
    private FadeController Fade { get; init; }
    private NameplateController Nameplates { get; init; }
    private RingRenderer Ring { get; init; }

    /// <summary>Error types already logged, so a failure that repeats every frame only logs once.</summary>
    private readonly HashSet<Type> reportedErrors = [];

    /// <summary><see cref="Environment.TickCount64"/> of the last radius change, used to flash the preview ring.</summary>
    public long RadiusChangedTick { get; private set; } = long.MinValue;

    public bool IsSettingsOpen => MainWindow.IsOpen;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Radius = Math.Clamp(Configuration.Radius, Configuration.MinRadius, Configuration.MaxRadius);
        Configuration.OutsideOpacity = Math.Clamp(Configuration.OutsideOpacity, Configuration.MinOpacity, Configuration.MaxOpacity);
        Configuration.Presets ??= [];

        Settings = new ActiveSettings();
        Settings.Apply(Configuration, null);

        Tracker = new BubbleTracker(Configuration, Settings);
        Sync = new SyncManager(Settings, Tracker);
        Presets = new PresetEngine(Configuration, Settings, Sync);
        PenumbraShield = new PenumbraShield(Settings, Tracker);
        Fade = new FadeController(Settings, Tracker);
        Nameplates = new NameplateController(Settings, Tracker);
        Ring = new RingRenderer(this, Tracker);

        MainWindow = new MainWindow(this);
        PresetEditor = new PresetEditorWindow(this);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(PresetEditor);

        var help = "Open Personal Space. Also: /pspace toggle · /pspace on|off · /pspace radius <2-100> · /pspace preset [auto|<name>]";
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) { HelpMessage = help });
        CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand) { HelpMessage = $"Alias for {CommandName}.", ShowInHelp = false });

        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // The plugin usually loads at the title screen, so wait for login before opening.
        if (Configuration.OpenOnStartup)
        {
            if (ClientState.IsLoggedIn)
                MainWindow.IsOpen = true;
            else
                ClientState.Login += OpenOnFirstLogin;
        }
    }

    private void OpenOnFirstLogin()
    {
        ClientState.Login -= OpenOnFirstLogin;
        MainWindow.IsOpen = true;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ClientState.Login -= OpenOnFirstLogin;
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        // Restore everyone we've faded or hidden before we go.
        Fade.Dispose();
        PenumbraShield.Dispose();
        Sync.Dispose();
        Nameplates.Dispose();
        Tracker.Dispose();

        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        PresetEditor.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandAlias);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            Presets.Update(framework);
            Tracker.Update();
            Sync.Update();
            PenumbraShield.Update();
            Fade.Update((float)framework.UpdateDelta.TotalSeconds);
            Nameplates.Update();
        }
        catch (Exception ex)
        {
            ReportError(ex, "framework update");

            // Something went wrong mid-frame: stand down so nobody is left faded in a bad state.
            try { Tracker.Clear(); }
            catch (Exception clearEx) { ReportError(clearEx, "reset after error"); }
        }
    }

    private void OnDraw()
    {
        try
        {
            Ring.Draw();
        }
        catch (Exception ex)
        {
            ReportError(ex, "ring drawing");
        }

        WindowSystem.Draw();
    }

    internal void ReportError(Exception ex, string context)
    {
        if (reportedErrors.Add(ex.GetType()))
            Log.Error(ex, $"Error during {context} (further errors of this type won't be logged)");
    }

    public void ToggleMainUi() => MainWindow.Toggle();

    /// <summary>Forces a preset on regardless of its conditions, or returns to automatic with null.</summary>
    public void ForcePreset(Preset? preset)
    {
        Configuration.ForcedPresetId = preset?.Id;
        Configuration.Save();
        Presets.Reevaluate();
    }

    /// <summary>Call after presets are added, removed, reordered or edited.</summary>
    public void PresetsChanged()
    {
        Configuration.Save();
        Presets.Reevaluate();
    }

    public void SetEnabled(bool enabled)
    {
        if (Configuration.Enabled == enabled)
            return;

        Configuration.Enabled = enabled;
        Configuration.Save();
    }

    /// <summary>Clamps and applies a new radius. Does not save, so sliders can call it every frame while dragging.</summary>
    public void SetRadius(float radius)
    {
        radius = Math.Clamp(radius, Configuration.MinRadius, Configuration.MaxRadius);
        if (radius == Configuration.Radius)
            return;

        Configuration.Radius = radius;
        RadiusChangedTick = Environment.TickCount64;
    }

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;

        switch (sub)
        {
            case "":
                ToggleMainUi();
                break;

            case "toggle":
                SetEnabled(!Configuration.Enabled);
                Print($"Personal Space {(Configuration.Enabled ? "enabled" : "disabled")}.");
                break;

            case "on":
            case "off":
                SetEnabled(sub == "on");
                Print($"Personal Space {(Configuration.Enabled ? "enabled" : "disabled")}.");
                break;

            case "radius":
                if (parts.Length < 2 || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var radius))
                {
                    PrintError($"Usage: {CommandName} radius <{Configuration.MinRadius:0}-{Configuration.MaxRadius:0}>  (currently {Configuration.Radius:0.#} yalms)");
                    break;
                }

                SetRadius(radius);
                Configuration.Save();
                Print($"Bubble radius set to {Configuration.Radius:0.#} yalms.");
                break;

            case "preset":
            case "presets":
                OnPresetCommand(parts.Length > 1 ? parts[1] : string.Empty);
                break;

            default:
                PrintError($"Unknown option \"{sub}\". Try {CommandName}, {CommandName} toggle, {CommandName} on|off, {CommandName} radius <n>, or {CommandName} preset [auto|<name>].");
                break;
        }
    }

    private void OnPresetCommand(string arg)
    {
        switch (arg.ToLowerInvariant())
        {
            case "":
            case "list":
                if (Configuration.Presets.Count == 0)
                {
                    Print("No presets yet. Add some on the Presets tab.");
                    break;
                }

                var active = Presets.Active;
                Print(Configuration.PresetsEnabled
                    ? active is null ? "Presets: automatic, none active (base settings)." : $"Presets: \"{active.Name}\" active{(Presets.IsForced ? " (forced)" : "")}."
                    : "Presets are turned off.");
                foreach (var preset in Configuration.Presets)
                    Print($"  {(ReferenceEquals(preset, active) ? "*" : preset.Enabled ? "-" : "x")} {preset.Name}: {preset.DescribeConditions()}");
                break;

            case "auto":
            case "automatic":
                ForcePreset(null);
                Print("Presets back to automatic.");
                break;

            case "on":
            case "off":
                Configuration.PresetsEnabled = arg.ToLowerInvariant() == "on";
                PresetsChanged();
                Print($"Adaptive presets {(Configuration.PresetsEnabled ? "enabled" : "disabled")}.");
                break;

            default:
                var found = Presets.Find(arg);
                if (found is null)
                {
                    PrintError($"No preset called \"{arg}\". {CommandName} preset list shows them.");
                    break;
                }

                if (!Configuration.PresetsEnabled)
                {
                    Configuration.PresetsEnabled = true;
                    Configuration.Save();
                }

                ForcePreset(found);
                Print($"Preset \"{found.Name}\" forced on. {CommandName} preset auto returns to automatic.");
                break;
        }
    }

    private static void Print(string message) => ChatGui.Print(message, "Personal Space");
    private static void PrintError(string message) => ChatGui.PrintError(message, "Personal Space");
}
