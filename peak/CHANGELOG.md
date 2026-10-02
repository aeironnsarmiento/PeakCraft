# Changelog

## 0.1.0

First version: SkyCraft's Minecraft mod and link protocol, with a PEAK host plugin in place of the
Skyrim one.

- Shared-memory link with heartbeats; PEAK takes the player back when Minecraft goes away.
- Minecraft movement on PEAK's collision (ray-cast grid), camera and input bridged.
- Minecraft's HUD and screens over PEAK; blocks, skin, arrows, items and particles drawn in PEAK.
- PEAK interact key (G), cutscene handoff for the flight, crash, warps, death and ending.
- PEAK's hazards as Minecraft damage in Survival; a Minecraft death is the scout's death.
- Minecraft mod: the mirror world starts in Creative and digging is off by default; sneaking stops at
  the edges of PEAK's ground; the player's model is sent in first person too, for PEAK's mirrors.
