using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PeakCraft.Dev;

/// <summary>
/// Development only ([Dev] Harness = true): lets a script drive PEAK without a hand on the mouse.
/// Starts an offline airport session from the title screen and runs commands written, one per
/// line, to BepInEx/peakcraft-dev.txt (screenshots land in BepInEx/peakcraft-shots/).
/// </summary>
internal sealed class DevHarness
{
    private readonly Dictionary<string, Action<string[]>> commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly string commandFile = Path.Combine(Paths.BepInExRootPath, "peakcraft-dev.txt");
    private readonly string shotDir = Path.Combine(Paths.BepInExRootPath, "peakcraft-shots");
    private float nextPoll;
    private bool startedOffline;

    public DevHarness()
    {
        Directory.CreateDirectory(shotDir);
        Register("shot", a => Shot(a.Length > 0 ? a[0] : "shot"));
        Register("probe", _ => Probe());
        Register("log", a => Plugin.Log.LogInfo("dev: " + string.Join(" ", a)));
        Register("quit", _ => Application.Quit());
        Plugin.Log.LogInfo($"dev harness on: commands from {commandFile}");
    }

    public void Register(string name, Action<string[]> action) => commands[name] = action;

    public static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    public void Update()
    {
        AutoStartOffline();
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

    private void AutoStartOffline()
    {
        if (startedOffline || LoadingScreenHandler.loading || SceneManager.GetActiveScene().name != "Title")
        {
            return;
        }
        MainMenu menu = UnityEngine.Object.FindAnyObjectByType<MainMenu>();
        if (menu == null || Time.timeSinceLevelLoad < 3f)
        {
            return;
        }
        startedOffline = true;
        Plugin.Log.LogInfo("dev: starting an offline session from the title screen");
        menu.PlaySoloClicked();
    }

    private void Shot(string name)
    {
        string path = Path.Combine(shotDir, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        Plugin.Log.LogInfo($"dev: screenshot -> {path}");
    }

    /// <summary>Logs the runtime facts docs/PEAK-NOTES.md needs: bindings, shaders, sizes, world bounds.</summary>
    private static void Probe()
    {
        var log = Plugin.Log;
        Scene scene = SceneManager.GetActiveScene();
        log.LogInfo($"probe: scene '{scene.name}', screen {Screen.width}x{Screen.height}, graphics {SystemInfo.graphicsDeviceType}");

        var sb = new StringBuilder("probe: bindings");
        foreach (InputAction action in InputSystem.actions)
        {
            sb.Append("\n  ").Append(action.actionMap?.name).Append('/').Append(action.name).Append(": ")
                .Append(string.Join(", ", action.bindings.Where(b => !b.isComposite).Select(b => b.effectivePath)));
        }
        log.LogInfo(sb.ToString());

        Character character = Character.localCharacter;
        if (character != null)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Collider c in character.refs.ragdoll.colliderList)
            {
                minY = Mathf.Min(minY, c.bounds.min.y);
                maxY = Mathf.Max(maxY, c.bounds.max.y);
            }
            Physics.Raycast(character.Center, Vector3.down, out RaycastHit ground, 10f, LayerMask.GetMask("Terrain", "Map", "Default"), QueryTriggerInteraction.Ignore);
            log.LogInfo($"probe: character center {character.Center:F3} head {character.Head:F3} hip {character.HipPos():F3} collider y {minY:F3}..{maxY:F3} (height {maxY - minY:F3}) ground y {ground.point.y:F3} camera {MainCamera.instance.transform.position:F3} fov {MainCamera.instance.cam.fieldOfView:F1}");
            log.LogInfo($"probe: lookValues {character.data.lookValues} lookDirection {character.data.lookDirection:F3} grounded {character.data.isGrounded}");
        }

        int mask = LayerMask.GetMask("Terrain", "Map", "Default");
        var bounds = new Bounds();
        bool any = false;
        int count = 0, meshColliders = 0, readable = 0, terrains = 0;
        var layers = new Dictionary<string, int>();
        foreach (Collider c in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            string layer = LayerMask.LayerToName(c.gameObject.layer);
            layers[layer] = layers.TryGetValue(layer, out int n) ? n + 1 : 1;
            if (c.isTrigger || !c.enabled || (mask & (1 << c.gameObject.layer)) == 0)
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
        log.LogInfo($"probe: {count} solid colliders on Terrain/Map/Default, world bounds min {bounds.min:F1} max {bounds.max:F1}; {meshColliders} mesh colliders ({readable} readable), {terrains} terrain colliders");
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
            bool interesting = props.Any(p => p is "_Cutoff" or "_AlphaClip" or "_SrcBlend" or "_BaseMap" or "_MainTex");
            sb.Append("\n  ").Append(s.name).Append(s.isSupported ? "" : " (unsupported)");
            if (interesting)
            {
                sb.Append(" [").Append(string.Join(" ", props.Take(40))).Append(']');
            }
        }
        log.LogInfo(sb.ToString());
    }
}
