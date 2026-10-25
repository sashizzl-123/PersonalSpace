# Personal Space

*Your bubble. Your rules.* A personal Dalamud plugin for FINAL FANTASY XIV. Players outside a radius around you are
faded (or hidden), lose their nameplate text, and, optionally, have their mod syncing held off. Players inside look
completely normal. Adaptive presets can change any of that automatically depending on frame rate, crowd size, how many
synced players are around, and where you are. Commands: `/pspace` (alias `/bubble`), `/pspace toggle`,
`/pspace radius <n>`, `/pspace preset [list|auto|on|off|<name>]`.

## Build

Requires the .NET 10 SDK and an installed XIVLauncher/Dalamud (the `Dalamud.NET.Sdk` project SDK finds the dev
assemblies under `%APPDATA%\XIVLauncher\addon\Hooks\dev`).

```bash
dotnet build PersonalSpace/PersonalSpace.csproj -c Release -p:Platform=x64
```

## Install as a dev plugin

1. In game, run `/xlsettings`, open **Experimental**, and add the built DLL
   (`PersonalSpace/bin/x64/Release/PersonalSpace.dll`) under **Dev Plugin Locations**.
2. Run `/xlplugins`, open **Dev Tools → Installed Dev Plugins**, and enable **Personal Space**.
3. Open the settings with `/pspace`.

## Sync Shield: how it works and what it can't do

No Mare-family client offers a local, client-side "pause this pair" IPC. Every pause they do have goes through the
sync server and is visible to the other player, which this plugin refuses to use. Sync Shield therefore works at the
one place every client ends up: Penumbra.

Every Mare-family client applies a pair's mods through a Penumbra temporary collection assigned to that pair. Synced
pairs who stay outside the bubble for 3 seconds get an *empty* temporary collection force-assigned instead and are
redrawn, so their mod files resolve to vanilla no matter which client is syncing them. On re-entry the client's own
collection is assigned back and the pair redrawn. Shielded pairs are otherwise treated like everyone else outside the
bubble (faded or hidden according to your settings); Sync Shield never hides anyone by itself.

Only Penumbra's public IPC is used (Penumbra.Api 5.15, API v5). Pairs are identified through each client's public
`<Client>.GetHandledAddresses` IPC, which is read-only and needs no registration. Nothing else of theirs is touched.

| Client | Badge | Notes | Checked against |
| --- | --- | --- | --- |
| Lightless Sync 3.3 | Vanilla via Penumbra | After an outside redraw it only re-applies Glamourer, so the swap holds. Re-assigns its collection when new data arrives; the swap is re-applied within a second. | Source (`PairRedrawManager`, `PairPlayerRealizationRunner`) |
| Snowcloak 4.1 | Vanilla via Penumbra | Same pattern. Its own **Crowd Priority** mode is a good complement for VRAM limits. | Source (`ModApplicator`, `ObjectTableCache`) |
| MareSempiterne 1.15 | Vanilla via Penumbra (unverified) | Does not react to external redraws at all. Re-assigns its collection on every mod apply; the swap is re-applied within a second. | Decompiled binary |

### Known limitations

- **Downloads are not paused.** Every client still downloads a pair's files; only applying them is replaced.
- **Appearance edits stay.** Glamourer, Customize+, Heels, Honorific and Moodles state is not Penumbra data and is left
  as the client set it. Only mod files (models, textures, animations, sounds) become vanilla.
- **Each boundary crossing costs a redraw** out and another back in, capped at two redraws per frame. The 3 second
  debounce and the 1 yalm hysteresis keep edge-dancers from thrashing.
- **A permanent per-player Penumbra collection** on a pair makes Penumbra report that one instead of the client's
  temporary collection. The swap still works, but after re-entry the client's mods only return when it next applies.
- **Full hide uses `Model | Nameplate`** (0x802) on purpose. Snowcloak's global draw block triggers on exactly `Model`
  and the Mare lineage's on exactly `0x800`; the combination matches neither, so hiding one player doesn't stall mod
  application for everyone else.
- **The Visibility plugin** hides with the same flags. If both plugins hide or restore the same player, the last
  writer wins; Personal Space only clears flags it set itself.
- **MareSempiterne is decompiled, not source-verified.** If a shielded pair still shows mods, please report it.

## Performance in crowds

Fully hiding the crowd outside the bubble is cheap; keeping them translucent is not, because an alpha-faded character
still goes through skinning, materials and every shadow pass. The "Performance while translucent" options under
*Outside the bubble* trim that cost while keeping the fade:

- **Drop their shadows** (default on): faded players and their attachments get the draw object's `ShadowsDisabled`
  state, the same bit the game's *Shadows → Other NPCs → Hide* setting uses, but only outside the bubble. The bit is
  only cleared again if this plugin set it, so a global "hide" setting is left alone.
- **Hide mounts, minions and accessories**: the rider stays translucent, the heavy stuff isn't drawn.
- **Hide beyond**: a second radius past the bubble edge. The band in between stays translucent, everyone farther is
  not drawn. 1 yalm of hysteresis, like the bubble itself.
- **Translucent cap**: only the nearest N outside players stay translucent. Ranks have a 3-place margin so people
  swapping positions don't flicker.

The game's own knobs still matter: *Character and Object Quantity*, *Character Display Range*, and *Use low-detail
models on distant objects*. For synced pairs, Lightless's model decimation and Snowcloak's texture shrinking plus
Crowd Priority cut mod cost at the source.

## Adaptive presets

A preset is a set of conditions plus the settings to use while they hold. The *Presets* tab lists them; the editor
window (pencil button) sets them up. Presets are checked top to bottom a few times a second and the first match wins.
A preset with no conditions always matches, so one at the bottom works as a fallback. Everything a preset changes is
an *override*: untouched settings keep the values from the other tabs, and those tabs show a note while a preset is
overriding something on them.

Conditions (every ticked one must hold):

| Condition | Source | Notes |
| --- | --- | --- |
| Frame rate | Framework update delta, smoothed over ~1.5 s | "at least / at most"; 0 upper bound = no limit. |
| Players nearby | Player characters in the object table | Counted independently of the bubble, so a preset that turns the bubble off still sees the crowd. |
| Synced players nearby | Pairs reported by the sync clients' `GetHandledAddresses` IPC | The only way to know who is modded. Needs a sync client; otherwise always 0. Polled once a second while any preset uses it. |
| Kind of place | TerritoryIntendedUse plus the game's housing manager and the BoundByDuty condition | City, Overworld, Housing district, Inside a house, Inn room, Duty, Island Sanctuary, Other. Tick several for "any of". |
| Zones | TerritoryType row ids | Exact zones. Instanced copies count as the same zone. "Add current zone" or search. |
| Worlds | World row ids | The world you're currently on, so it follows world travel. |
| Housing | HousingManager ward and plot | Any ward/plot, or a specific one. Combine with the district as a zone to pin one house. Apartments report plot 0. |

Settings a preset can override: bubble on/off, radius, preview ring, opacity, fully hide, hide names, drop shadows,
hide mounts/minions, hide beyond, translucent cap, Sync Shield, and the friends/party/target exemptions.

Switching is debounced under *Timing*: a preset must match for *Switch after* seconds (default 3) before it takes
over, and the active one must stop matching for *Return after* seconds (default 8) before base settings return. An
active preset's frame-rate limits are also relaxed by the *Frame-rate margin* (default 10 fps), because a preset that
hides the crowd raises the frame rate and would otherwise switch itself off as soon as it worked. *Force* applies one
preset regardless of its conditions; `/pspace preset <name>` does the same from chat and `/pspace preset auto` undoes it.
*Add examples* adds three disabled starting points to copy from.

Known limits: the place kinds for City, Overworld, Inn and Island Sanctuary come from TerritoryIntendedUse values
0, 1, 2 and 49; housing is read live from the housing manager so it is exact; *Duty* is anything bound by duty that
isn't one of the above; everything else is *Other*. The *Right now* box on the Presets tab shows what was detected,
so a misclassified zone is easy to spot and can always be matched exactly by zone instead.
