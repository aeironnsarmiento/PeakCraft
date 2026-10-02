---
title: Porting a two-game shared-memory link to a new host game
date: 2026-10-02
category: architecture-patterns
module: host plugin
problem_type: architecture_pattern
component: tooling
severity: medium
applies_when:
  - Adding a new host game to the SkyCraft guest and protocol
  - Writing a mod where a second, hidden game owns the player inside a visible one
  - Debugging link flapping, a body fought over by two games, or guest geometry drawn wrong
tags: [shared-memory, host-plugin, ownership-state-machine, collision-export, bepinex, harmony, unity, porting]
---

# Porting a two-game shared-memory link to a new host game

## Context

SkyCraft links Minecraft to Skyrim through one shared-memory mapping. PeakCraft replaced the Skyrim
half with a BepInEx plugin for PEAK (`peak/`), reusing the protocol
(`protocol/skycraft_protocol.h`) unchanged and the Minecraft mod (`fabric/`) with four small
optional changes. The port showed which parts are the same for
any host and which mistakes are easy to repeat. The full, game-agnostic write-up is
[docs/PORTING-GUIDE.md](../../PORTING-GUIDE.md); this entry is the short form.

## Guidance

**The host plugin has nine jobs, and none of them needs a guest or protocol change:** link,
coordinates, collision export, ownership and follower, camera, input, overlay, world drawing,
damage and death. Build them in that order; each is checkable alone.

**One value decides who owns the player.** Five states (`peak/src/PeakCraft/Player/Ownership.cs`):
host owns, handoff, guest owns, host menu, cutscene. It is decided once per frame in one function,
and every patch and module reads it. Link loss is simply "host owns", which restores movement,
camera, HUD and the character in one transition.

**Taking the body is a handshake.** The host publishes its character's position and raises
`teleportSeq`; it stays in handoff until the guest's `teleportAck` matches. The starting sequence
number is derived from the clock so an old guest's ack can never match.

**Patch the funnel, not the consumers.** Five patch classes on six methods were enough: the input
sampler, the movement step and the look, the camera update, the hazard function and the
instant-kill function (`peak/src/PeakCraft/Patches/`). Patches are applied one by one; a failure unpatches everything
and disables the plugin (`Plugin.ApplyPatches`).

**Sample collision when shapes cannot be read.** Ray-cast a half-block grid per 8-block region
column, build solid spans by testing each gap for front face versus back face, join corners into
floor and ceiling triangles, and anchor walls where a floor ends against something solid
(`peak/src/PeakCraft/World/CollisionExporter.cs`). Budget it by time (2.5 ms a frame), refresh on a
timer, evict by sending a region empty, and bump an epoch when the world changes.

**Verify at startup.** A layout self-check and a ring wrap-around self-test run before the mapping
is created (`Proto.SelfCheck`, `Rings.SelfTest`).

## Why This Matters

Each of these was a real failure during the port:

| Symptom | Cause | Fix |
|---|---|---|
| Link went up and down every few seconds | The render ring was not drained yet; the guest waits about a second per message when it is full, on the thread that stamps its heartbeat | Drain every guest-written ring every frame from the first frame (`RenderRing.Update`) |
| Character flew apart, world went dark, positions NaN | Every ragdoll body was moved by the same delta, but they are a bone hierarchy, so children moved once per ancestor | Move only the topmost bodies (`Follower.Take`) |
| Host crashed with a graphics out-of-memory error | The whole atlas was re-uploaded for every animated sprite, every frame | Upload the small region to a scratch texture and copy it on the GPU (`Atlas.SetRegion`) |
| Black boxes on skin, leaves and particles | The host's build stripped the shader's alpha-clip variant | Classify each triangle's texels on the CPU; draw mixed ones blended with depth write (`Atlas.Classify`) |
| Hazards never killed in Survival | At scale 1 the damage was slower than Minecraft's natural healing | Measure time-to-kill; default scale 3 (`AfflictionBridge`) |
| Guest HUD covered the host's end-screen buttons | The overlay was drawn in every state | Hide it while a host window is open (`Plugin.OnGUI`) |
| Sneaking walked off ledges | Minecraft's edge check looks for blocks; host ground is triangles | Count the host's triangles as ground in the guest's edge check (`fabric/src/main/java/dev/skycraft/mixin/PlayerEdgeMixin.java`) |
| Timers froze in the pause menu | The host sets time scale to zero when paused offline | Use unscaled time for every timer |

## When to Apply

- Starting a port to another host: read the guide's checklist first, then this table.
- Any symptom in the table above on a new host: the cause is very likely the same.
- Reviewing a host plugin: check that ownership is one value, that rings are drained
  unconditionally, and that a failed patch or mapping leaves the host unmodified.

## Examples

The ownership table, as the plugin implements it:

| State | Host movement | Host input | Host camera | Host HUD | Character shown | Overlay and blocks |
|---|---|---|---|---|---|---|
| PeakOwns | on | on | on | on | yes | off |
| Handoff | frozen | off | on | on | yes | off |
| MinecraftOwns | off | off | off | off | no | on |
| PeakMenu | off | on | off | on | no | on |
| Cutscene | on | off | on | on | yes | on |

Testing without a second game: `tools/fake_minecraft.py` opens the mapping, stamps a heartbeat and
writes a scripted player state, which is enough to verify link up, link down and the layout. In the
real game, a dev harness behind a config switch (`peak/src/PeakCraft/Dev/DevHarness.cs`) reads
commands from a file, so screenshots, key presses and teleports can be scripted.

## Related

- [docs/PORTING-GUIDE.md](../../PORTING-GUIDE.md): the full guide
- [docs/DESIGN.md](../../DESIGN.md): SkyCraft's original design
- [docs/PEAK-NOTES.md](../../PEAK-NOTES.md): PEAK's patch points and measured facts
- [docs/PEAKCRAFT-PROGRESS.md](../../PEAKCRAFT-PROGRESS.md): evidence per unit of the port
