# Lethal Company notes

Decompile findings for the LethalCraft plugin (`lethal/`). Lethal Company v81 (Steam build 22825947), Unity 2022.3.62f2, HDRP, Mono, Netcode for GameObjects 1.12, BepInEx 5.4.23.5.
Source: `ilspycmd` on `Lethal Company_Data/Managed/Assembly-CSharp.dll` (output kept local in `.tools/lc-src`, never committed).

Every unit cites this file instead of guessing names. Where a patch shipped, the "Patch" column says so.

## Script order inside one frame

| Order | Class | Phase | What it does |
|---|---|---|---|
| -1000 | `LethalCraft.Plugin` | Update | reads Minecraft's state, decides ownership, places the follower, exports collision, pumps input, publishes the host state |
| 0 | `PlayerControllerB` | Update | reads `InputSystem.actions` ("Move", "Sprint"), sprint/crouch/jump state, `thisController.Move(...)`, fall values, `ClickHoldInteraction()` at the very end |
| 0 | `PlayerControllerB` | LateUpdate | physics-region parenting, `PlayerLookInput()` (turns `transform` by yaw and `cameraContainerTransform` by `cameraUp`), sprint meter, held-item offsets, `SetHoverTipAndCurrentInteractTrigger()` (the interact ray from `gameplayCamera`), local arms follow `cameraContainerTransform` |
| 0 | `HUDManager` | Update | HUD elements fade by `HUDElement.canvasGroup.alpha` towards `targetAlpha` |

So the camera written in a postfix of `PlayerControllerB.LateUpdate` is the camera the hover targeting (same LateUpdate, before the postfix: it used last frame's camera) and the renderer use; the hover target is at most a frame behind, which the interact arbitration tolerates.

## Concerns and patch points

| Concern | Class.member | Patch | Notes |
|---|---|---|---|
| Local scout | `GameNetworkManager.Instance.localPlayerController` | none | null in the menus; `isPlayerControlled` once spawned in the ship, `isPlayerDead` while dead (still the local one: it spectates) |
| Input actions | `InputSystem.actions` (the project-wide asset; `FindAction("Interact")` etc.), `PlayerControllerB.playerActions.Movement.Look` (a separate generated wrapper, used only for Look and the terminal's Esc) | called | `IngamePlayerSettings` itself calls `InputSystem.actions.Disable()/Enable()` around rebinding, so disabling the asset while Minecraft has the input is the game's own idiom. `playerActions.Movement` stays enabled; its Look is read in `PlayerLookInput`, which is skipped |
| Look | `PlayerControllerB.PlayerLookInput()` (private) | prefix, skip | the plugin writes `transform.eulerAngles.y`, `cameraUp` and `cameraContainerTransform.localEulerAngles.x` instead (`CameraDriver.ApplyLookToHost`) |
| Movement | `PlayerControllerB.Update()` → `thisController.Move(...)` | postfix | the CharacterController is disabled while the body follows (`Follower.Take`), so `Move` moves nothing; the postfix re-zeroes `fallValue`, `fallValueUncapped`, `takingFallDamage`, `isFallingNoJump`, `isFallingFromJump`, `isJumping`, `sprintMeter` |
| Fall damage | `PlayerHitGroundEffects()` (from Update when the controller becomes grounded) | none | never runs: the disabled controller is never grounded. `DamagePlayer(..., fallDamage: true)` is dropped anyway |
| Camera | `PlayerControllerB.LateUpdate()` | postfix | `CameraDriver.Apply` sets `cameraContainerTransform` and `gameplayCamera.transform` position/rotation and `gameplayCamera.fieldOfView` (also `targetFOV`, which Update lerps towards). Not applied while the host has the camera (HostOwns, Handoff, Cutscene) |
| Interact (press) | `Interact_performed` (private, needs a performed context) | replicated in `Input/Interact.Press` | `BeginGrabObject()` + `hoveringOverTrigger.Interact(thisPlayerBody)` with the same guards |
| Interact (hold) | `ClickHoldInteraction()` (public, reads `InputSystem.actions.FindAction("Interact").IsPressed()`) | prefix, skip | replicated with the plugin's key state (`Input/Interact.Hold`) |
| Interact target | `hoveringOverTrigger`, `cursorTip`, `interactRay` with `grabDistance` (5) on `interactableObjectsMask` (0x40000340: layers 6 Props, 8 Room, 9 InteractableObject, 30), tags `PhysicsProp` / `InteractTrigger`; layer 8 and 30 hits are ignored | read | `Interact.HasTarget` repeats the ray for the right-mouse arbitration |
| Teleports | `TeleportPlayer(Vector3 pos, ...)` (entrance doors via `EntranceTeleport`, ship teleporters, `StartOfRound` spawn at a new day and revive) | postfix | `Follower.HostMoved` + `Ownership.HostTeleported`: the body stays where the host put it, Minecraft is asked to follow, and the host position is published until it acknowledges |
| Damage | `DamagePlayer(int damageNumber, bool hasDamageSFX, bool callRPC, CauseOfDeath, int deathAnimation, bool fallDamage, Vector3 force)` | prefix, skip | every hazard (turret, landmine, spike trap, enemies' hits) ends here; while the body follows the amount goes to `DamageBridge` (fall damage dropped) |
| Death | `KillPlayer(Vector3 bodyVelocity, bool spawnBody, CauseOfDeath, int deathAnimation, Vector3 positionOffset, bool setOverrideDropItems)` | prefix, skip (lethal Minecraft damage) | a Minecraft death calls it back with `Death.Killing` set. Dead = Cutscene until `StartOfRound.ReviveDeadPlayers` (end of the day) teleports the scout to the ship |
| Menus | `quickMenuManager.isMenuOpen` (pause menu: Esc through `OpenMenu_performed`), `isTypingChat`, `inSpecialMenu` | polled | HostMenu state. `inTerminalMenu` (set by `Terminal.BeginUsingTerminal`) is a Cutscene: the terminal animates the camera and takes the keyboard |
| Ship | `StartOfRound.inShipPhase` (true in orbit until `StartGame`, true again after the end-of-day stats), `shipHasLanded` (true from the end of `openingDoorsSequence` until `ShipLeave`), `shipIsLeaving` (true from `ShipLeave` until the ship is back in orbit), `travellingToNewLevel`, `firingPlayersCutsceneRunning`, `suckingPlayersOutOfShip` | polled | landing = `!inShipPhase && !shipHasLanded && !shipIsLeaving`; leaving = `shipIsLeaving`. Both are Cutscene states. World slot: moon while `shipHasLanded`, orbit while `inShipPhase && !shipIsLeaving` |
| Special animations | `inSpecialInteractAnimation` (ladders, chairs, the terminal), `isClimbingLadder`, `inAnimationWithEnemy`, `inVehicleAnimation`, `jetpackControls`, `isFreeCamera` | polled | Cutscene |
| HUD | `HUDManager.Instance.Inventory`, `PlayerInfo`, `Tooltips`, `Clock`, `Chat` (`HUDElement.canvasGroup`), `playerScreenTexture` (RawImage of the low-res game view), `HideHUD(bool)` (all-or-nothing animator) | written | Inventory and PlayerInfo alpha forced to 0 while Minecraft owns the body; `PingHUDElement` brings them back |
| Hosting | `GameNetworkManager.Instance.lobbyHostSettings = new HostSettings(name, isPublic, tag)` then `StartHost()` (what `MenuManager.ConfirmHostButton` does) | dev harness | from the `MainMenu` scene |
| Scenes | `InitSceneLaunchOptions` → `InitScene` → `MainMenu` → `SampleSceneRelay` (the ship, in orbit); moons load additively (`StartOfRound.StartGame` → `NetworkManager.SceneManager.LoadScene(currentLevel.sceneName, Additive)`) and unload when the ship has left | `SceneManager.sceneLoaded/sceneUnloaded` | collision epoch bumps on both |
| Launching | the exe relaunches itself through Steam without its arguments: start it with `steam.exe -applaunch 1966720 --doorstop-enabled true --doorstop-target-assembly <profile>\BepInEx\core\BepInEx.Preloader.dll` (`tools/launch_lethal.bat`) | — | BepInEx's profile log is `<profile>\BepInEx\LogOutput.log`; set `WriteUnityLog = true` in `BepInEx.cfg` to see exceptions |

## Runtime facts

Measured in game with the plugin's dev probe (`[Dev] Harness = true`, command `probe`). Fill in as they are measured.

| Fact | Value | Used by |
|---|---|---|
| Physics layers | 0 Default, 8 Room, 11 Colliders, 24 MiscLevelGeometry, 25 Terrain, 28 Railing, 29 DecalStickableSurface are what `LayerMask.GetMask` resolved for the collision sampler (mask 0x33000901). Others from the masks in code: 3 Player, 6 Props, 9 InteractableObject, 19 Enemies, 30 (used in `interactableObjectsMask`, ignored on hit) | `World/CollisionExporter.cs` |
| Masks in code | `StartOfRound.walkableSurfacesMask` 0x52000109, `collidersAndRoomMask` 0x42000100, `collidersAndRoomMaskAndDefault` 0x42000101, `allPlayersCollideWithMask` 0xBDBFFFB7, `PlayerControllerB.interactableObjectsMask` 0x40000340 | `World/CollisionExporter.cs`, `Input/Interact.cs` |
| Scout size | `thisController.height` 2.5 standing (centre y 1.28), 1.5 crouched (centre y 0.72); the controller root is at the feet | `World/Coords.cs` (`UnitsPerBlock` 1.25: a 1.8-block player is 2.25 units) |
| Camera height | to measure (`probe` logs `gameplayCamera` local position) | `World/Coords.cs` |
| Axes | Minecraft X = Unity X / scale, Minecraft Z = -Unity Z / scale, Minecraft yaw = Unity yaw + 180, Minecraft pitch = `cameraUp` (both positive looking down) | `World/Coords.cs` |
| Block material | HDRP/Lit, cloned from materials the game has loaded (opaque / alpha-clipped / transparent); HDRP/Lit ignores vertex colour, so faces are grouped by tint per material | `Render/Atlas.cs` |

## Patches that shipped

| Patch class | Method | Kind |
|---|---|---|
| `PlayerControllerBPatch` | `PlayerControllerB.Update` | postfix |
| `PlayerControllerBPatch` | `PlayerControllerB.PlayerLookInput` | prefix, skip |
| `PlayerControllerBPatch` | `PlayerControllerB.LateUpdate` | postfix |
| `PlayerControllerBPatch` | `PlayerControllerB.ClickHoldInteraction` | prefix, skip |
| `PlayerControllerBPatch` | `PlayerControllerB.TeleportPlayer` | postfix |
| `PlayerControllerBPatch` | `PlayerControllerB.DamagePlayer` | prefix, skip |
| `PlayerControllerBPatch` | `PlayerControllerB.KillPlayer` | prefix, skip |

Called but not patched: `InputSystem.actions.Disable/Enable`; `BeginGrabObject`, `InteractTrigger.Interact`, `HUDManager.HoldInteractionFill`; `KillPlayer` (on a Minecraft death); `HUDManager.PingHUDElement`; `GameNetworkManager.StartHost`, `StartOfRound.StartGame`, `EndGameServerRpc`, `ChangeLevelServerRpc`, `TeleportPlayer` (dev harness only).
