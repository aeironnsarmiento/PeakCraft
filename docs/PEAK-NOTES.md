# PEAK notes

Decompile findings for the PeakCraft plugin (`peak/`). PEAK 2.5.a, Unity 6000.3.15, BepInEx 5.4.23.3.
Source: `ilspycmd` on `PEAK_Data/Managed/Assembly-CSharp.dll` (output kept local in `.tools/peak-src`, never committed).

Every unit cites this file instead of guessing names. Where a patch shipped, the "Patch" column says so.

## Script order inside one frame

| Order | Class | Phase | What it does |
|---|---|---|---|
| -100 | `Character` | Update / FixedUpdate | life, pass-out, death state; ragdoll control value |
| -99 | `CharacterRagdoll` | FixedUpdate | evaluates the animator by hand, saves the target pose per bodypart |
| -1 | `Bodypart` | — | one rigidbody of the ragdoll |
| 0 | `CharacterMovement` | Update | **samples input**, look, jump, sprint; FixedUpdate adds forces to every bodypart |
| 0 | `CharacterClimbing` | Update | starts and runs wall climbing from input |
| 500 | `MainCameraMovement` | LateUpdate | places the camera (`CharacterCam`, `Spectate`, `OverrideCam`) |
| 600 | `Interaction` | LateUpdate | ray from `MainCamera.instance.transform`, reads `input.interact*`, calls `IInteractible.Interact` |
| 0 | `GUIManager` | LateUpdate | window status, pause menu, reticle, prompts |

So a camera written in a postfix of `MainCameraMovement.LateUpdate` is the camera `Interaction` aims with.

## Concerns and patch points

| Concern | Class.member | Patch | Notes |
|---|---|---|---|
| Local character | `Character.localCharacter` (static) | none | set in `Character.Awake` when `view.IsMine`; null in Title scene and during loads |
| Input actions | `CharacterInput.Sample(bool)` | postfix | called from `CharacterMovement.Update`. Postfix zeroes the gameplay fields and sets `interact*` from the plugin's own key. `pauseWasPressed` is left as PEAK read it (Esc opens PEAK's menu) |
| Look | `CharacterMovement.CameraLook()` | prefix, skip | adds `input.lookInput * sensitivity` to `data.lookValues`. The plugin owns `lookValues` instead |
| Movement | `CharacterMovement.FixedUpdate()` | prefix, skip | per-bodypart animate, gravity, movement force, drag. Skipped while Minecraft owns the body |
| Jump, sprint, crouch | `CharacterMovement.Update()` | none | all read `character.input`, which the `Sample` postfix has zeroed |
| Ground / fall damage | `CharacterMovement.CheckFallDamage` (via `EvaluateGroundChecks` in FixedUpdate) | none | never runs because FixedUpdate is skipped |
| Climbing | `CharacterClimbing.Update()` → `TryToStartWallClimb` | none | starts only from `input.usePrimary*`, which is zeroed. `Character.refs.climbing.StopAnyClimbing()` is called when taking over |
| Rope / vine | `CharacterRopeHandling.Update`, `CharacterVineClimbing.Update` | none | started by interacting with a rope or vine; the follower keeps the body at Minecraft's position regardless |
| Stamina | `Character.UseStamina` / `StaminaBar` | none | nothing spends stamina once movement and climbing are off. The bar is hidden with the HUD |
| Ragdoll | `CharacterRagdoll.ToggleKinematic(bool)`, `HaltBodyVelocity()`, `partList` | called, not patched | follower makes all rigidbodies kinematic and moves the topmost bodypart (the hip; the others are its children) by the delta between the character's feet and Minecraft's |
| Character position | `Character.Center` (torso bodypart), `Character.Head`, `GetBodypart(BodypartType.Hip)` | none | there is no root transform that moves; the bodyparts are the character |
| Warp | `Character.WarpPlayer(Vector3, bool)`, `Character.warping` | none | PEAK's own teleports (checkpoints, death pos, revive). While `warping` the plugin is in Cutscene state |
| Character visibility | `Renderer.enabled` on everything under the character; `HideTheBody` only fades the body for PEAK's own first person | called, not patched | the plugin disables the character's `Renderer`s while Minecraft owns the body and re-enables them otherwise; the held item is unequipped (`CharacterItems.EquipSlot(None)`) |
| Camera | `MainCameraMovement.LateUpdate()` | postfix | plugin overwrites `MainCamera.instance.transform` and `MainCamera.instance.cam.fieldOfView` after it. Not applied when `isGodCam`, when `cam.camOverride` is set, or when spectating (`fullyPassedOut`) |
| Camera overrides | `MainCamera.camOverride` (set by `CameraOverride.DoOverride`, binoculars) | none | treated as Cutscene state |
| Interact | `Interaction.LateUpdate()` → `DoInteractableRaycasts`, `DoInteraction` | none | driven through `input.interactWasPressed / IsPressed / WasReleased`, so hold-to-interact (kiosk, campfire, luggage) works through the same key |
| Menus | `GUIManager.instance.windowBlockingInput`, `GUIManager.instance.windowShowingCursor`, `GUIManager.InPauseMenu`, `MenuWindow.AllActiveWindows` | none | boarding pass (`BoardingPass : MenuWindow`), pause menu and end screen all show here. `GUIManager.instance.wheelActive` for the emote/backpack wheels |
| Cursor | `CursorHandler` (sets `Cursor.lockState`) | none | PEAK locks the cursor in gameplay and frees it for windows; the plugin does not touch it |
| HUD | `GUIManager.instance.hudCanvas` (`Canvas`), `hudCanvasGroup`, `staminaCanvasGroup`, `bar` (`StaminaBar`) | called, not patched | plugin toggles `hudCanvas.enabled`; pause menu and boarding pass are on other canvases |
| Afflictions | `CharacterAfflictions.AddStatus(STATUSTYPE, float, …)` | prefix | every hazard funnels through here (fog cold: `Fog.MakePlayerCold`). Prefix accumulates the amount and skips the original while Minecraft owns the body |
| Afflictions set directly | `CharacterAfflictions.SetStatus` (weight, thorns) and `currentStatuses[]` | called | plugin zeroes `currentStatuses` each frame while the body follows, so nothing can pass the character out |
| Airport immunity | `CharacterAfflictions.m_inAirport` | none | `AddStatus` already returns early in the airport |
| Death | `Character.DieInstantly()` (checkpoint first), `RPCA_Die`, `data.dead`, `data.fullyPassedOut` | called and prefixed | Minecraft's death event calls `DieInstantly()` on the local character; PEAK's own calls (kill planes) become lethal Minecraft damage while the body follows |
| Run end | `Character.CheckEndGame` → `RPCEndGame` → `GUIManager.instance.endScreen.Open()` | none | all players dead ends the run; solo death goes straight to the end screen and back to the airport |
| Scene load | `LoadingScreenHandler.loading` (static), `LoadingScreenHandler.OnLoadingStart / OnLoadingDone`, `SceneManager.sceneLoaded` | subscribed | Title → Airport (`MainMenu.StartOfflineModeRoutine`), Airport → island (`AirportCheckInKiosk.BeginIslandLoadRPC`, loading screen type `Plane`) |
| Segment change | `MapHandler.GoToSegment(Segment)`, `Singleton<MapHandler>.Instance.GetCurrentSegment()` | polled | old segment's `segmentParent` is deactivated; plugin bumps the collision epoch when the current segment changes |
| Flight and crash | Plane loading screen, then `Character.StartPassedOutOnTheBeach()` → `data.passedOutOnTheBeach = 3`, `Fall(7f)` → `data.fallSeconds` | polled | Cutscene state while `LoadingScreenHandler.loading`, `data.fallSeconds > 0`, `data.passedOutOnTheBeach > 0`, `data.passedOut`, `data.fullyPassedOut`, `data.dead`, `character.warping`, `data.isCarried`, or a camera override is set |
| Ending | `PeakHandler.EndCutscene()`, `EndScreen` | polled | `Character.refs.stats.won` / `GUIManager.instance.endScreen.isOpen` |
| Offline start | `MainMenu.PlaySoloClicked()` → `StartOfflineModeRoutine()` | called by the dev harness only | `PhotonNetwork.OfflineMode = true`, loads `Airport` |

## Runtime facts

Measured in game with the plugin's dev probe (`[Dev] Harness = true`, command `probe`).

| Fact | Value | Used by |
|---|---|---|
| Character height | ragdoll colliders span 1.99 units standing; PEAK's camera sits 1.62 above the feet, Minecraft's eye height exactly. One block is one unit (R8) | `World/Coords.cs` |
| Feet below `Character.Center` | 1.089 units | `Coords.CenterAboveFeet` |
| Airport world Y range | colliders from -15 to 38 | Y offset |
| Island world Y range | shore -5, first campfire 285, Alpine 450, Caldera 757, Kiln 858, peak marker 1188 (`MountainProgressHandler.progressPoints`); far scenery colliders reach -2035 and 2605 | Y offset |
| Y offset | Minecraft Y = Unity Y - 512, so the playable mountain is Minecraft -517 to about 700, inside the mirror dimension's -1024 to 1023 | `[World] YOffset` |
| Axes | Minecraft X = Unity X, Minecraft Z = -Unity Z, Minecraft yaw = Unity yaw + 180, Minecraft pitch = -`lookValues.y`. Checked in game: W at Minecraft yaw 180 moves Unity +Z, yaw -90 moves +X | `World/Coords.cs` |
| Default key bindings | PEAK: E interact, Q drop, R emote, G/T scroll, Tab unselect, V push to talk, 1-9 hotbar, Esc pause, Space jump, Shift sprint, Ctrl crouch, mouse buttons use/ping. `UI/UIConfirm` is Enter and E, `UI/UICancel` is Esc | interact key G and Minecraft menu key O (SkyCraft's choices; unbound in Minecraft, and PEAK's own bindings are off while Minecraft owns the body) |
| Physics layers | world collision is on `Terrain`, `Map` and `Default`; `Character` and `Rope` are left out of the collision ray casts. Loose items on `Default` are skipped by their non-kinematic rigidbody | `World/CollisionExporter.cs` |
| Collider meshes | airport: 163 mesh colliders, 6 readable; island: 3091, 798 readable. Not readable in general, so collision is ray-cast (KTD6) | `World/CollisionExporter.cs` |
| Block shader | `Universal Render Pipeline/Particles/Simple Lit`: multiplies vertex colour, lit by PEAK. Its alpha-clip variant is stripped from PEAK's build (clipped texels draw black and still write the depth prepass), so cutout is done by classifying faces on the CPU and blending the ones with holes | `Render/Atlas.cs` |
| Mirror | `Mirror.mirrorCamera` renders the airport mirror to a texture; culling mask 0x20308C37 includes layer 0 | `Render/RenderRing.cs` |
| Pause menu | `PauseMenuHandler.OnEnable` sets `Time.timeScale = 0` in offline mode, so the plugin times everything with unscaled time | all modules |
| Fog | `FogSphere.SetSharderVars` adds 0.0105 cold per second outside the sphere when `ENABLE` is 1: a full status bar in 95 s | `Hazards/AfflictionBridge.cs` |

## Patches that shipped

| Patch class | PEAK method | Kind |
|---|---|---|
| `CharacterInputPatch` | `CharacterInput.Sample` | postfix |
| `CharacterMovementPatch` | `CharacterMovement.FixedUpdate`, `CharacterMovement.CameraLook` | prefix, skip |
| `MainCameraMovementPatch` | `MainCameraMovement.LateUpdate` | postfix |
| `CharacterAfflictionsPatch` | `CharacterAfflictions.AddStatus` | prefix, skip |
| `CharacterPatch` | `Character.DieInstantly` | prefix, skip (kill planes become lethal Minecraft damage) |

Called but not patched: `CharacterRagdoll.ToggleKinematic`, `HaltBodyVelocity`; `CharacterClimbing.StopAnyClimbing`; `CharacterItems.EquipSlot`; `GUIManager.hudCanvas`; `Interaction.bestInteractable`; `Character.DieInstantly` (on a Minecraft death); `OrbFogHandler`, `MainMenu.PlaySoloClicked`, `BoardingPass.StartGame`, `EndScreen.ReturnToAirport`, `Character.WarpPlayer` (dev harness only).

Differences from the first table above, found while building:

- The follower does not use `CharacterRagdoll.MoveAllRigsInDirection`. The bodyparts are a bone hierarchy, so moving every rigidbody's transform moves children twice; only the topmost bodypart (the hip) is moved.
- The plane flight and crash are covered by the loading screen: PEAK's pass-out on the beach (`StartPassedOutOnTheBeach`, `Fall(7f)`) has run out by the time the loading screen ends, so the log shows `Cutscene (loading)` and then the handoff.
- Minecraft being dead (`kMcDead`) is also a Cutscene state, so a respawned Minecraft player is teleported to PEAK's character.

## Scenes

`Pretitle` → `Title` (main menu) → `Airport` → island scene named by `MapBaker.GetLevel(index)` (fallback `WilIsland`).
`GameHandler.IsGameplayScene` / `CharacterAfflictions.m_inAirport` tell them apart by name.
