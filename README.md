# Personal Space

*Your bubble. Your rules.*

A Dalamud plugin for FINAL FANTASY XIV. Set a radius around your character: players inside it look completely normal,
players outside it fade out (or disappear), lose their nameplates, and can optionally have their synced mods held off.

## Features

- **Fade or hide** everyone outside your bubble, with exemptions for friends, party members and your target.
- **Hide nameplates** outside the bubble.
- **Sync Shield**: show synced players outside the bubble as vanilla without pausing them on the sync server.
- **Crowd performance options** to keep faded players cheap to draw.
- **Adaptive presets** that change settings automatically based on frame rate, crowd size, synced players nearby,
  and where you are.

## Install

1. In game, run `/xlsettings` and open **Experimental**.
2. Under **Custom Plugin Repositories**, add:
   ```
   https://raw.githubusercontent.com/sashizzl-123/DalamudPlugins/main/repo.json
   ```
3. Tick **Enabled**, click **+**, then **Save and Close**.
4. Run `/xlplugins`, search for **Personal Space**, and install it.

## Commands

`/bubble` works as an alias for `/pspace`.

| Command | Does |
| --- | --- |
| `/pspace` | Open the settings window |
| `/pspace toggle` | Turn the bubble on or off |
| `/pspace on` / `off` | Turn the bubble on / off |
| `/pspace radius <2-100>` | Set the bubble radius in yalms |
| `/pspace preset` | List presets |
| `/pspace preset <name>` | Force a preset regardless of its conditions |
| `/pspace preset auto` | Stop forcing; go back to automatic presets |
| `/pspace preset on` / `off` | Enable / disable adaptive presets |

## Sync Shield

Sync Shield makes synced players outside your bubble appear vanilla, **without** using the sync server's pause (which
the other player can see).

### How it works

Every Mare-family client applies a pair's mods through a Penumbra temporary collection. When a synced pair stays
outside the bubble for 3 seconds, Personal Space assigns them an *empty* temporary collection and redraws them, so
their mod files resolve to vanilla. When they come back in, the client's own collection is restored and they're
redrawn again.

- Only Penumbra's public IPC is used (Penumbra.Api 5.15, API v5).
- Pairs are identified through each client's read-only `<Client>.GetHandledAddresses` IPC. Nothing else is touched.
- Sync Shield never hides anyone by itself. Shielded pairs are faded or hidden like everyone else outside the bubble.

### Supported clients

| Client | Status | Notes |
| --- | --- | --- |
| Lightless Sync 3.3 | Works | Only re-applies Glamourer after an outside redraw, so the swap holds. If it re-assigns its collection, the swap is re-applied within a second. Checked against source. |
| Snowcloak 4.1 | Works | Same pattern as Lightless. Its own **Crowd Priority** mode pairs well with this for VRAM limits. Checked against source. |
| MareSempiterne 1.15 | Unverified | Ignores external redraws and re-assigns its collection on every mod apply; the swap is re-applied within a second. Checked against a decompiled binary only. Please report it if a shielded pair still shows mods. |

### Limitations

- **Downloads aren't paused.** Clients still download a pair's files; only applying them is replaced.
- **Only mod files go vanilla.** Models, textures, animations and sounds are swapped out. Glamourer, Customize+,
  Heels, Honorific and Moodles state isn't Penumbra data and stays as the client set it.
- **Each boundary crossing costs a redraw** going out and another coming back, capped at two redraws per frame. A
  3 second debounce and 1 yalm of hysteresis stop players on the edge from flickering.
- **Permanent per-player Penumbra collections** take priority over the client's temporary one. The swap still works,
  but after re-entry that pair's mods only return the next time the client applies them.
- **Full hide uses `Model | Nameplate` (0x802)** on purpose. Snowcloak's global draw block triggers on exactly
  `Model` and the Mare lineage's on exactly `0x800`, so this combination hides one player without stalling mod
  application for everyone else.
- **The Visibility plugin** uses the same flags. If both plugins act on the same player, the last one wins. Personal
  Space only clears flags it set itself.

## Performance in crowds

Fully hiding players is cheap. Keeping them translucent isn't: a faded character still goes through skinning,
materials and every shadow pass. These options, under *Outside the bubble → Performance while translucent*, cut that
cost while keeping the fade:

| Option | Effect |
| --- | --- |
| **Drop their shadows** (on by default) | Disables shadows for faded players and their attachments, using the same flag as the game's *Shadows → Other NPCs → Hide*. Only cleared if this plugin set it, so your global setting is left alone. |
| **Hide mounts, minions and accessories** | The player stays translucent; the heavy extras aren't drawn. |
| **Hide beyond** | A second radius past the bubble. Players between the two stay translucent; anyone farther isn't drawn. Has 1 yalm of hysteresis. |
| **Translucent cap** | Only the nearest N outside players stay translucent. A 3-place margin stops players swapping positions from flickering. |

The game's own settings still matter too: *Character and Object Quantity*, *Character Display Range*, and *Use
low-detail models on distant objects*. For synced pairs, Lightless's model decimation and Snowcloak's texture
shrinking and Crowd Priority reduce mod cost at the source.

## Adaptive presets

A preset is a set of conditions plus the settings to use while those conditions hold. Manage them on the *Presets*
tab and edit one with the pencil button.

- Presets are checked top to bottom a few times a second. **The first match wins.**
- A preset with no conditions always matches, so put one at the bottom as a fallback.
- Presets only **override** the settings they change. Everything else keeps its value from the other tabs, which
  show a note while a preset is overriding something on them.
- **Add examples** creates three disabled presets to start from.

### Conditions

Every ticked condition must hold for the preset to match.

| Condition | Notes |
| --- | --- |
| Frame rate | Smoothed over ~1.5 s. Set "at least" and/or "at most"; an upper bound of 0 means no limit. |
| Players nearby | Counted whether or not the bubble is on, so a preset that turns the bubble off still sees the crowd. |
| Synced players nearby | Read from the sync clients' `GetHandledAddresses` IPC, polled once a second while a preset uses it. Always 0 without a sync client. |
| Kind of place | City, Overworld, Housing district, Inside a house, Inn room, Duty, Island Sanctuary, Other. Tick several to match any of them. |
| Zones | Exact zones; instanced copies count as the same zone. Use "Add current zone" or search. |
| Worlds | The world you're currently on, so it follows world travel. |
| Housing | Any ward/plot or a specific one. Combine with a district zone to pin one house. Apartments report plot 0. |

### What a preset can change

Bubble on/off, radius, preview ring, opacity, fully hide, hide names, drop shadows, hide mounts/minions, hide beyond,
translucent cap, Sync Shield, and the friends/party/target exemptions.

### Timing

Settings under *Timing* keep presets from flipping back and forth:

- **Switch after** (default 3 s): how long a preset must match before it takes over.
- **Return after** (default 8 s): how long the active preset must stop matching before base settings return.
- **Frame-rate margin** (default 10 fps): loosens the active preset's frame-rate limits. Without it, a preset that
  hides the crowd would raise your frame rate and immediately switch itself off.
- **Force**: applies one preset regardless of its conditions (same as `/pspace preset <name>`).

### Place detection

City, Overworld, Inn room and Island Sanctuary come from the zone's TerritoryIntendedUse (0, 1, 2 and 49). Housing is
read live from the game's housing manager, so it's exact. *Duty* is anything bound by duty that isn't one of those;
everything else is *Other*. The *Right now* box on the Presets tab shows what's detected, so a misclassified zone is
easy to spot, and you can always match it exactly by zone instead.

## Building from source

Requires the .NET 10 SDK and an installed XIVLauncher/Dalamud. The `Dalamud.NET.Sdk` finds the dev assemblies under
`%APPDATA%\XIVLauncher\addon\Hooks\dev`.

```bash
dotnet build PersonalSpace/PersonalSpace.csproj -c Release -p:Platform=x64
```

To load your build as a dev plugin:

1. If you installed from the custom repo, uninstall that copy first; the two clash.
2. In `/xlsettings` → **Experimental**, add `PersonalSpace/bin/x64/Release/PersonalSpace.dll` under **Dev Plugin
   Locations**.
3. In `/xlplugins` → **Dev Tools → Installed Dev Plugins**, enable **Personal Space**.
