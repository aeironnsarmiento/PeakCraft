# PeakCraft

Play PEAK as a Minecraft player. You move with Minecraft's physics on PEAK's mountain, carry
Minecraft's inventory and HUD, and place and break blocks that PEAK draws in its own world.

PeakCraft is a port of [SkyCraft](https://github.com/chasmlol/SkyCraft) (Minecraft inside Skyrim)
to PEAK. Neither game is rewritten: Minecraft runs its own game logic in the background, PEAK runs
its airport, island, fog and campfires, and two small mods talk through shared memory. This
package is the PEAK half (a BepInEx plugin) and carries the Minecraft half (a Fabric mod jar) with it.

> **Experimental, solo only.** A fan project, not affiliated with Mojang, Microsoft, Landfall or
> Aggro Crab. You need to own both games.

## What works

- **Movement:** walk, sprint, jump, sneak, fall and fly (Creative) with real Minecraft physics on
  PEAK's terrain and buildings. Sneaking stops you at the edge of PEAK's ledges as it does on blocks. PEAK's own movement, climbing and stamina are off.
- **Camera:** first person and both F5 third-person views. Your Minecraft skin, armour and held
  items are drawn in PEAK; PEAK's scout is hidden. PEAK's mirrors show your Minecraft body, animated,
  in first person too.
- **HUD:** Minecraft's hotbar, hearts, chat, inventory and every other Minecraft screen, over
  PEAK's picture.
- **Blocks:** place and break blocks anywhere. They sit in PEAK's world, hidden by PEAK's rocks in
  front of them and lit by PEAK's sun. Arrows, dropped items, torches, chests and particles show too.
- **PEAK's run:** the kiosk, the flight, the crash on the beach, luggage, campfires, and the rising
  fog work as usual, through one interact key. The run's end screen after a death works; the
  summit ending does not yet (see Known limitations).
- **Survival:** the mirror world starts in Creative, where nothing on the mountain hurts you.
  `/gamemode survival` makes PEAK's hazards (fog cold, heat, poison, thorns, lava) cost Minecraft
  hearts, and a Minecraft death kills your scout.
- **Safe fallback:** if Minecraft closes or hangs, PEAK gives you its own controls, camera and HUD
  back within a few seconds, and links again when Minecraft returns.

## Requirements

- PEAK with [BepInExPack_PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/) (a mod
  manager installs it for you).
- A Minecraft: Java Edition account and your own **Minecraft 26.3** instance with:
  - [Fabric Loader](https://fabricmc.net/) 0.19.5 or newer and [Fabric API](https://modrinth.com/mod/fabric-api) 0.161.0+26.3
  - Java 25
  - this JVM argument: `--enable-native-access=ALL-UNNAMED`
- Windows. About 3 GB of extra RAM for the hidden Minecraft.

## Installing

1. Install this package with your mod manager (or copy `plugins/` into `BepInEx/plugins/`).
2. Copy `skycraft-<version>.jar` from this package's `plugins` folder into your Minecraft
   instance's `mods` folder, next to Fabric API. In Thunderstore Mod Manager or r2modman:
   *Settings → Browse profile folder → BepInEx/plugins/<team>-PeakCraft*.
3. Add `--enable-native-access=ALL-UNNAMED` to the instance's JVM arguments.

## Playing

1. **Start the Minecraft instance first** and leave it on its title screen. It waits there.
2. **Start PEAK** (modded) and begin a **solo/offline** game.
3. When the airport loads, Minecraft opens its own void "SkyCraft" world, hides its window, and
   takes over your body. You are standing in PEAK's airport with a Minecraft hotbar.

Minecraft closes itself a few seconds after PEAK does. Add `-Dskycraft.quitWithSkyrim=false` to its
JVM arguments to keep it running between PEAK sessions, and `-Dskycraft.showWindow=true` to keep
its window visible.

## Controls

Minecraft has priority. These keys still go to PEAK:

| Key | Does |
|---|---|
| **G** | PEAK interact, on whatever PEAK targets: kiosk, luggage, campfire, items. Hold it where PEAK wants a hold. A prompt under the crosshair says what it will do. |
| **Esc** | PEAK's pause menu (or closes an open Minecraft screen first) |
| **O** | Minecraft's pause / options menu |

Every other key and the mouse are Minecraft's: **E** inventory, **F5** camera, **T** chat, **/**
commands, **Shift** sneak, **Ctrl** sprint, and so on. While a PEAK window is open (pause menu,
boarding pass, end screen) the mouse and keyboard are PEAK's and Minecraft stands still.

Both keys can be changed in `BepInEx/config/aeironnarmiento.PeakCraft.cfg`, which also has the
hazard damage scale, the brightness of Minecraft's blocks and the height offset between the two worlds.

## Known limitations

- **Solo only.** Other players would see neither you nor your blocks; multiplayer is not attempted.
- **Collision is sampled, not exact.** PEAK's collision meshes cannot be read at runtime, so the
  plugin ray-casts PEAK's physics on a half-block grid around you and rebuilds surfaces from the
  hits. Anything thinner than half a block (railings, poles, ropes) can be walked through, sharp
  edges are rounded by up to half a block, and very steep walls made of many small ledges can be
  climbed where PEAK would not let you. Only about 28 blocks around you exist for Minecraft; fast
  Creative flight can outrun it for a moment.
- **PEAK's lighting on Minecraft things is approximate.** PEAK ships without a shader that can
  alpha-clip, so faces with holes (leaves, plants, torches, item sprites) are drawn blended. They
  cast no shadow and can sort oddly against other see-through things. Minecraft's own light
  sources (torches, lava) do not light PEAK.
- **The summit ending cannot be reached yet.** PEAK ends a run when a flare is lit at the peak,
  and PEAK's items cannot be held or used while Minecraft has the body. You can climb to the top,
  but the run only ends through a death or PEAK's pause menu.
- **PEAK's items do not cross over.** Picking one up with G puts it in PEAK's inventory, which is
  hidden and unused while Minecraft has the body. Stamina, hunger and weight are ignored.
- **No digging into the mountain**, no water or lava flowing over PEAK's terrain, and explosions
  leave PEAK's world alone. SkyCraft's digging is switched off.
- **PEAK's HUD is hidden** while Minecraft has the body, including PEAK's own interact prompt
  (replaced by the small `[G]` prompt) and the fog/"hero" banners.
- **Minecraft's HUD covers PEAK's cutscenes** (the death view before the end screen), but is
  hidden under PEAK's own windows so their buttons stay clickable.
- **After a Survival death** PEAK's solo run ends with its scouting report. Minecraft's player is
  respawned automatically and waits; it is placed at your scout again once you are back in the airport.
- **Minecraft's inverted crosshair** is drawn plain white.
- Windows only; tested with PEAK 2.5.a and Minecraft 26.3.

## Uninstalling

Remove the package in your mod manager and delete `skycraft-<version>.jar` from the Minecraft
instance's `mods` folder. The Minecraft world it made is `saves/SkyCraft` in that instance.

## Building from source

The repository is a fork of SkyCraft: `fabric/` (the Minecraft mod), `protocol/` (the shared-memory
layout, unchanged) and `peak/` (this plugin).

```sh
cd fabric && gradlew build            # JDK 25; writes fabric/build/libs/skycraft-<version>.jar
dotnet build peak -c Release          # .NET SDK 10; writes peak/artifacts/thunderstore/release/*.zip
```

Copy `peak/Config.Build.user.props.template` to `Config.Build.user.props` if PEAK or your BepInEx
profile are not in the default places. `docs/PEAK-NOTES.md` lists every PEAK class the plugin
touches; `tools/fake_minecraft.py` stands in for Minecraft when testing the link.

## Credits and licence

MIT, like SkyCraft. The Minecraft mod, the protocol and the design are
[chasmlol's SkyCraft](https://github.com/chasmlol/SkyCraft); PeakCraft adds the PEAK host plugin
and four small changes in the Minecraft mod: a Creative mirror world, digging off by default,
sneaking that stops at the edges of the host's ground, and the player's model sent in first person
too so mirrors can show it (`-Dskycraft.firstPersonAvatar=false` turns that off).
