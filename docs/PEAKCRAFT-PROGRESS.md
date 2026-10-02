# PeakCraft progress

Tracks execution of `docs/2026-10-02-001-feat-peakcraft-plan.md`. The plan file itself is not edited.
All evidence below was observed on this PC on 2026-10-02 with PEAK 2.5.a and the Gradle dev client of Minecraft 26.3.

## Environment decisions

- The plan lives in `docs/`, not `docs/plans/`.
- The repo is a direct clone of `chasmlol/SkyCraft` (origin = upstream), not a GitHub fork. Nothing was committed or pushed.
- .NET SDK 10 and JDK 25 were missing; installed project-local in `.tools/dotnet` and `.tools/jdk-25` (gitignored). User approved.
- BepInEx lives in Thunderstore Mod Manager. A clean `PeakCraft` profile is used; the user's `Default` profile is untouched. User approved.
- No Minecraft 26.3 launcher instance exists; the Gradle dev client (`gradlew runClient`) is the Minecraft instance. User approved.
- Doorstop's `winhttp.dll` was copied into the PEAK install folder with a `doorstop_config.ini` that has `enabled = false`, so a plain Steam launch stays vanilla; `tools/launch_peak.bat` starts PEAK against the PeakCraft profile.

## Units

| Unit | Status | Evidence |
|---|---|---|
| U1 Fork, scaffold, decompile notes | done | `Loading [PeakCraft 0.1.0]` in `BepInEx/LogOutput.log`; `docs/PEAK-NOTES.md` names a patch point for every concern and the measured runtime facts |
| U2 Link and protocol mirror | done | `protocol layout self-check: PASS` (45 size and offset checks), `ring self-test: PASS` (entry and byte ring wrap-around), `link up` / `link down` against `tools/fake_minecraft.py` with the scripted position read back; forced `CreateFileMapping failed (Windows error 5)` disables the plugin with no further log lines |
| U3 Fabric switches and baseline | done | unmodified mod built and walked `tools/fake_skyrim.py`'s floor, overlay PNG saved; after the switches a fresh world flies on double-jump (Creative) and 4 s of punching diggable triangles reported 0 dug cells; `gradlew build` passes with all 21 tests; both logs show the other side's heartbeat |
| U4 Coordinates and collision export | done | Minecraft feet y -511.445 equals PEAK ground y under the feet (mc -511.445); W at yaw 180 moved Unity +Z, yaw -90 moved +X; `click 3 -> BLOCK (…, -511.0, …)` on PEAK's floor; `collision cleared (epoch N)` on every scene and segment change. The open-air teleport edge was not isolated (someone was playing in the window during the test); teleports were acknowledged in every handoff seen |
| U5 Player follower and camera | done | `follower: took PEAK's character (321 renderers hidden, ragdoll kinematic)`; `camera: … apart 0.0 cm` standing, 14 cm while walking (one Minecraft frame of latency, the plugin interpolates ahead); stamina stays 1.00; Handoff holds until `teleportAck` matches |
| U6 Input bridge and menu state | done | real key presses through Windows SendInput: E opens the inventory (flags 0x7), chat line `<Dovahkiin> hello 123` arrived intact, F5 cycles camera mode 1/2/0, Ctrl+W sprint (0x15), Shift sneak (0xD); AE2: W held, Esc opens PEAK's menu, Minecraft's position stays fixed until the menu closes; focus loss logs `input: released (release-all sent)` |
| U7 Safe state on link loss | done | AE4: Minecraft killed 04:25:05, `link down … PEAK has the player` and `PeakOwns` within 9 s, PEAK HUD and character back; restarted Minecraft re-linked without restarting PEAK (`link up`, `Handoff`, `MinecraftOwns`); no `Skyrim link down` in Minecraft's log across PEAK's loading screens |
| U8 HUD overlay | done | screenshots show hotbar, hearts, crosshair and hand upright at scale, creative inventory readable with cursor tooltips, no PEAK stamina bar; `overlay: 1920x1080` then `3440x1440` after resolution changes |
| U9 Interact key and cutscene handoff | done | `interact: PEAK target 'AirportGateKiosk' (board flight)` opens the boarding pass; log order `PeakMenu -> Cutscene (loading) -> Handoff -> MinecraftOwns` ends on the island with `teleported to 15.7 -510.7 372.6`; luggage opened and the first checkpoint campfire lit with G; `interact: nothing targeted` is the whole reaction to an empty press |
| U10 Blocks and avatar in PEAK | done | screenshots: plank tower on island terrain with the player on top, glass, see-through leaves, tinted grass, torches, selection outline, a cobblestone block partly hidden behind rock; leaves decaying and broken blocks disappear; third-person skin with armour and shield |
| U11 Hazards and death | done | AE1: in PEAK's fog, Creative `health 20.0 -> 20.0 (blocked/immune)`, Survival `19.26 -> 19.15 …`, back to Creative immune again; fog kills in 54 s at the default scale; AE5: fall death shows PEAK's "Your body was never found" report; Minecraft respawns by itself and is teleported to the scout in the airport |
| U12 Package and README | done | `peak/artifacts/thunderstore/release/aeironnarmiento-PeakCraft-0.1.0.zip` holds `manifest.json`, `README.md`, `CHANGELOG.md`, `LICENSE`, 256x256 `icon.png`, `plugins/aeironnarmiento.PeakCraft.dll`, `plugins/skycraft-0.1.2.jar`; dependency `BepInEx-BepInExPack_PEAK-5.4.75301`; unzipped into a clean profile it logged load, self-check PASS and `link up`. Nothing uploaded |

Flows F1 to F5 ran in one PEAK session (04:19 to 04:26): start, kiosk to island, block placed, Survival toggle in the fog, Minecraft killed and restarted.

## Differences from the plan

- **Fabric switch touches three files, not two.** `DestructionToggle.load` defaulted a missing `destruction=` key to on, which would have overridden the new default; it now defaults to `SkyDig.destruction`.
- **Template layout:** `PeakCraft.slnx`, `Directory.Build.targets` and `global.json` instead of `PeakCraft.sln`.
- **Overlay is hidden under PEAK's own windows** (pause menu, boarding pass, end screen), where the plan's table says "on". Minecraft's hand and hotbar covered the end screen's buttons otherwise.
- **Steep-slope walls are not raised by the exporter.** Minecraft's local player collides with triangles, and its triangle collider already refuses anything steeper than about 45 degrees.
- **Hazard scale is 3, not 1.** At 1 the fog's damage (0.21 health a second) is slower than Minecraft's natural healing.
- **Minecraft is respawned by the plugin** after a death (a click on the death screen's Respawn button), which answers the plan's open question about the respawned player.
- **Added on request during the session:** the scene message (other entities, particles) and the world-entities table (arrows, items) are drawn, and PEAK's mirrors show the Minecraft body in first person. The plan's U10 said to ignore those.
- **Two more Fabric changes, asked for after the first hand-over:** `PlayerEdgeMixin` no longer switches Minecraft's sneak edge guard off; it makes `Player.canFallAtLeast` count the host's triangles as ground (verified: sneak-walking on a raised airport platform stopped at its edge with Y unchanged, walking without sneak fell off). `AvatarExporter` sends the player model in first person too (`-Dskycraft.firstPersonAvatar=false` restores SkyCraft's behaviour); the plugin draws it only for PEAK's mirror cameras while `cameraMode` is 0 (verified: the mirror shows the crouch and the raised shield in first person).
- **Thunderstore team and plugin GUID prefix** are `aeironnarmiento` (the git user name); change `peak/Directory.Build.props` and the csproj before any upload.

## Known gaps against the plan

Found when re-reading every unit after the first hand-over:

- **R22/R23, summit ending:** not reachable. PEAK's ending needs a flare lit at the peak, and PEAK items are a Scope Boundary ("PEAK items ... crossing into Minecraft"). The ownership code handles the ending state (`stats.won`, end screen) but it was only exercised through the death end screen. Listed in `peak/README.md` under known limitations.
- **U4, open-air teleport edge:** not isolated (see the table).
- **U5 verification:** the camera agrees to 0.0 cm standing and 14 cm while walking, which is more than "a few centimetres".
- **U6, mouse buttons:** left and right were exercised (break, place, shield); the middle button was not checked on its own.
- **U8, "no new frame keeps the last one":** true by construction (`Overlay.Update` returns without touching the texture); not checked with a stalled Minecraft.
- **U10 file name:** the avatar is drawn by `Render/CapturedModel.cs`, not `Render/Avatar.cs`, because the same class draws the scene and the mirror body.

## Dev notes

- `. .tools/env.sh` puts the local dotnet and JDK on PATH; `. .tools/dev.sh` adds helpers that drive the plugin's dev harness (`[Dev] Harness = true` in the plugin config; off again now).
- The harness auto-starts an offline airport session and runs commands from `BepInEx/peakcraft-dev.txt` (screenshots, key and mouse events, teleport, fog, probe).
