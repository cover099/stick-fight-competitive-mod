# Stick Fight Competitive Mod

A [BepInEx 5](https://github.com/BepInEx/BepInEx) plugin for **Stick Fight: The Game** aimed at competitive 1v1s. It fixes gameplay that depends on your FPS, fixes online desyncs, and can lock the game to a fixed competitive ruleset. Works on Windows and Linux (Proton).

## Turning it on

The mod starts **switched off**. Turn it on with any of these:

- the **COMP MOD** lever on the first screen ("PRESS EVERY BUTTON TO JOIN"): knock it to the ON side.
- the chat command `/comp` (toggle), `/comp on` or `/comp off`.
- `Enabled = true` under `[General]` in `BepInEx/config/competitivemod.stickfight.cfg`.

The setting is saved. Online, the **host's** setting applies to everyone in the lobby and can only be changed in the lobby, not mid-match.

**Both players need the mod.** Many fixes change how the two games talk to each other, so a player without it will see things go wrong.

## Fixes

Each fix can be turned off separately in the config file (they only apply while the mod is on).

| Setting | Default | What it does |
|---|---|---|
| `NetworkSendRate` | on | Send your position updates at the intended 50/s regardless of FPS (vanilla: 30/s at 60 FPS, 48/s at 144-240 FPS). |
| `FullAutoFireRate` | on | Full-auto weapons fire at their real cooldown instead of being rounded up to the next frame (vanilla: slower at lower FPS). |
| `ThrowAim` | on | Throw weapons in the direction you are aiming this frame (vanilla can use last frame's aim; worse at lower FPS). |
| `ThrowWhiff` | on | Stop thrown weapons from counting as landed before they've launched (vanilla: above 60 FPS throws often lose their damage and get blocked by your own arm, dropping in front of you). |
| `StunCarryOver` | on | Clear the stun from punching into a block when the next map starts (vanilla: it carries over and you can't attack or block at the start of the round). |
| `PoseCarryOver` | on | Start every map standing upright and at rest (vanilla: you keep your pose from the end of the last map, e.g. upside down or legs out, plus any leftover spin). |
| `Draws` | on | If the last player alive also dies before the next map, it's a draw: nobody gets the win (vanilla: they get 2 wins and the next map breaks). |
| `RoundStart` | on | Online, give control the moment the countdown ends (vanilla: ~2 frames later, so 60 FPS players start ~30 ms after 400 FPS players). |
| `AllowAllMapsOnline` | on | Let the host pick maps the game only allows offline (Desert 2, 7, 8, Factory 5, Winter 13) when they're on in the map list. |
| `SyncedObjects` | on | For non-hosts, stop host-synced map objects (moving platforms, crates) from flying off and vanishing when two updates arrive close together (vanilla: the Factory 1/3/4 platform often disappears). |
| `FallingBlocks` | on | Online, when a crumbling block (Castle 6/7) falls on anyone's game, make it fall for everyone (vanilla: each game decides on its own, so the maps desync). Every player needs the mod. |
| `ShotSync` | on | Online, send every gun shot reliably so it always shows up on other players' screens, including a gun's last shot (vanilla: grenades and bullets often go missing for the other player but still hit). Both players need it on. |
| `SnakeSync` | on | Online, keep snakes chasing the same player in the same place on every screen (vanilla: a non-host's snakes never chase on their own screen, extra snakes get deleted for others, and snakes drift apart). Host and players need the mod. |
| `Quickdraw` | on | Make quickdraws (shooting the moment you pick up a gun) work the same at any FPS as at 60 FPS (vanilla: the glitch ends almost immediately at high FPS). |
| `ThrowBlock` | on | Online, the target's own game decides whether a thrown weapon hit them, so a throw you stop by shooting it can't kill you on the thrower's screen (vanilla: you 'die to nothing'). Both players need it on. |
| `PlacedGuns` | on | Online, make guns placed on the map (Lava 3/5/6/7, Laser 7/8/9, Western 3/5/6/9/10, Winter 15, Halloween 2/5/7/8/9/10/11, Xmas 12) work for a player whose map finished loading after the host's. |
| `HostSidePickup` | on | Online (host), also give a gun to another player when their body touches the host's copy of it, so non-hosts can pick up the minigun, flamethrower and RPG (vanilla: their copy slides away and they can barely grab them). |
| `NoLobbyWin` | on | Don't count the lobby round (first kill in the menu lobby) as a win (vanilla: the survivor starts the match one win up). |
| `JoinTimeout` | on | When joining a lobby, wait up to 30 s for the host to accept instead of 5 s (vanilla: often fails with the 'check firewall' error unless you pause while joining). |
| `RemoteMovement` | on | Apply other online players' movement/jumps once per physics step instead of once per frame (vanilla: their wall jumps kick harder on your screen the higher your FPS). |
| `FixedStepMovement` | on | Run walking (and the leg animation) on the 60 Hz physics step instead of per frame (vanilla: uneven above 60 FPS, sluggish below it). |
| `FixedStepTurning` | on | Run body turning on the 60 Hz physics step instead of per frame. |
| `FixedStepBalance` | on | Run leg balancing on the 60 Hz physics step instead of per frame. |
| `FixedStepWeaponHold` | on | Run weapon holding and the hand pull while blocking/grabbing on the 60 Hz physics step instead of per frame (vanilla: wobbly at low FPS). |

## Competitive ruleset (locks)

While the mod is on, these lock the in-game settings to the ruleset in `Rules.cs` and show lock icons in the menus. Auto pickup and chat are never locked. Turning a lock off doesn't restore your previous settings.

| Setting | Default | What it does |
|---|---|---|
| `GameSettings` | on | Lock the game settings (HP, regen, weapon spawns, maps option, bots, ping display, win display) to the competitive ruleset. Auto pickup and chat are never locked. |
| `Maps` | on | Lock the map selection to the competitive map list. |
| `Weapons` | on | Lock the weapon selection to the competitive weapon list. |

## Install

### Easy install (Windows)

1. Download `CompetitiveMod-windows-installer.zip` from the [latest release](https://github.com/cover099/stick-fight-competitive-mod/releases/latest) and extract it anywhere.
2. Close the game, then double-click `Install.bat`.

It finds the game through Steam (or asks for the folder), installs BepInEx 5 if it isn't there yet, and puts the mod in place. If Windows shows "Windows protected your PC", click **More info** → **Run anyway**.

### Manual install

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (`BepInEx_win_x64`) into the game folder.
2. Copy `CompetitiveMod.dll` into `BepInEx/plugins`.

On Linux, set the game's Steam launch options to `WINEDLLOVERRIDES="winhttp=n,b" %command%` so BepInEx loads under Proton.

## Build

Requires the .NET SDK and a copy of the game with BepInEx installed. The project compiles against the game's own assemblies (`StickFight_Data/Managed`) and BepInEx's `core` folder; none of them are included here.

```sh
dotnet build -c Release -p:GameDir=/path/to/StickFightTheGame/
```

By default `GameDir` is `../StickFightTheGame/` next to this folder. The built DLL is copied into that install's `BepInEx/plugins` folder.
