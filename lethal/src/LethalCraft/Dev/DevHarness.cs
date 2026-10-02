using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using GameNetcodeStuff;
using LethalCraft.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LethalCraft.Dev;

/// <summary>
/// Development only ([Dev] Harness = true): lets a script drive Lethal Company without a hand on
/// the mouse. Hosts a private lobby from the main menu and runs commands written, one per line,
/// to BepInEx/lethalcraft-dev.txt (screenshots land in BepInEx/lethalcraft-shots/).
/// </summary>
internal sealed class DevHarness
{
    private readonly Dictionary<string, Action<string[]>> commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly string commandFile = Path.Combine(Paths.BepInExRootPath, "lethalcraft-dev.txt");
    private readonly string shotDir = Path.Combine(Paths.BepInExRootPath, "lethalcraft-shots");
    private readonly bool autoHost;
    private float nextPoll;
    private bool startedHost;

    public DevHarness(bool autoHost)
    {
        this.autoHost = autoHost;
        Directory.CreateDirectory(shotDir);
        Register("shot", a => Shot(a.Length > 0 ? a[0] : "shot"));
        Register("probe", _ => Probe());
        Register("log", a => Plugin.Log.LogInfo("dev: " + string.Join(" ", a)));
        Register("quit", _ => Application.Quit());
        Register("host", _ => StartHost());
        Plugin.Log.LogInfo($"dev harness on: commands from {commandFile}");
    }

    public void Register(string name, Action<string[]> action) => commands[name] = action;

    public static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    public void Update()
    {
        if (autoHost)
        {
            AutoStartHost();
        }
        // F9 at the keyboard: what is the camera ray hitting? (for invisible walls and wrong floors)
        if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
        {
            ColliderProbe();
        }
        if (Time.unscaledTime < nextPoll)
        {
            return;
        }
        nextPoll = Time.unscaledTime + 0.1f;
        if (!File.Exists(commandFile))
        {
            return;
        }
        string[] lines;
        try
        {
            lines = File.ReadAllLines(commandFile);
            File.Delete(commandFile);
        }
        catch (IOException)
        {
            return; // still being written; next poll
        }
        foreach (string line in lines)
        {
            string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }
            if (!commands.TryGetValue(parts[0], out Action<string[]>? action))
            {
                Plugin.Log.LogWarning($"dev: unknown command '{parts[0]}'");
                continue;
            }
            try
            {
                action(parts.Skip(1).ToArray());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"dev: '{line}' failed: {e}");
            }
        }
    }

    private void AutoStartHost()
    {
        if (startedHost || SceneManager.GetActiveScene().name != "MainMenu" || Time.timeSinceLevelLoad < 4f)
        {
            return;
        }
        startedHost = true;
        StartHost();
    }

    /// <summary>Hosts a private lobby the way the menu's host screen does (MenuManager.ConfirmHostButton).</summary>
    private static void StartHost()
    {
        GameNetworkManager manager = GameNetworkManager.Instance;
        if (manager == null)
        {
            Plugin.Log.LogWarning("dev: no GameNetworkManager yet");
            return;
        }
        Plugin.Log.LogInfo("dev: hosting a private lobby from the main menu");
        manager.lobbyHostSettings = new HostSettings("LethalCraft", isPublic: false, "");
        manager.StartHost();
    }

    /// <summary>Every collider along the camera ray (10 units), nearest first, with layer and trigger flag.</summary>
    public static void ColliderProbe()
    {
        PlayerControllerB? player = Ownership.LocalPlayer;
        if (player == null)
        {
            return;
        }
        Transform cam = player.gameplayCamera.transform;
        RaycastHit[] hits = Physics.RaycastAll(cam.position, cam.forward, 10f, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        var sb = new StringBuilder($"colprobe: from {cam.position:F2} along {cam.forward:F2}, {hits.Length} hits");
        foreach (RaycastHit hit in hits)
        {
            Collider c = hit.collider;
            Rigidbody body = c.attachedRigidbody;
            sb.Append($"\n  {hit.distance:F2} '{c.name}' ({c.GetType().Name}) layer {c.gameObject.layer}={LayerMask.LayerToName(c.gameObject.layer)} tag {c.tag}{(c.isTrigger ? " TRIGGER" : "")}{(body != null ? (body.isKinematic ? " kinematicRB" : " dynamicRB") : "")} path {HierarchyPath(c.transform)}");
        }
        Plugin.Log.LogInfo(sb.ToString());
    }

    private static string HierarchyPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    private void Shot(string name)
    {
        string path = Path.Combine(shotDir, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        Plugin.Log.LogInfo($"dev: screenshot -> {path}");
    }

    /// <summary>Logs the runtime facts docs/LC-NOTES.md needs: bindings, layers, shaders, sizes, world bounds.</summary>
    private static void Probe()
    {
        var log = Plugin.Log;
        Scene scene = SceneManager.GetActiveScene();
        var scenes = new List<string>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            scenes.Add(SceneManager.GetSceneAt(i).name);
        }
        log.LogInfo($"probe: active scene '{scene.name}' (loaded: {string.Join(", ", scenes)}), screen {Screen.width}x{Screen.height}, graphics {SystemInfo.graphicsDeviceType}");

        var sb = new StringBuilder("probe: layers");
        for (int i = 0; i < 32; i++)
        {
            string name = LayerMask.LayerToName(i);
            if (name.Length > 0)
            {
                sb.Append(' ').Append(i).Append('=').Append(name);
            }
        }
        log.LogInfo(sb.ToString());

        sb = new StringBuilder("probe: bindings");
        if (InputSystem.actions != null)
        {
            foreach (InputAction action in InputSystem.actions)
            {
                sb.Append("\n  ").Append(action.actionMap?.name).Append('/').Append(action.name).Append(": ")
                    .Append(string.Join(", ", action.bindings.Where(b => !b.isComposite).Select(b => b.effectivePath)));
            }
        }
        log.LogInfo(sb.ToString());

        PlayerControllerB? player = Ownership.LocalPlayer;
        StartOfRound round = StartOfRound.Instance;
        if (player != null && round != null)
        {
            CharacterController controller = player.thisController;
            Physics.Raycast(player.transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 10f, round.walkableSurfacesMask, QueryTriggerInteraction.Ignore);
            log.LogInfo($"probe: scout feet {player.transform.position:F3} controller center {controller.center:F3} height {controller.height:F3} radius {controller.radius:F3} camera {player.gameplayCamera.transform.position:F3} (local {player.gameplayCamera.transform.localPosition:F3}, container local {player.cameraContainerTransform.localPosition:F3}) fov {player.gameplayCamera.fieldOfView:F1} ground y {ground.point.y:F3} ({(ground.collider != null ? ground.collider.name : "none")})");
            log.LogInfo($"probe: yaw {player.transform.eulerAngles.y:F1} cameraUp {player.cameraUp:F1} grounded {controller.isGrounded} inShipPhase {round.inShipPhase} landed {round.shipHasLanded} leaving {round.shipIsLeaving} level {round.currentLevelID} '{round.currentLevel?.PlanetName}' elevator {round.elevatorTransform.position:F2} inElevator {player.isInElevator} inShipRoom {player.isInHangarShipRoom} inFactory {player.isInsideFactory}");
            log.LogInfo($"probe: masks walkable 0x{round.walkableSurfacesMask:X} collidersAndRoom 0x{round.collidersAndRoomMask:X} allPlayersCollideWith 0x{round.allPlayersCollideWithMask:X} interactable 0x{player.interactableObjectsMask:X}");
        }

        var bounds = new Bounds();
        bool any = false;
        int count = 0, meshColliders = 0, readable = 0, terrains = 0;
        var layers = new Dictionary<string, int>();
        foreach (Collider c in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            string layer = LayerMask.LayerToName(c.gameObject.layer);
            layers[layer] = layers.TryGetValue(layer, out int n) ? n + 1 : 1;
            if (c.isTrigger || !c.enabled)
            {
                continue;
            }
            count++;
            if (c is MeshCollider mc && mc.sharedMesh != null)
            {
                meshColliders++;
                if (mc.sharedMesh.isReadable)
                {
                    readable++;
                }
            }
            if (c is TerrainCollider)
            {
                terrains++;
            }
            if (!any)
            {
                bounds = c.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(c.bounds);
            }
        }
        log.LogInfo($"probe: {count} solid colliders, world bounds min {bounds.min:F1} max {bounds.max:F1}; {meshColliders} mesh colliders ({readable} readable), {terrains} terrain colliders");
        log.LogInfo("probe: colliders per layer: " + string.Join(", ", layers.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}")));

        var shaderUse = new Dictionary<string, int>();
        foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            foreach (Material m in r.sharedMaterials)
            {
                if (m != null && m.shader != null)
                {
                    shaderUse[m.shader.name] = shaderUse.TryGetValue(m.shader.name, out int n) ? n + 1 : 1;
                }
            }
        }
        log.LogInfo("probe: shaders on scene renderers: " + string.Join(", ", shaderUse.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}")));

        sb = new StringBuilder("probe: loaded shaders");
        foreach (Shader s in Resources.FindObjectsOfTypeAll<Shader>().OrderBy(s => s.name))
        {
            var props = new List<string>();
            for (int i = 0; i < s.GetPropertyCount(); i++)
            {
                props.Add(s.GetPropertyName(i));
            }
            bool interesting = props.Any(p => p is "_AlphaCutoff" or "_AlphaCutoffEnable" or "_SurfaceType" or "_BaseColorMap" or "_UnlitColorMap" or "_MainTex" or "_BaseMap" or "_Color");
            sb.Append("\n  ").Append(s.name).Append(s.isSupported ? "" : " (unsupported)");
            if (interesting)
            {
                sb.Append(" [").Append(string.Join(" ", props.Take(40))).Append(']');
            }
        }
        log.LogInfo(sb.ToString());

        var litKinds = new Dictionary<string, int>();
        foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (m == null || m.shader == null || !m.shader.name.StartsWith("HDRP/", StringComparison.Ordinal))
            {
                continue;
            }
            string kind = $"{m.shader.name} surface={(m.HasProperty("_SurfaceType") ? m.GetFloat("_SurfaceType") : -1)} clip={(m.HasProperty("_AlphaCutoffEnable") ? m.GetFloat("_AlphaCutoffEnable") : -1)} doubleSided={(m.HasProperty("_DoubleSidedEnable") ? m.GetFloat("_DoubleSidedEnable") : -1)}";
            litKinds[kind] = litKinds.TryGetValue(kind, out int n) ? n + 1 : 1;
        }
        log.LogInfo("probe: HDRP material variants loaded: " + string.Join("; ", litKinds.OrderByDescending(p => p.Value).Select(p => $"{p.Key} x{p.Value}")));

        if (HUDManager.Instance != null && HUDManager.Instance.playerScreenTexture != null)
        {
            Transform t = HUDManager.Instance.playerScreenTexture.transform;
            sb = new StringBuilder("probe: player screen RawImage path");
            while (t != null)
            {
                sb.Insert(29, " / " + t.name + $"[{t.GetSiblingIndex()}]");
                t = t.parent;
            }
            log.LogInfo(sb.ToString());
            Canvas canvas = HUDManager.Instance.playerScreenTexture.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                log.LogInfo($"probe: HUD canvas '{canvas.name}' mode {canvas.renderMode} sort {canvas.sortingOrder}; children: {string.Join(", ", Enumerable.Range(0, canvas.transform.childCount).Select(i => canvas.transform.GetChild(i).name))}");
            }
        }
    }
}
