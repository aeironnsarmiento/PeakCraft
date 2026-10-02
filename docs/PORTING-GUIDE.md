# Porting guide: putting a Minecraft player inside another game

This is the design behind SkyCraft (Minecraft in Skyrim) and PeakCraft (Minecraft in PEAK), written
so the next host game takes days instead of weeks. It covers the parts that are the same for every
host: the link, the ownership rules, collision, camera, input, drawing, damage, testing and
packaging. It leaves out everything that belongs to one game.

Terms used throughout:

- **Guest**: Minecraft with the SkyCraft Fabric mod (`fabric/`). It runs hidden and owns the
  player's physics, inventory, HUD and blocks. It is the same for every host.
- **Host**: the game the player sees. It owns the world, the picture, the window and the keyboard.
- **Host plugin**: the mod you write for the host. This guide is about writing it.
- **Protocol**: `protocol/skycraft_protocol.h`, the byte layout of the shared memory both sides use.

## 1. The one rule

Neither game is rewritten. Minecraft runs its own movement, combat, inventory and block logic.
The host runs its own world, scripts and saves. The two mods only translate:

- The host tells the guest what the world is shaped like and what the keyboard and mouse did.
- The guest tells the host where the player is, where the camera is, and what to draw.

If you find yourself re-implementing a Minecraft mechanic in the host plugin, or a host mechanic in
Java, stop: the data is flowing the wrong way.

A new host needs **no change to the guest and no change to the protocol**. PeakCraft's plugin was
written against the unchanged header. Every guest change it made was optional: two default
settings, a sneaking fix and sending the player model in first person for mirrors.

## 2. What the host plugin has to do

Nine jobs. Build and verify them in this order; each one is playable on its own.

| # | Job | Done when |
|---|---|---|
| 1 | Link | The plugin creates the mapping, both sides see each other's heartbeat, a scripted fake guest's position is read back |
| 2 | Coordinates | A unit test or in-game probe confirms axes, scale, yaw sign and the feet offset |
| 3 | Collision export | The guest player stands on the host's ground at the same height the host's character does |
| 4 | Ownership and follower | The host's character is hidden and sits where the guest player is; link loss gives it back |
| 5 | Camera | The host camera matches the guest's eye in first person and both third-person modes |
| 6 | Input | Keys, mouse, text and cursor reach the guest; the host's own controls are off |
| 7 | Overlay | The guest's HUD and screens are drawn over the host's picture |
| 8 | World drawing | Blocks, the player model, entities and particles are drawn inside the host's 3D scene |
| 9 | Damage and death | Host hazards hurt the guest player; a guest death runs the host's death |

Then interaction with the host's own objects (one key), cutscene handling, and packaging.

## 3. The link

One named shared-memory mapping (`Local\SkyCraft_v1`, about 191 MB). The host creates it at
startup; the guest polls for it and opens it. There are no sockets, no threads you must start and
no serialisation library: fixed-size little-endian structs at fixed offsets.

### Four shapes cover everything

| Shape | Used for | Rule |
|---|---|---|
| **Seqlock slot** | Latest-value state written every frame: host state, guest state, tables | Writer makes the `seq` field odd, writes, makes it even. Reader copies, then checks `seq` is unchanged and even; on a torn read it keeps last frame's value |
| **Entry ring** | Small fixed-size events: input, guest events | Single producer, single consumer. `head` and `tail` are running 64-bit counts; index is `count & (entries - 1)` |
| **Byte ring** | Variable-size messages: collision, render | Each message is 8-byte aligned with a `{type, payloadBytes}` header. A pad message (type 0) means "skip to the start of the ring". Unknown types are skipped by length |
| **Triple buffer** | The HUD image | Writer owns a back slot, reader a front slot, one atomic word names the middle slot and carries a dirty bit. Neither side ever waits |

### Rules that cost time when broken

- **Mirror the layout and check it at startup.** The plugin's copy of the structs gets a self-check
  that compares the size of each mirrored struct with the header's `static_assert` values, and key
  field and region offsets with the header's layout. It refuses to run on a mismatch. A wrong offset otherwise shows up as a subtly wrong position hours later.
- **Self-test the rings on a private buffer** at startup, including a wrap-around. It is about 80 lines
  and catches every off-by-one before a second process is involved.
- **Use the same clocks as the guest.** Heartbeats are `GetTickCount64`, tick timestamps are
  `QueryPerformanceCounter`. Call the Win32 functions directly; a runtime's own "tick count" is not
  guaranteed to be the same clock.
- **Publish the host state every frame.** The guest paces its frames on the host state's sequence
  number.
- **Drain every ring the guest writes, every frame, from the first frame.** When the render
  ring is full the guest waits up to about a second per message before giving up and retrying
  later. Those waits are on the thread that stamps its heartbeat, so if the plugin does not read
  the ring yet, the guest stalls and the link flaps up and down. This was the first serious bug in PeakCraft.
- **Reset what the host owns when creating the mapping.** If the guest outlives the host, the
  mapping survives with old ring positions in it. Zero the host-owned state, ring heads and the
  overlay control word, write the version and pid, and write the magic number last.
- **Stop exporting when the consumer is behind.** Check the collision ring's backlog and skip the
  frame rather than pile on.
- **A failed mapping disables the plugin completely.** The host must run unmodified in that case.

### Liveness

Each side stamps a heartbeat every frame. The host plugin treats the guest as alive if its stamp
is non-zero and younger than three seconds. The guest allows the host eight seconds, because a
host's loading screens stall its heartbeat. A changed guest pid counts as a new link: everything sent before must
be sent again (bump the collision epoch, reset the overlay).

## 4. Coordinates

The protocol speaks Minecraft space everywhere: blocks, Y up, Z south. Convert at the edge of the
plugin, in one file, and nowhere else.

Decide four things and measure each in the running game:

1. **Scale.** Blocks per host unit. Pick it so the host's character is about 1.8 blocks tall.
2. **Axes.** Which host axis is up, and which sign maps to Minecraft's Z. Check it by holding W at
   a known Minecraft yaw and watching which host axis grows.
3. **Yaw and pitch.** Offset and sign. Minecraft yaw 0 looks along +Z, pitch is positive downwards.
4. **Vertical offset.** The guest's mirror world has a fixed build range (-1024 to 1023 in the
   current mod). If the host's playable world is taller or sits outside it, subtract a constant.
   Use a multiple of the collision region size (8) so region boundaries stay aligned.

Also measure **where the feet are**. Hosts rarely have a "feet" transform; find the character's
reference point and the distance down to the ground under it, with a probe, and store it as a
constant.

## 5. Collision export

The guest's player collides with **triangles** the host sends, using Minecraft's own movement code.
The host also sends a coarse 1/8-block occupancy grid, which the guest uses for everything that is
not the local player (holding the player during a teleport, other entities, block support).

### Where the triangles come from

In order of preference:

1. **Read the physics shapes** (exact). Possible when the host exposes them, as SkyCraft does with
   Havok.
2. **Ray-cast the physics world** (sampled). The fallback, and often the only choice: in a Unity
   build most collider meshes are not readable at runtime.

### Rebuilding surfaces from ray casts

This is the method PeakCraft uses. It works on any engine with a ray cast that can report front and
back faces.

- Work in **region columns**: 8 by 8 blocks, a vertical window of a few regions around the player.
- Sample a **half-block grid** of vertical columns in each region.
- For each grid column, cast down repeatedly and build a list of **solid spans** (top and
  underside). After each hit, test the gap between that hit and the surface above: cast up once
  for front faces and once for any face. A front face first means open air under a ceiling. A back
  face first, or nothing, means the gap is the inside of something solid, so the span continues.
- Join the four corners of each grid cell into triangles. Span tops within one block of each other
  are one floor. Undersides are joined the same way into ceilings.
- Where a floor does not reach all four corners, it ends against something. If the missing corner
  is solid at the floor's height, raise the surface to the top of what is there: a wall. If it is
  lower ground and the floor is solid down to it, drop the surface to that ground: a ledge face.
  An overhang's lip gets neither.
- Split quads along the flatter diagonal.

What to leave out of the ray casts: the player's own character, ropes and other things the host
moves, and anything with a non-kinematic rigid body (loose props are not world).

### Streaming

- **Time budget, not a count.** Spend about 2.5 ms a frame, nearest column first.
- **Refresh on a timer.** Near columns every few seconds, far ones every 45, so moving host
  geometry is picked up without events.
- **Evict by sending the region empty.** The guest keeps what it was last told.
- **Follow the player vertically.** A column sent for one height window must be re-sent when the
  player leaves it.
- **Epoch.** Any time the host's world changes under the player (scene load, streamed chunk swap,
  a new guest), bump the epoch and send a clear message. The guest drops everything.
- **Centre on where the guest will be**, not where it is, while a teleport is pending.

### Known limits of sampling

Anything thinner than the grid step can be walked through, sharp edges are rounded by up to one
step, and a steep wall made of small ledges can be climbed. Say so in the README.

## 6. Who owns the player

This is the part to design before writing any patch. Every patch and every module reads one value.

| State | Host movement | Host input | Host camera | Host HUD | Host character shown | Guest overlay and blocks |
|---|---|---|---|---|---|---|
| **HostOwns** | on | on | on | on | yes | off |
| **Handoff** | frozen | off | on | on | yes | off |
| **GuestOwns** | off | off | off | off | no | on |
| **HostMenu** | off | on | off | on | no | on |
| **Cutscene** | on | off | on | on | yes | on |

- **HostOwns**: no link, or no character. The host runs as if the plugin were not installed.
- **Handoff**: the guest has been asked to teleport to the host's character and has not confirmed.
- **GuestOwns**: normal play.
- **HostMenu**: a host window is open over a guest-owned body.
- **Cutscene**: the host itself is moving or showing the character: loading screens, scripted
  falls, being carried, death, endings, any camera override.

### The teleport handshake

The host state carries a `teleportSeq`; the guest state carries a `teleportAck`. To take over:

1. Publish the host character's position and increase `teleportSeq`.
2. Stay in Handoff until `teleportAck` equals `teleportSeq`.
3. Only then hide the host character and follow the guest.

Without the handshake the body snaps to wherever the guest's player happened to be. Start
`teleportSeq` from a value an earlier session cannot have used, because the guest may still hold an
old ack.

### Rules

- **Decide the state once per frame, in one function,** from the link, the host's flags and the
  guest's flags. Modules ask questions like "does the body follow?", never "what is the host doing?".
- **Link loss is just HostOwns.** Restoring movement, camera, HUD and the character in one
  transition is what makes "the guest crashed" safe.
- **List every way the host takes the character** by reading its code, and treat each as Cutscene.
  A missed one shows up as the two games fighting over the body.
- **A dead guest player is a Cutscene too.** When it respawns it is teleported back to the host's
  character through the normal handoff.
- **Tell the guest about it.** Set the Loading flag while in Cutscene (the guest parks its player
  and waits for a teleport) and the MenuOpen flag in HostMenu (the guest drops held keys).

## 7. The follower

While the guest owns the player, the host's character stays in the world as a hidden follower at the
guest's position. That keeps the host's triggers, hazards, checkpoints and scripts working without
any change.

- Make the character's physics kinematic and hide its renderers. Remember exactly which renderers
  you hid so releasing restores them.
- Move it by the difference between where its feet are and where the guest's feet are.
- **In a bone hierarchy, move only the topmost bodies.** Moving every rigid body by the same delta
  moves children once per ancestor; the character flies apart and positions become NaN.
- Each frame, write the state the host's skipped movement code would have written (grounded, not
  falling), so nothing downstream starts a fall or a pass-out.
- On release, restore physics, zero the velocity and reset the "time since grounded" style timers.

### Smooth motion

The guest's physics runs at 20 ticks a second. Its guest state carries the last two tick positions
and the timestamp of the latest tick. Interpolate between them on the host's own frame clock,
exactly as Minecraft's renderer does. Using the guest's already-interpolated position instead
produces judder, because the two games' frames are not in phase. Fall back to the reported position
when the tick pair is stale (just after a teleport).

## 8. Camera

- Overwrite the host camera **after** the host's own camera update, so anything the host aims with
  the camera (its interaction ray) uses the result.
- The look direction is owned by the host plugin: it integrates mouse movement with Minecraft's own
  sensitivity curve and publishes yaw and pitch in the host state. Write the same look back into
  the host's character so its own facing agrees.
- Position is the interpolated feet plus the guest's eye height. For third person use the guest's
  `cameraMode` and `cameraDistance`; the distance already includes Minecraft's zoom collision.
- Copy the guest's field of view. Check whether the host's is vertical or horizontal.
- Log the distance between the eye position you computed and the guest's reported eye every few
  seconds. It
  should be zero standing still and about one frame of movement while walking.

## 9. Input

The host window has focus and the guest is hidden, so the plugin reads the devices and re-encodes
them for the input ring: keys as SDL scancodes, mouse buttons as SDL numbers, scroll, text and an
absolute cursor.

- **Read raw device state**, not the host's bound actions. The host's bindings are for its own game.
- **Switch the host's own controls off at the source.** Patch the one place the host samples input
  and zero the gameplay fields. That disables jump, sprint, climbing and item use in one stroke,
  and is safer than patching each consumer.
- **Send release-all on every change of routing**: a host menu opens, a cutscene starts, the window
  loses focus, the link drops. Otherwise a key held at that moment stays held in Minecraft.
- **Two cursor modes.** With no guest screen open, mouse movement turns the look. With one open,
  the plugin keeps its own cursor position in overlay pixels, sends it, and draws a pointer.
- **Text** is separate from keys: forward typed characters only while a guest screen is open, and
  send key repeats for held keys in text fields.
- **Reserve a few keys for the host.** One "host interact" key, one "open guest menu" key, and Esc
  for the host's pause menu unless a guest screen is open, in which case Esc closes that screen.
  Pick keys Minecraft does not bind.

### One interact key

To use the host's own objects, do not re-implement its interaction system. Drive the host's
"interact pressed / held / released" input fields from the reserved key, and let its own targeting
run with the camera you wrote. Hold-to-interact objects then work for free. The host's prompt is
usually hidden with its HUD, so draw a small prompt of your own from its current target.

## 10. Drawing

Two separate paths.

### The overlay: HUD and screens

The guest renders its HUD, hand and open screens at the host's resolution (the host state carries
the viewport size) and publishes RGBA frames through the triple buffer. Upload the newest frame to
a texture when the dirty bit is set and draw it full-screen after the host's own UI. Check the
slot's row-order flag. Hide it under the host's own windows so their buttons stay clickable.

### The world: blocks, player model, entities

The guest sends real geometry through the render ring, and the host draws it in its own 3D scene,
so blocks are hidden by host geometry in front of them and lit by the host's lights.

| Message | What it is | Lifetime |
|---|---|---|
| Atlas | The block and item texture atlas | Until replaced |
| Atlas region | One animated sprite's current frame | Applied onto the atlas |
| Texture | An entity texture (skin, armour) by id | Until replaced |
| Section | A 16-block cube's triangle list; zero vertices removes it | Until replaced or cleared |
| Avatar | The player's posed model this frame | One frame |
| Scene | Every other entity and all particles this frame | One frame |
| World entities table | Arrows, dropped items, the block outline | Latest value |

Lessons, all learned the hard way:

- **Borrow a shader that ships with the host.** A mod loader usually cannot compile shaders. You
  need one that takes a texture, multiplies vertex colour (tint and ambient occlusion ride on it)
  and is lit by the host. In a URP game the particle shaders do this.
- **Shader variants the host never uses are stripped from its build.** If alpha-clip is missing,
  clipped texels draw black and still write depth. Do cutout on the CPU instead: keep each
  texture's alpha, classify every triangle by the texels it covers (all opaque, all clear, mixed),
  drop the empty ones, and draw only the mixed ones with a blended material that writes depth.
- **Never re-upload the whole atlas for an animated sprite.** The atlas is tens of megabytes and
  sprites animate every frame; that exhausted GPU memory and crashed the host. Upload the small
  region to a scratch texture and copy it into the atlas on the GPU.
- **Sections are persistent objects, per-frame models are not.** Keep one mesh per section, and
  rebuild avatar and scene meshes each frame.
- **Z flips mirror winding.** If the conversion negates an axis, reverse triangle order and swap
  the min and max of that axis in boxes.
- **First person and mirrors.** The player must not see their own model in first person, but the
  host's reflections and mirrors should. Draw the model only for those cameras, on a layer each
  one actually renders.
- **Clear everything on link loss** so nothing of the guest's stays on screen.
- **Bound the bytes drained per frame**, and skip unknown message types by length so a newer guest
  does not break an older plugin.

## 11. Damage and death

The guest's health is the only health.

- **Host hurts player.** Find the one function every hazard goes through. While the body follows,
  intercept it: accumulate the amount, skip the host's own handling, and send one hurt event at
  most every half second (Minecraft ignores repeat hits inside its invulnerability window). Also
  clear any status the host sets directly, so the host can never knock the hidden character out.
- **Scale it by measurement.** Compute how long a hazard takes to kill at scale 1 and compare with
  Minecraft's natural healing. PeakCraft's first value was slower than healing; the default became 3.
- **Instant kills** in the host become one very large hurt event rather than a host death.
- **Creative is the safe mode for free.** A Creative guest ignores hurt events, so the game-mode
  switch is the whole "hazards on or off" feature.
- **Guest dies.** The death event runs the host's own death for the character, guarded by a flag
  so the plugin's own instant-kill intercept lets it through.
- **Respawn the guest yourself.** The guest sits behind a death screen the player cannot easily
  reach during the host's death flow. Click its Respawn button through the input ring.

## 12. Patching the host

- **Decompile first, write a notes file second, code third.** One table: concern, class and
  member, kind of patch, why. Every unit of work cites it. Do not commit decompiled source.
- **Learn the host's update order.** Which script samples input, which moves the character, which
  places the camera, which does interaction, and in what phase. Most ordering bugs are answered by
  that table.
- **Patch the funnel, not the consumers.** One input sampler, one movement step, one camera
  update, one hazard function, one death function. PeakCraft ships five patch classes on six methods.
- **Prefer calling the host's own methods to patching.** Hiding the HUD, stopping an action,
  killing the character and warping are usually public calls.
- **Apply patches one at a time and log each.** If one fails, unpatch everything and disable the
  plugin. A half-patched host is worse than an unmodified one.
- **Every patch checks ownership first** and does nothing in HostOwns.
- **Use unscaled time.** Pause menus often set the time scale to zero; timers on scaled time stop.

## 13. Testing without a hand on the mouse

A build-launch-test cycle with two games is minutes long. These made it workable.

- **A fake guest.** A script of about 90 lines (`tools/fake_minecraft.py`) that opens the mapping, stamps the
  heartbeat and writes a scripted player state. It proves the link, the layout and link loss before
  Minecraft is involved. `tools/fake_skyrim.py` does the same for the guest's side.
- **A dev harness in the plugin**, behind a config switch that is off by default. It polls a
  command file and runs one command per line: screenshot, probe (print measured facts), key and
  mouse events, teleport, start a session, toggle a hazard. With it, every acceptance check is a
  script and a screenshot.
- **Log state transitions and a periodic one-line summary** (owner, frame rate, guest position,
  teleport ack). Most bugs were found by reading that line.
- **Wait for a new log line, never read the log once.** Stale reads cost several false conclusions.
- **Measure facts in the running game** (character height, feet offset, layers, world height range,
  key bindings) and write them into the notes file. Guesses about these are wrong.
- **Expect the user to play during tests.** Scripted readings mix with their input; re-run rather
  than trust one sample, and say before restarting a game.

## 14. Packaging

- Ship the host plugin and the guest jar together, and say plainly which file goes where.
- The guest needs `--enable-native-access=ALL-UNNAMED` in its JVM arguments.
- State the start order (guest first, waiting on its title screen, then the host).
- Document the limits honestly: what sampled collision misses, what does not cross between the
  games, what the plugin hides.
- Verify the package by installing it into a clean profile and reading the log for the load line,
  the self-checks and "link up".
- Keep the upstream licence and credit SkyCraft.

## 15. Checklist for a new host

1. Set up the mod loader, a clean profile and a way to launch the host modded from a script.
2. Decompile; write the notes file with update order and patch points.
3. Mirror the protocol structs; add the layout self-check and ring self-test.
4. Create the mapping; heartbeat; link up and down against the fake guest.
5. Measure scale, axes, yaw, feet offset and world height range. Write the coordinate file.
6. Export collision. Check the guest stands at the host's ground height.
7. Write the ownership state machine and its table. List every Cutscene condition.
8. Follower, then camera, then input, each checked on its own.
9. Overlay, then world drawing. Drain the render ring from the first day.
10. Hazards, death, respawn.
11. One interact key and a prompt.
12. Link-loss test: kill the guest mid-play, confirm the host recovers, restart the guest, confirm
    it links again without restarting the host.
13. Package, install into a clean profile, read the log.
