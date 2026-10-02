using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PeakCraft.Dev;
using PeakCraft.Hazards;
using PeakCraft.Input;
using PeakCraft.Link;
using PeakCraft.Patches;
using PeakCraft.Player;
using PeakCraft.Render;
using PeakCraft.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakCraft;

/// <summary>
/// PeakCraft: the PEAK half of SkyCraft's two-game link. Entry point, config and per-frame order.
///
/// Each frame: read Minecraft's state, decide who owns the player, place the follower, export
/// collision around the new position, pump input, publish the host state. After PEAK's camera
/// update the camera is overwritten (MainCameraMovementPatch), and the overlay is drawn last (OnGUI).
/// </summary>
[BepInAutoPlugin]
[DefaultExecutionOrder(-1000)]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    internal static Plugin Instance { get; private set; } = null!;

    internal PeakLink Link { get; private set; } = null!;
    internal Ownership Ownership { get; } = new();
    internal Follower Follower { get; private set; } = null!;
    internal CameraDriver Camera { get; } = new();
    internal Interact Interact { get; } = new();
    internal InputBridge InputBridge { get; private set; } = null!;
    internal CollisionExporter Collision { get; private set; } = null!;
    internal Overlay Overlay { get; private set; } = null!;
    internal RenderRing Blocks { get; private set; } = null!;
    internal AfflictionBridge Hazards { get; private set; } = null!;
    internal Death Death { get; private set; } = null!;
    internal DevHarness? Dev { get; private set; }

    /// <summary>Minecraft's state as read this frame.</summary>
    internal Proto.GuestState Guest => guest;

    private ConfigEntry<string> linkName = null!;
    private ConfigEntry<float> yOffset = null!;
    private ConfigEntry<bool> devHarness = null!;
    private Harmony? harmony;
    private bool linked;
    private uint guestPid;
    private Proto.GuestState guest;
    private Segment lastSegment;
    private float segmentResetAt = -1f;
    private float lastStateLog;
    private bool hudHidden;
    private GUIStyle? promptStyle;

    private void Awake()
    {
        Log = Logger;
        Instance = this;
        linkName = Config.Bind("Link", "MappingName", Proto.MappingName,
            "Name of the shared-memory mapping. Must match Minecraft's -Dskycraft.link (default Local\\SkyCraft_v1).");
        yOffset = Config.Bind("World", "YOffset", -512f,
            "Added to PEAK's height to get Minecraft's. Keeps the mountain inside the mirror world's build range (-1024 to 1023). Use a multiple of 8.");
        devHarness = Config.Bind("Dev", "Harness", false,
            "Development only: auto-start an offline session and run commands from BepInEx/peakcraft-dev.txt.");
        Coords.YOffset = yOffset.Value;

        Log.LogInfo($"Plugin {Name} {Version} is loaded!");

        bool layoutOk = Proto.SelfCheck(Log);
        bool ringsOk = Rings.SelfTest(Log);
        Link = new PeakLink(Log);
        if (!layoutOk || !ringsOk || !Link.Create(linkName.Value) || !ApplyPatches())
        {
            Log.LogError("PeakCraft is disabled for this session: PEAK runs unmodified.");
            enabled = false;
            return;
        }
        Follower = new Follower(Link);
        InputBridge = new InputBridge(Link, Camera, Interact, Config);
        Collision = new CollisionExporter(Link);
        Overlay = new Overlay(Link);
        Blocks = new RenderRing(Link, Config);
        Hazards = new AfflictionBridge(Link, Config);
        Death = new Death(Link);
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (devHarness.Value)
        {
            Dev = new DevHarness();
            DevCommands.Register(this, Dev);
        }
    }

    /// <summary>One patch class per PEAK class. A patch that fails to apply disables the plugin rather than half-running.</summary>
    private bool ApplyPatches()
    {
        harmony = new Harmony(Id);
        Type[] patches = { typeof(CharacterInputPatch), typeof(CharacterMovementPatch), typeof(MainCameraMovementPatch), typeof(CharacterAfflictionsPatch), typeof(CharacterPatch) };
        foreach (Type patch in patches)
        {
            try
            {
                harmony.CreateClassProcessor(patch).Patch();
                Log.LogInfo($"patch ok   {patch.Name}");
            }
            catch (Exception e)
            {
                Log.LogError($"patch FAIL {patch.Name}: {e.Message}");
                harmony.UnpatchSelf();
                return false;
            }
        }
        return true;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Follower?.Release();
        harmony?.UnpatchSelf();
        Link?.Close();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Collision.Reset($"scene {scene.name}");
    }

    private void Update()
    {
        Link.Heartbeat();
        Dev?.Update();

        bool nowLinked = Link.GuestAlive;
        if (nowLinked && (!linked || Link.GuestPid != guestPid))
        {
            guestPid = Link.GuestPid;
            Log.LogInfo($"link up: Minecraft (pid {guestPid}) is on the other end");
            // A (new) Minecraft knows nothing we sent before.
            Collision.Reset("Minecraft linked");
            Link.ResetOverlay();
        }
        else if (!nowLinked && linked)
        {
            Log.LogInfo("link down: Minecraft's heartbeat stopped; PEAK has the player");
            Overlay.Clear();
            Blocks.Reset();
        }
        linked = nowLinked;
        if (linked && Link.ReadGuestState(out Proto.GuestState state))
        {
            guest = state; // on a torn read keep last frame's
        }
        DrainEvents();
        Character? character = Character.localCharacter;
        Owner before = Ownership.State;
        Ownership.Update(linked, guest);
        Owner now = Ownership.State;
        if (character != null && Ownership.PeakCamera)
        {
            Camera.SetLookFromPeak(character); // Minecraft starts out looking where PEAK looks
        }

        Follower.Update(Ownership, guest);
        UpdateCollision(character);
        InputBridge.Update(Ownership, guest);
        Hazards.Update(Ownership);
        Death.Update(linked, guest);
        UpdateHud();
        if (Ownership.OverlayAndBlocks)
        {
            Overlay.Update();
        }
        if (linked)
        {
            Blocks.Update(Ownership.OverlayAndBlocks, Follower.Feet, Camera.Yaw, guest.cameraMode == 0);
        }
        PublishHostState(character);

        if (linked && Time.unscaledTime - lastStateLog > 10f)
        {
            lastStateLog = Time.unscaledTime;
            Log.LogInfo($"state: {now}; {1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f):F0} fps; Minecraft flags 0x{guest.flags:X} feet ({guest.x:F2}, {guest.y:F2}, {guest.z:F2}) ack {guest.teleportAck}/{Ownership.TeleportSeq}");
        }
    }

    private void DrainEvents()
    {
        // Event types the plugin does not handle (hits on actors, explosions, ...) are dropped here.
        while (Link.PopEvent(out Proto.GuestEvent e))
        {
            if (e.type == Proto.EvPlayerDied)
            {
                Death.OnMinecraftDeath();
            }
        }
    }

    private void UpdateCollision(Character? character)
    {
        if (!linked || character == null || LoadingScreenHandler.loading)
        {
            return;
        }
        // PEAK swaps map segments in and out as the climb goes on; what was sent is stale then.
        MapHandler map = Zorro.Core.Singleton<MapHandler>.Instance;
        if (map != null)
        {
            Segment segment = map.GetCurrentSegment();
            if (segment != lastSegment)
            {
                lastSegment = segment;
                Collision.Reset($"segment {segment}");
                segmentResetAt = Time.unscaledTime + 6f; // again once the old segment has been switched off
            }
        }
        if (segmentResetAt > 0f && Time.unscaledTime > segmentResetAt)
        {
            segmentResetAt = -1f;
            Collision.Reset("segment settled");
        }

        // While PEAK owns the character or a teleport is pending, Minecraft's own position means
        // nothing yet: centre on where it is about to be put.
        if (Ownership.BodyFollows)
        {
            Collision.Update(guest.x, guest.y, guest.z);
        }
        else if (!character.data.dead)
        {
            Coords.ToMc(Follower.PeakFeet(character), out double x, out double y, out double z);
            Collision.Update(x, y, z);
        }
    }

    private void UpdateHud()
    {
        GUIManager gui = GUIManager.instance;
        if (gui == null || gui.hudCanvas == null)
        {
            hudHidden = false;
            return;
        }
        bool hide = !Ownership.PeakHud;
        if (hide != hudHidden || gui.hudCanvas.enabled == hide)
        {
            hudHidden = hide;
            gui.hudCanvas.enabled = !hide;
        }
    }

    private void PublishHostState(Character? character)
    {
        var host = new Proto.HostState
        {
            collisionEpoch = Collision.Epoch,
            teleportSeq = Ownership.TeleportSeq,
            viewportW = (uint)Screen.width,
            viewportH = (uint)Screen.height,
            gameHour = 12f,
            yaw = Camera.Yaw,
            pitch = Camera.Pitch,
            worldId = (uint)SceneManager.GetActiveScene().buildIndex + 1,
        };
        Owner state = Ownership.State;
        bool loading = character == null || LoadingScreenHandler.loading || state == Owner.Cutscene;
        if (character != null)
        {
            host.flags |= (uint)Proto.HostFlags.InGame;
            Vector3 feet = Ownership.BodyFollows ? Follower.Feet : Follower.PeakFeet(character);
            Coords.ToMc(feet, out host.posX, out host.posY, out host.posZ);
        }
        if (loading)
        {
            host.flags |= (uint)Proto.HostFlags.Loading; // Minecraft parks its player and teleports to us afterwards
        }
        if (state == Owner.PeakMenu)
        {
            host.flags |= (uint)Proto.HostFlags.MenuOpen; // Minecraft drops its held keys
        }
        // Minecraft paces its frames on this sequence number: write it every PEAK frame.
        Link.WriteHostState(host);
    }

    /// <summary>PEAK's own prompt is hidden with its HUD, so say what the interact key would do.</summary>
    private void DrawInteractPrompt()
    {
        if (Ownership.State != Owner.MinecraftOwns || InputBridge.ScreenOpen || Event.current.type != EventType.Repaint
            || Interaction.instance == null || Interaction.instance.bestInteractable == null)
        {
            return;
        }
        IInteractible target = Interaction.instance.bestInteractable;
        string text = $"[{InputBridge.InteractKeyName}] {target.GetInteractionText()}";
        promptStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        promptStyle.fontSize = Mathf.Max(16, Screen.height / 45);
        var rect = new Rect(0f, Screen.height * 0.56f, Screen.width, promptStyle.fontSize * 2f);
        promptStyle.normal.textColor = Color.black;
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, promptStyle);
        promptStyle.normal.textColor = Color.white;
        GUI.Label(rect, text, promptStyle);
    }

    private void OnGUI()
    {
        // PEAK's own windows (pause menu, boarding pass, end screen) are not covered by Minecraft's HUD.
        if (!Ownership.OverlayAndBlocks || Ownership.PeakMenuOpen())
        {
            return;
        }
        Overlay.Draw(InputBridge.ScreenOpen, InputBridge.Cursor);
        DrawInteractPrompt();
    }
}
