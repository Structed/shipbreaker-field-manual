# Field Manual for Hardspace: Shipbreaker

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for *Hardspace: Shipbreaker*. Press **F1** to open a help panel that tells you about the item or structure you are aiming at:

- what it is
- what you can do with it
- where it goes
- how to take it apart without blowing yourself up

```
| Class II Reactor
| Reactor  ·  Barge Objects
| BARGE
| [REACTOR]
| A Class II reactor. Hugely valuable, but it starts melting
| down as soon as it is disconnected.
|
| WHAT YOU CAN DO
| •  Watch its stability on the HUD once the meltdown starts
| •  Use the Scanner's Systems Mode to see what it is connected to
|
| DISMANTLE SAFELY
| 1. Clear a wide path from the reactor to the Barge before anything else.
| 2. Release the reactor's connection to the thrusters.
| 3. Remove the coolant at the ship's ECU. Doing both ...
```

## Features

- **Live target tracking.** The panel follows whatever the game's own object scanner, your cutter, or your crosshair is pointing at.
- **Hazard badges and warnings from live game state.** The mod reads the game's data directly:
  - reactor meltdown in progress
  - countdown to an explosion
  - fire
  - electrified parts
  - leaks
  - frozen or irradiated parts
  - panels that seal a pressurized room
  - cut grades above your cutter's
- **Power generator help.** For a generator, the panel shows how many of the ship's fuses are still live. For a fuse, it tells you whether its lights are on (wait) or off (pull the lever now).
- **"What you can do".** Lists the interactions available on the object, which cutting tools work on it, how to break its joint, whether it needs Tethers, and where it should be salvaged.
- **Step-by-step "Dismantle safely" guides.** Written for reactors, generators and fuses, fuel and coolant tanks, the ECU and its modules, thrusters, doors, glass, panels, cut points and more. Any other object gets general advice built from its detected hazards.
- **Moddable guide.** Add or override entries in your own `guide.json` and reload them in game with **Ctrl+F1**. You don't need to restart.
- Semi-transparent, compact, and on the right edge of the screen. It hides automatically in menus, the hab and the pause screen.

## Requirements

- Hardspace: Shipbreaker for Windows. Tested on the Steam release, which is a Mono/Unity 2020.3 build.
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (x64). The install script installs BepInEx 5.4.23.5 for you.
- Works alongside [Flat Hab](https://github.com/Structed/shipbreaker-flat-hab).

## Installation

Download `FieldManual-<version>.zip` from the [latest release](https://github.com/Structed/shipbreaker-field-manual/releases/latest) and extract it.

### With the script

Close the game, open PowerShell in the extracted folder and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-FieldManual.ps1
```

The script:

1. Finds the game in your Steam libraries.
2. If BepInEx isn't installed yet, downloads BepInEx 5.4.23.5 and checks it against a pinned SHA-256 hash.
3. Copies the mod to `BepInEx\plugins\FieldManual\`.

If your game is installed somewhere else, add `-GameDir "<game folder>"`.

### Manually

1. Extract [BepInEx_win_x64_5.4.23.5.zip](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) into the game folder, next to `Shipbreaker.exe`.
2. Copy `FieldManual.dll` to `<game folder>\BepInEx\plugins\FieldManual\`.
3. Start the game. `BepInEx\LogOutput.log` should contain `Field Manual 0.1.0 loaded`.

## Keys

| Key | Action |
| --- | --- |
| **F1** | Show or hide the field manual |
| **Ctrl+F1** | Reload `guide.json` from disk |
| **Shift+F1** | Write everything known about the current target to `BepInEx\LogOutput.log` (asset names, hazards, guide matches) |

## Configuration

After the first launch, the settings are in `BepInEx\config\structed.shipbreaker.fieldmanual.cfg`:

| Setting | Default | Description |
| --- | --- | --- |
| `General.ToggleKey` | `F1` | Show or hide the panel. |
| `General.VisibleOnStart` | `false` | Open the panel automatically. |
| `General.ShowKeyHint` | `true` | Show a short "F1: field manual" reminder at the start of each shift. |
| `Appearance.Opacity` | `0.82` | Panel background opacity. |
| `Appearance.Scale` | `1` | Size multiplier (0.5 to 2.5). |
| `Appearance.PanelWidth` | `430` | Panel width at 1080p. |
| `Appearance.TopOffset` | `0.16` | Distance from the top of the screen, as a fraction of its height. |
| `Content.ShowDescription` | `true` | Show the game's own object description. |
| `Content.ShowStats` | `true` | Show value, mass and joint strength. |
| `Content.ShowSystems` | `true` | Show the ship systems the object belongs to. |
| `Debug.ReloadGuideKey` | `Ctrl+F1` | Reload guide files. |
| `Debug.InspectKey` | `Shift+F1` | Log details about the target. |
| `Debug.LogUnknownObjects` | `false` | Log every object that has no curated guide entry. Useful when writing your own. |
| `Debug.VerboseLogging` | `false` | Log target changes. |

## Writing your own guide entries

The built-in guide is embedded in the DLL. On every launch, a copy is written to `BepInEx\config\FieldManual\guide.default.json` for reference. To add or change entries, create `BepInEx\config\FieldManual\guide.json`:

```json
{
  "version": 1,
  "entries": [
    {
      "id": "fuel-container",
      "match": { "objects": [ "INFO_FuelTank", "INFO_FuelCanister" ] },
      "summary": "My own text for fuel tanks.",
      "dismantle": [
        "Step one.",
        "Send it to the {destination}."
      ]
    }
  ]
}
```

- An entry with the same `id` as a built-in entry replaces it. New ids are added.
- Press **Ctrl+F1** in game to reload.
- **Shift+F1** logs the asset names of whatever you are aiming at. Turn on `Debug.LogUnknownObjects` to collect everything that has no entry yet.

### Matching

Every matching entry contributes, and the most specific match wins for the summary and the dismantle steps. In order of specificity:

| Field | Matches |
| --- | --- |
| `objects` | Object asset names (e.g. `INFO_Reactor_Class_II`) or asset GUIDs |
| `parts` | Structure part asset names |
| `nameContains` | Text in the displayed or asset name (`nameExcludes` vetoes) |
| `hazards` | Detected hazards: `Explosive`, `Flammable`, `IgnitesFromElectricity`, `Electrifiable`, `ElectricitySource`, `Leakable`, `Radioactive`, `Reactor`, `PowerGenerator`, `SealsPressurizedRoom`, and the live states `Burning`, `Electrified`, `Leaking`, `Frozen`, `Irradiated`, `Exploding`, `ActiveReactor` |
| `categories` | Category asset names (e.g. `Furniture_CategoryAsset`) or displayed category names |
| `destinations` | `Barge`, `Processor`, `Furnace`, `Pickup`, `Destroy` |

`objects`, `parts` and `nameContains` are alternatives: any one of them can identify the object. `hazards`, `categories` and `destinations` are filters: every one you set must match too. Within a list, any item can match. `priority` adds to the match score to break ties.

### Content fields

| Field | Description |
| --- | --- |
| `summary` | One-line description |
| `actions` | "What you can do" lines |
| `dismantle` | Numbered steps |
| `warnings` | "Watch out" lines |
| `tips` | Extra tips |
| `dontCut` | `true` if the object's body must never be cut. The panel then won't suggest cutting it. Explosive parts, parts that leak when cut, reactors, live generators and fuses get this automatically. |

Warnings and tips from all matching entries are merged. Unless an entry sets `"replaceAutomatic": true`, the panel adds a final "send it to the ..." step when your steps don't mention the destination.

Text can use these placeholders:

| Placeholder | Replaced with |
| --- | --- |
| `{name}` | The object's name |
| `{destination}` | Its salvage destination |
| `{grade}` | Its cut grade |
| `{cutter}` | Your cutter's grade |
| `{tools}` | The tools that can cut it |

### Object catalog tool

`tools\ObjectCatalogDump` is an offline tool that reads the game's asset bundles read-only. It lists every object, its asset name, category, salvage destination, cut grade and hazard components, which helps when writing entries. It needs the .NET 10 SDK:

```powershell
dotnet run --project tools\ObjectCatalogDump -c Release -- "D:\SteamLibrary\steamapps\common\Hardspace Shipbreaker" "$env:TEMP\shipbreaker-catalog"
```

The output contains the game's own text, so keep it out of this repository.

## Uninstalling

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-FieldManual.ps1 -Uninstall                 # removes the mod only, keeps your guide.json
powershell -ExecutionPolicy Bypass -File .\Install-FieldManual.ps1 -Uninstall -RemoveBepInEx  # also removes BepInEx (and any other BepInEx mods)
```

To uninstall by hand, delete `BepInEx\plugins\FieldManual\`.

## Building

You need the [.NET SDK](https://dotnet.microsoft.com/download) (6 or newer) and a copy of the game with BepInEx installed. The project compiles against the game's own assemblies, so this repository contains no game files.

```powershell
dotnet build src\FieldManual -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Hardspace Shipbreaker"
```

- `GameDir` defaults to `C:\Program Files (x86)\Steam\steamapps\common\Hardspace Shipbreaker`.
- Add `-p:DeployToGame=true` to copy the built DLL straight into `BepInEx\plugins\FieldManual\`.
- Running `.\Install-FieldManual.ps1` from the repository root installs `src\FieldManual\bin\Release\FieldManual.dll`.

## How it works

- **Finding the target.** The mod asks the game what the cutter is locked onto, then what the vanilla object-info HUD is showing, and finally falls back to a raycast from the camera.
- **Reading the object.** For the targeted part, it reads:
  - the object's info asset: name, categories and salvage destination
  - its cutting data: cut grade and which tools can cut it
  - its breakable joint
  - its ECS components: explosive, flammable, leakable, electrical, radioactive, reactor, generator and pressurized-room data
  - available interactions
  - fuse/breaker state
- **Building the advice.** This live data drives the badges and the "Right now" section. Curated guide entries are matched on top. Objects without an entry get general advice from their hazards.
- **Rendering.** The panel uses Unity IMGUI and is rebuilt only when something changes. If it doesn't fit on screen, the description, tips and systems are hidden first, and warnings move up.

## Known limitations

- English only. The advice text is the mod's own; the object names and descriptions come from the game's localization.
- Only tested with the Steam version.
- The curated advice covers the common hazardous objects. Some rarer objects only get general advice. Contributions to `guide.json` are welcome.

## Disclaimer

This is an unofficial fan mod. It is not affiliated with or endorsed by Blackbird Interactive or Focus Entertainment. *Hardspace: Shipbreaker* is a trademark of its respective owners. This repository contains no game code, assets or text.

## License

[MIT](LICENSE)
