# Personal Space 🫧

*Your bubble. Your rules.*

Busy city? Crowded event? Personal Space gives you a little bubble around your character. Everyone inside it looks
totally normal. Everyone outside it fades into the background, so you can focus on the people you're actually with.

> **Heads up:** this plugin was made with AI (Claude). The idea is free for anyone to use, copy, remix or build on.
> No credit needed, though it's always nice!

## What it can do

- 👻 **Fade or hide** players outside your bubble
- 🏷️ **Hide their nameplates** so the screen isn't a wall of names
- 🤝 **Keep friends, party members and your target visible**, even when they're outside
- 🛡️ **Sync Shield**: show synced players outside your bubble without their mods, without pausing them
- ⚡ **Run smoother in crowds** with options that make faded players cheaper to draw
- 🔄 **Presets** that switch settings for you, like "hide more when my FPS drops" or "turn off at home"

## Install

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this link:
   ```
   https://moonbright.cc/repo/pluginmaster.json
   ```
3. Tick **Enabled**, click **+**, then **Save and Close**.
4. Type `/xlplugins`, search for **Personal Space**, and click install.

That's it! Type `/pspace` to open the settings.

## Commands

You can use `/bubble` anywhere you'd use `/pspace`.

| Type this | To do this |
| --- | --- |
| `/pspace` | Open the settings |
| `/pspace toggle` | Turn your bubble on or off |
| `/pspace on` or `/pspace off` | Turn your bubble on or off |
| `/pspace radius 15` | Set your bubble size (2 to 100 yalms) |
| `/pspace preset` | See your presets |
| `/pspace preset <name>` | Use a specific preset right now |
| `/pspace preset auto` | Go back to picking presets automatically |
| `/pspace preset on` or `off` | Turn presets on or off |

## Sync Shield

If you use a sync client like Lightless or Snowcloak, Sync Shield can show synced players outside your bubble
**without their mods**. When they step back inside, their mods come right back.

It doesn't pause anyone on the sync server, so nobody gets a notification and nothing changes on their end. It all
happens on your screen through Penumbra.

| Sync client | Works? |
| --- | --- |
| Lightless Sync | ✅ Yes |
| Snowcloak | ✅ Yes |
| MareSempiterne | 🤔 Should work, not fully tested. Please let me know if it doesn't! |

**Good to know:**

- Their mods still **download** in the background. They just aren't shown until the player comes closer.
- Only **mod files** are hidden (models, textures, animations, sounds). Glamourer, Customize+, Heels, Honorific and
  Moodles changes still show.
- Someone has to stay outside for **3 seconds** before their mods are hidden, so people walking along the edge
  don't flicker.
- If you also use the **Visibility** plugin, the two can overlap when both try to hide the same player.

## Smoother crowds

Completely hiding players is easy on your PC. Fading them is surprisingly expensive. These options help, under
**Outside the bubble → Performance while translucent**:

- **Drop their shadows** (on by default): faded players don't cast shadows.
- **Hide mounts, minions and accessories**: keep the player, skip the extras.
- **Hide beyond**: pick a second, bigger distance. Anyone past it isn't drawn at all.
- **Translucent cap**: only fade the closest few players and hide the rest.

The game's own **Character and Object Quantity** and **Character Display Range** settings help too.

## Presets

Presets change your settings automatically depending on what's going on. Each preset has a few **conditions** ("when
this is true...") and **settings** ("...use these").

You can base a preset on:

- Your **frame rate**
- How many **players** are nearby
- How many **synced players** are nearby
- The **kind of place** you're in: city, overworld, housing, inn, duty, Island Sanctuary...
- Specific **zones**, **worlds** or **houses**

A few tips:

- Presets are checked **top to bottom**, and the first one that matches wins.
- A preset with no conditions always matches, so it works as a fallback at the bottom of the list.
- A preset only changes the settings you tell it to. Everything else stays the same.
- Click **Add examples** for three starter presets you can copy and tweak.
- Presets wait a few seconds before switching, so they don't flip back and forth.
- Not sure what the plugin thinks your location is? The **Right now** box on the Presets tab shows you.

## For developers

### Building

You'll need the .NET 10 SDK and XIVLauncher installed.

```bash
dotnet build PersonalSpace/PersonalSpace.csproj -c Release -p:Platform=x64
```

To test your build in game, uninstall the regular version first, then add
`PersonalSpace/bin/x64/Release/PersonalSpace.dll` under **Dev Plugin Locations** in `/xlsettings` → **Experimental**.

### Publishing

1. Bump `<Version>` in `PersonalSpace/PersonalSpace.csproj`. Without that, players won't be offered the update.
2. Run `tools/publish-plugin.sh` on the web server. It builds the plugin, copies it into
   `/var/www/moonbright.cc/personalspace/` (served at `https://moonbright.cc/personalspace/`), and runs
   `tools/combine-repos.sh` to refresh the shared `https://moonbright.cc/repo/pluginmaster.json`.

Plugin zips aren't committed, but `repo/pluginmaster.json` is.
