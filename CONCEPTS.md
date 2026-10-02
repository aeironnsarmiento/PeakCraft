# Concepts

Shared domain vocabulary for this project — entities, named processes, and status concepts with project-specific meaning. Seeded with core domain vocabulary, then accretes as ce-compound and ce-compound-refresh process learnings; direct edits are fine. Glossary only, not a spec or catch-all.

## The two games

### Guest
The hidden Minecraft client that owns the player's physics, inventory, HUD and blocks.
*Avoid:* client, MC side

The Guest is the same for every Host. It waits for a Link, then opens its Mirror World.

### Host
The game the player actually sees, which owns the world, the picture, the window and the keyboard.
*Avoid:* main game, Skyrim side, PEAK side

### Host plugin
The mod inside the Host that translates between the Host and the Guest.

### Mirror World
The Guest's empty world, in which only the blocks the player places exist and the Host's ground arrives as collision data.

## The link

### Link
The shared memory both games read and write, and the state of both sides seeing each other alive.

The Host creates it and the Guest opens it. Each side stamps a Heartbeat; the Link is up while the other side's stamp is recent. A different Guest process on the other end counts as a new Link, so everything sent before is sent again.

### Heartbeat
A timestamp each side writes every frame so the other can tell it is alive.

### Protocol
The fixed byte layout of the Link, defined once and mirrored by each side.

### Collision epoch
A counter the Host raises whenever its world changes under the player, telling the Guest to drop all collision it was sent.

## Ownership

### Ownership
The single per-frame decision of which game controls the player's body, camera, input and HUD.

States: Host owns, Handoff, Guest owns, Host menu, Cutscene. Losing the Link always returns to Host owns.

### Handoff
The Ownership state in which the Guest has been asked to teleport to the Host's character and has not yet confirmed.

The Host keeps the camera and the visible character, with its controls frozen, until the Guest acknowledges the teleport.

### Cutscene
The Ownership state in which the Host itself is moving or showing the character, such as loading, scripted falls, death and endings.

### Follower
The Host's own character while the Guest owns the player: hidden, no longer simulated, and moved to the Guest's position each frame so the Host's triggers and hazards still see it.
*Avoid:* puppet

## Drawing

### Overlay
The Guest's HUD and open screens, sent as a full-screen image and drawn over the Host's picture.

### Render ring
The stream of geometry and textures the Guest sends so the Host can draw blocks, the player's model and other entities inside its own scene.

The Guest stalls while the stream is full, so the Host must read it every frame whether or not it draws.
