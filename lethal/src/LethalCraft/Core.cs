using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using LethalCraft.Dev;
using LethalCraft.Hazards;
using LethalCraft.Input;
using LethalCraft.Link;
using LethalCraft.Patches;
using LethalCraft.Player;
using LethalCraft.Render;
using LethalCraft.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LethalCraft;

/// <summary>
/// LethalCraft: the Lethal Company half of SkyCraft's two-game link. Modules, config and per-frame order.
///
/// Each frame: read Minecraft's state, pick the world slot, decide who owns the scout, place the
/// follower, export collision around the new position, pump input, publish the host state. After
/// the scout's own LateUpdate the camera is overwritten (PlayerControllerBPatch), and the overlay
/// is drawn last (OnGUI).
/// </summary>
/// <remarks>Plain C# (not a Unity object): patches can test it for null without Unity's lifetime rules.</remarks>
internal sealed class Core
{
    internal static Core? Instance { get; private set; }
    private static ManualLogSource Log => Plugin.Log;
    private readonly ConfigFile Config;

    internal HostLink Link { get; private set; } = null!;
    internal Ownership Ownership { get; } = new();
    internal Follower Follower { get; private set; } = null!;
    internal CameraDriver Camera { get; } = new();
    internal Interact Interact { get; } = new();
    internal Bindings Bindings { get; } = new();
    internal InputBridge InputBridge { get; private set; } = null!;
    internal CollisionExporter Collision { get; private set; } = null!;
    internal Overlay Overlay { get; private set; } = null!;
    internal RenderRing Blocks { get; private set; } = null!;
    internal DamageBridge Hazards { get; private set; } = null!;
    internal Death Death { get; private set; } = null!;
    internal DevHarness? Dev { get; private set; }

    /// <summary>Minecraft's state as read this frame.</summary>
    internal Proto.GuestState Guest => guest;

    private readonly ConfigEntry<string> linkName;
    private readonly ConfigEntry<float> scale;
    private readonly ConfigEntry<float> yOffset;
    private readonly ConfigEntry<bool> devHarness;
    private readonly ConfigEntry<bool> devAutoHost;
    private readonly string harmonyId;
    private Harmony? harmony;
    private bool linked;
    private uint guestPid;
    private Proto.GuestState guest;
    private float lastStateLog;
    private bool hudHidden;
    private GUIStyle? promptStyle;

    public bool Enabled { get; private set; }

    public Core(ConfigFile config, string harmonyId)
    {
        Config = config;
        this.harmonyId = harmonyId;
        Instance = this;
        linkName = Config.Bind("Link", "MappingName", Proto.MappingName,
            "Name of the shared-memory mapping. Must match Minecraft's -Dskycraft.link (default Local\\SkyCraft_v1).");
        scale = Config.Bind("World", "UnitsPerBlock", 1.4f,
            "Lethal Company units per Minecraft block. The scout is 2.5 units tall with its camera 2.36 units up; Minecraft's player is 1.8 blocks with eyes at 1.62, so 1.4 lines the eyes up.");
        yOffset = Config.Bind("World", "YOffset", 0f,
            "Added to Lethal Company's height (in blocks) to get Minecraft's. Use a multiple of 8.");
        devHarness = Config.Bind("Dev", "Harness", false,
            "Development only: run commands from BepInEx/lethalcraft-dev.txt.");
        devAutoHost = Config.Bind("Dev", "AutoHost", false,
            "Development only: host a private lobby as soon as the main menu is up.");
        Coords.Scale = Mathf.Clamp(scale.Value, 0.25f, 4f);
        Coords.YOffset = yOffset.Value;

        bool layoutOk = Proto.SelfCheck(Log);
        bool ringsOk = Rings.SelfTest(Log);
        Link = new HostLink(Log);
        if (!layoutOk || !ringsOk || !Link.Create(linkName.Value) || !ApplyPatches())
        {
            Log.LogError("LethalCraft is disabled for this session: Lethal Company runs unmodified.");
            Instance = null;
            return;
        }
        Enabled = true;
        Follower = new Follower(Link);
        InputBridge = new InputBridge(Link, Camera, Interact, Bindings, Config);
        Collision = new CollisionExporter(Link);
        Overlay = new Overlay(Link);
        Blocks = new RenderRing(Link, Config);
        Hazards = new DamageBridge(Link, Config);
        Death = new Death(Link);
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        if (devHarness.Value)
        {
            Dev = new DevHarness(devAutoHost.Value);
            DevCommands.Register(this, Dev);
        }
    }

    /// <summary>One patch class per game class. A patch that fails to apply disables the plugin rather than half-running.</summary>
    private bool ApplyPatches()
    {
        harmony = new Harmony(harmonyId);
        Type[] patches = { typeof(PlayerControllerBPatch) };
        foreach (Type patch in patches)
        {
            try
            {
                harmony.CreateClassProcessor(patch).Patch();
                Log.LogInfo($"patch ok   {patch.Name}");
            }
            catch (Exception e)
            {
                Log.LogError($"patch FAIL {patch.Name}: {e}");
                harmony.UnpatchSelf();
                return false;
            }
        }
        return true;
    }

    public void Shutdown()
    {
        if (Instance != this)
        {
            return;
        }
        Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        Follower?.Release();
        InputBridge?.Restore();
        harmony?.UnpatchSelf();
        Link?.Close();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Collision?.Reset($"scene {scene.name} loaded");
    }

    private void OnSceneUnloaded(Scene scene)
    {
        Collision?.Reset($"scene {scene.name} unloaded");
    }

    public void Update()
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
            Log.LogInfo("link down: Minecraft's heartbeat stopped; Lethal Company has the scout");
            Overlay.Clear();
            Blocks.Reset();
        }
        linked = nowLinked;
        if (linked && Link.ReadGuestState(out Proto.GuestState state))
        {
            guest = state; // on a torn read keep last frame's
        }
        DrainEvents();
        PlayerControllerB? player = Ownership.LocalPlayer;
        UpdateWorldSlot();
        Ownership.Update(linked, guest);
        if (player != null && Ownership.HostCamera)
        {
            Camera.SetLookFromHost(player); // Minecraft starts out looking where the scout looks
        }

        Follower.Update(Ownership, guest);
        UpdateCollision(player);
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
        PublishHostState(player);

        if (linked && Time.unscaledTime - lastStateLog > 10f)
        {
            lastStateLog = Time.unscaledTime;
            Log.LogInfo($"state: {Ownership.State}; {1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f):F0} fps; Minecraft flags 0x{guest.flags:X} feet ({guest.x:F2}, {guest.y:F2}, {guest.z:F2}) ack {guest.teleportAck}/{Ownership.TeleportSeq}; slot {WorldSlots.Current}");
        }
    }

    private void DrainEvents()
    {
        // Event types the plugin does not handle yet (hits on actors, explosions, ...) are dropped here.
        while (Link.PopEvent(out Proto.GuestEvent e))
        {
            if (e.type == Proto.EvPlayerDied)
            {
                Death.OnMinecraftDeath();
            }
        }
    }

    /// <summary>The ship in orbit and every moon live in their own part of the mirror world.</summary>
    private void UpdateWorldSlot()
    {
        if (WorldSlots.Apply(WorldSlots.Desired()))
        {
            Collision.Reset($"world slot {WorldSlots.Current}");
            Blocks.Reposition();
            if (Ownership.BodyFollows)
            {
                Ownership.RequestTeleport();
            }
        }
    }

    private void UpdateCollision(PlayerControllerB? player)
    {
        if (!linked || player == null || WorldSlots.Current < 0)
        {
            return;
        }
        // While the host owns the scout or a teleport is pending, Minecraft's own position means
        // nothing yet: centre on where it is about to be put.
        if (Ownership.BodyFollows && !Ownership.HostTeleportPending)
        {
            Collision.Update(guest.x, guest.y, guest.z);
        }
        else if (!player.isPlayerDead)
        {
            Coords.ToMc(Follower.HostFeet(player), out double x, out double y, out double z);
            Collision.Update(x, y, z);
        }
    }

    /// <summary>The scout's inventory and health HUD make no sense while Minecraft's are up; the rest (clock, tips, scan) stays.</summary>
    private void UpdateHud()
    {
        HUDManager hud = HUDManager.Instance;
        if (hud == null)
        {
            hudHidden = false;
            return;
        }
        bool hide = !Ownership.HostHud;
        if (hide)
        {
            hud.Inventory.canvasGroup.alpha = 0f;
            hud.PlayerInfo.canvasGroup.alpha = 0f;
        }
        else if (hudHidden)
        {
            hud.PingHUDElement(hud.Inventory, 0f, 1f, 0.13f);
            hud.PingHUDElement(hud.PlayerInfo, 0f, 1f, 0.13f);
        }
        hudHidden = hide;
    }

    private void PublishHostState(PlayerControllerB? player)
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
            worldId = WorldSlots.WorldId,
        };
        Owner state = Ownership.State;
        bool loading = player == null || state == Owner.Cutscene || WorldSlots.Current < 0;
        if (player != null)
        {
            host.flags |= (uint)Proto.HostFlags.InGame;
            Vector3 feet = Ownership.BodyFollows && !Ownership.HostTeleportPending ? Follower.Feet : Follower.HostFeet(player);
            Coords.ToMc(feet, out host.posX, out host.posY, out host.posZ);
        }
        if (loading)
        {
            host.flags |= (uint)Proto.HostFlags.Loading; // Minecraft parks its player and teleports to us afterwards
        }
        if (state == Owner.HostMenu)
        {
            host.flags |= (uint)Proto.HostFlags.MenuOpen; // Minecraft drops its held keys
        }
        // Minecraft paces its frames on this sequence number: write it every frame.
        Link.WriteHostState(host);
    }

    /// <summary>The scout's own hover tip stays visible, so only say which key interacts now.</summary>
    private void DrawInteractPrompt()
    {
        PlayerControllerB? player = Ownership.LocalPlayer;
        if (Ownership.State != Owner.GuestOwns || InputBridge.ScreenOpen || Event.current.type != EventType.Repaint
            || player == null || player.hoveringOverTrigger == null || string.IsNullOrEmpty(player.hoveringOverTrigger.hoverTip))
        {
            return;
        }
        string text = $"[{InputBridge.InteractKeyName}] {player.hoveringOverTrigger.hoverTip.Replace("[E]", "").Replace("[LMB]", "").Trim(' ', ':')}";
        promptStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        promptStyle.fontSize = Mathf.Max(16, Screen.height / 45);
        var rect = new Rect(0f, Screen.height * 0.58f, Screen.width, promptStyle.fontSize * 2f);
        promptStyle.normal.textColor = Color.black;
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, promptStyle);
        promptStyle.normal.textColor = Color.white;
        GUI.Label(rect, text, promptStyle);
    }

    public void OnGUI()
    {
        // The host's own windows (pause menu, terminal) are not covered by Minecraft's HUD.
        if (!Ownership.OverlayAndBlocks || Ownership.State == Owner.HostMenu)
        {
            return;
        }
        Overlay.Draw(InputBridge.ScreenOpen, InputBridge.Cursor);
        DrawInteractPrompt();
    }
}
