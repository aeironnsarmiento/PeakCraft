using System.Collections.Generic;
using LethalCraft.Link;
using LethalCraft.World;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LethalCraft.Render;

/// <summary>
/// Minecraft's light-emitting blocks (kRenLights: torches, lanterns, glowstone, lava, ...) as HDRP
/// point lights in Lethal Company's dark world. One light per block, kept per section so a
/// section's new list replaces its old one; only the nearest few dozen are switched on, since
/// HDRP has a per-frame light budget and a torch wall would blow it.
/// </summary>
internal sealed unsafe class BlockLights
{
    private const int MaxLit = 40;

    private readonly Dictionary<(int, int, int), List<GameObject>> sections = new();
    private readonly List<(float distance, GameObject light)> scratch = new();
    private GameObject? root;
    private float nextCull;

    public int Count { get; private set; }

    /// <summary>kRenLights: a section's lights, replacing what it had.</summary>
    public void SetSection(int sx, int sy, int sz, uint count, Proto.RenLight* lights)
    {
        var key = (sx, sy, sz);
        if (sections.TryGetValue(key, out List<GameObject>? old))
        {
            foreach (GameObject light in old)
            {
                Object.Destroy(light);
            }
            Count -= old.Count;
            sections.Remove(key);
        }
        if (count == 0)
        {
            return;
        }
        if (root == null)
        {
            root = new GameObject("LethalCraft lights") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(root);
        }
        var list = new List<GameObject>((int)count);
        for (uint i = 0; i < count; i++)
        {
            Proto.RenLight l = lights[i];
            int level = Mathf.Clamp(l.level, 1, 15);
            var obj = new GameObject($"light {sx * 16 + l.x} {sy * 16 + l.y} {sz * 16 + l.z}");
            obj.transform.SetParent(root.transform, worldPositionStays: false);
            obj.transform.position = Coords.ToUnity(sx * 16 + l.x + 0.5, sy * 16 + l.y + 0.5, sz * 16 + l.z + 0.5);
            Light light = obj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color((l.color & 0xFF) / 255f, ((l.color >> 8) & 0xFF) / 255f, ((l.color >> 16) & 0xFF) / 255f);
            light.range = level * 0.9f * Coords.Scale;
            light.shadows = LightShadows.None;
            HDAdditionalLightData hd = obj.AddComponent<HDAdditionalLightData>();
            hd.affectsVolumetric = false;
            hd.SetIntensity(level * 12f, LightUnit.Candela);
            uint kind = (l.color >> 24) & 0xF;
            if (kind == 1)
            {
                obj.AddComponent<Flicker>();
            }
            obj.SetActive(false);
            list.Add(obj);
        }
        sections[key] = list;
        Count += list.Count;
    }

    /// <summary>Switches on the lights nearest the player; the rest stay off.</summary>
    public void Update(Vector3 feet, bool visible)
    {
        if (root == null)
        {
            return;
        }
        if (root.activeSelf != visible)
        {
            root.SetActive(visible);
        }
        if (!visible || Time.unscaledTime < nextCull)
        {
            return;
        }
        nextCull = Time.unscaledTime + 0.5f;
        scratch.Clear();
        foreach (List<GameObject> list in sections.Values)
        {
            foreach (GameObject light in list)
            {
                scratch.Add(((light.transform.position - feet).sqrMagnitude, light));
            }
        }
        scratch.Sort((a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < scratch.Count; i++)
        {
            bool on = i < MaxLit;
            if (scratch[i].light.activeSelf != on)
            {
                scratch[i].light.SetActive(on);
            }
        }
    }

    public void Reposition()
    {
        foreach (KeyValuePair<(int, int, int), List<GameObject>> pair in sections)
        {
            (int sx, int sy, int sz) = pair.Key;
            foreach (GameObject light in pair.Value)
            {
                string[] parts = light.name.Split(' ');
                light.transform.position = Coords.ToUnity(int.Parse(parts[1]) + 0.5, int.Parse(parts[2]) + 0.5, int.Parse(parts[3]) + 0.5);
            }
            _ = (sx, sy, sz);
        }
    }

    public void Clear()
    {
        foreach (List<GameObject> list in sections.Values)
        {
            foreach (GameObject light in list)
            {
                Object.Destroy(light);
            }
        }
        sections.Clear();
        Count = 0;
    }

    /// <summary>A torch's flicker: a little noise on the intensity.</summary>
    private sealed class Flicker : MonoBehaviour
    {
        private HDAdditionalLightData? hd;
        private float baseIntensity;
        private float seed;

        private void Start()
        {
            hd = GetComponent<HDAdditionalLightData>();
            baseIntensity = hd != null ? hd.intensity : 0f;
            seed = Random.value * 100f;
        }

        private void Update()
        {
            if (hd != null)
            {
                hd.intensity = baseIntensity * (0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 7f + seed, seed));
            }
        }
    }
}
