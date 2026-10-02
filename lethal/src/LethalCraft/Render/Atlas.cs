using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LethalCraft.Render;

/// <summary>How a face is drawn, decided per triangle from its texture's alpha (see <see cref="Atlas.Classify"/>).</summary>
internal enum Surface
{
    /// <summary>Every texel opaque: the host's opaque pass, depth prepass and all.</summary>
    Solid,

    /// <summary>Holes in it (leaves, a helmet's face opening, an arrow's sprite): alpha-clipped, writing depth.</summary>
    Cutout,

    /// <summary>Minecraft's translucent layer (stained glass, water): blended, no depth write.</summary>
    Translucent,

    /// <summary>Nothing to see (a skin's unused hat layer): not drawn at all.</summary>
    Empty,
}

/// <summary>
/// Minecraft's textures and the materials that draw with them. BepInEx cannot compile shaders, so
/// the material is borrowed from the host: Lethal Company is HDRP, and HDRP/Lit ships with it.
/// Shader variants the game never uses are stripped from its build, so rather than configuring
/// a fresh HDRP/Lit material (whose keyword set may not exist), the plugin clones materials the
/// game already has loaded: an opaque one, an alpha-clipped one and a transparent one, and
/// swaps their textures for Minecraft's atlas.
///
/// HDRP/Lit does not read vertex colour, so Minecraft's per-vertex tint (grass, leaves, water)
/// is applied per material instead: <see cref="SupportsVertexColor"/> is false and the mesh
/// builders group faces by tint (see <see cref="TintKey"/>). Per-vertex ambient occlusion is lost;
/// the host's own lighting stands in for it.
/// </summary>
internal sealed unsafe class Atlas
{
    private static readonly int BaseColorMap = Shader.PropertyToID("_BaseColorMap");
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int NormalMap = Shader.PropertyToID("_NormalMap");
    private static readonly int MaskMap = Shader.PropertyToID("_MaskMap");
    private static readonly int EmissiveColorMap = Shader.PropertyToID("_EmissiveColorMap");
    private static readonly int EmissiveColor = Shader.PropertyToID("_EmissiveColor");
    private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
    private static readonly int Metallic = Shader.PropertyToID("_Metallic");
    private static readonly int CullMode = Shader.PropertyToID("_CullMode");
    private static readonly int CullModeForward = Shader.PropertyToID("_CullModeForward");
    private static readonly int DoubleSidedEnable = Shader.PropertyToID("_DoubleSidedEnable");
    private static readonly int SurfaceType = Shader.PropertyToID("_SurfaceType");
    private static readonly int AlphaCutoffEnable = Shader.PropertyToID("_AlphaCutoffEnable");
    private static readonly int BlendMode = Shader.PropertyToID("_BlendMode");
    private static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
    private bool loggedMaterial;

    private readonly Material?[] templates = new Material?[3]; // by Surface
    private readonly float brightness;
    private readonly Dictionary<uint, Texture2D> textures = new(); // 0 = the block atlas
    private readonly Dictionary<uint, (int width, int height, byte[] alpha)> alphas = new();
    private readonly Dictionary<(uint texture, Surface surface, bool twoSided, uint tint), Material> materials = new();
    private readonly Dictionary<(int, int), Texture2D> stamps = new();
    private bool searched;

    public Atlas(ConfigFile config)
    {
        brightness = config.Bind("Render", "BlockBrightness", 1f,
            "Scales the colour of Minecraft's blocks under Lethal Company's lights.").Value;
    }

    /// <summary>False: the builders must bake the vertex tint into per-material colours.</summary>
    public bool SupportsVertexColor => false;

    public bool Usable
    {
        get
        {
            FindTemplates();
            return templates[(int)Surface.Solid] != null;
        }
    }

    /// <summary>A quantised tint from a vertex colour, ignoring its brightness (ambient occlusion rides on that).</summary>
    public static uint TintKey(uint rgba)
    {
        int r = (int)(rgba & 0xFF), g = (int)((rgba >> 8) & 0xFF), b = (int)((rgba >> 16) & 0xFF);
        int max = Math.Max(r, Math.Max(g, b));
        if (max == 0)
        {
            return 0xFFFFFF;
        }
        // Normalise so the brightest channel is 255, then keep 4 bits per channel: a few tints per section.
        uint nr = (uint)(r * 255 / max) >> 4, ng = (uint)(g * 255 / max) >> 4, nb = (uint)(b * 255 / max) >> 4;
        return (nr << 20) | (nr << 16) | (ng << 12) | (ng << 8) | (nb << 4) | nb;
    }

    /// <summary>Finds the game's own HDRP/Lit materials to clone: opaque, alpha-clipped, transparent.</summary>
    private void FindTemplates()
    {
        if (searched)
        {
            return;
        }
        Material? solid = null, cutout = null, translucent = null;
        foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (m == null || m.shader == null || m.shader.name != "HDRP/Lit" || !m.HasProperty(SurfaceType) || !m.HasProperty(AlphaCutoffEnable))
            {
                continue;
            }
            bool transparent = m.GetFloat(SurfaceType) > 0.5f;
            bool clipped = m.GetFloat(AlphaCutoffEnable) > 0.5f;
            if (transparent)
            {
                translucent ??= m;
            }
            else if (clipped)
            {
                cutout ??= m;
            }
            else
            {
                solid ??= m;
            }
            if (solid != null && cutout != null && translucent != null)
            {
                break;
            }
        }
        if (solid == null)
        {
            Shader? lit = Shader.Find("HDRP/Lit");
            if (lit != null)
            {
                solid = new Material(lit) { hideFlags = HideFlags.HideAndDontSave };
            }
        }
        searched = solid != null;
        templates[(int)Surface.Solid] = solid;
        templates[(int)Surface.Cutout] = cutout ?? solid;
        templates[(int)Surface.Translucent] = translucent ?? cutout ?? solid;
        Plugin.Log.LogInfo(solid == null
            ? "blocks: no HDRP/Lit material loaded yet; Minecraft's blocks wait"
            : $"blocks: material templates: solid '{solid.name}', cutout '{(cutout != null ? cutout.name : "none (falls back to opaque)")}', translucent '{(translucent != null ? translucent.name : "none")}'");
    }

    /// <summary>kRenAtlas / kRenTexture: a whole texture, RGBA8, top row first.</summary>
    public void SetTexture(uint id, int width, int height, byte* pixels)
    {
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
        {
            return;
        }
        if (!textures.TryGetValue(id, out Texture2D? texture) || texture.width != width || texture.height != height)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
            texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            textures[id] = texture;
            foreach (KeyValuePair<(uint texture, Surface surface, bool twoSided, uint tint), Material> pair in materials)
            {
                if (pair.Key.texture == id)
                {
                    pair.Value.SetTexture(BaseColorMap, texture);
                    pair.Value.mainTexture = texture;
                }
            }
            Plugin.Log.LogInfo(id == 0 ? $"blocks: atlas {width}x{height}" : $"blocks: texture {id} {width}x{height}");
        }
        var alpha = new byte[width * height];
        for (int i = 0; i < alpha.Length; i++)
        {
            alpha[i] = pixels[i * 4 + 3];
        }
        alphas[id] = (width, height, alpha);
        // Loaded as sent: Unity's row 0 is then Minecraft's top row, so Minecraft's v is used unchanged.
        texture.LoadRawTextureData((IntPtr)pixels, width * height * 4);
        texture.Apply(updateMipmaps: false);
    }

    /// <summary>kRenAtlasRegion: an animated sprite's current frame.</summary>
    public void SetRegion(int x, int y, int width, int height, byte* pixels)
    {
        if (!textures.TryGetValue(0, out Texture2D? texture) || width <= 0 || height <= 0 || x < 0 || y < 0
            || x + width > texture.width || y + height > texture.height)
        {
            return;
        }
        // A small texture per sprite size, copied into the atlas on the GPU: re-uploading the whole
        // atlas for every animated sprite every frame would move hundreds of megabytes.
        if (!stamps.TryGetValue((width, height), out Texture2D? stamp))
        {
            stamp = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: false) { hideFlags = HideFlags.HideAndDontSave };
            stamps[(width, height)] = stamp;
        }
        stamp.LoadRawTextureData((IntPtr)pixels, width * height * 4);
        stamp.Apply(updateMipmaps: false);
        Graphics.CopyTexture(stamp, 0, 0, 0, 0, width, height, texture, 0, 0, x, y);
    }

    /// <summary>
    /// Looks at the texels a triangle covers (a grid of points across it, pulled slightly inwards so
    /// neighbouring sprites don't bleed in): all opaque is <see cref="Surface.Solid"/>, none is
    /// <see cref="Surface.Empty"/>, a mix is <see cref="Surface.Cutout"/>.
    /// </summary>
    public Surface Classify(uint id, float u0, float v0, float u1, float v1, float u2, float v2)
    {
        if (!alphas.TryGetValue(id, out (int width, int height, byte[] alpha) map))
        {
            return Surface.Solid;
        }
        const int steps = 5;
        bool anyOpaque = false, anyClear = false;
        for (int i = 0; i <= steps; i++)
        {
            for (int j = 0; j <= steps - i; j++)
            {
                // Barycentric weights, shrunk towards the middle by a twentieth.
                float a = (i + 0.05f * steps) / (steps * 1.15f), b = (j + 0.05f * steps) / (steps * 1.15f), c = 1f - a - b;
                int x = Mathf.Clamp((int)((u0 * a + u1 * b + u2 * c) * map.width), 0, map.width - 1);
                int y = Mathf.Clamp((int)((v0 * a + v1 * b + v2 * c) * map.height), 0, map.height - 1);
                if (map.alpha[y * map.width + x] < 26)
                {
                    anyClear = true;
                }
                else
                {
                    anyOpaque = true;
                }
            }
        }
        return !anyOpaque ? Surface.Empty : anyClear ? Surface.Cutout : Surface.Solid;
    }

    /// <summary>The material for a texture, surface kind, sidedness and tint (0xRRGGBB from <see cref="TintKey"/>, 0xFFFFFF = none).</summary>
    public Material? Material(uint textureId, Surface surface, bool twoSided, uint tint = 0xFFFFFF)
    {
        FindTemplates();
        Material? template = surface == Surface.Empty ? null : templates[(int)surface];
        if (template == null)
        {
            return null;
        }
        var key = (textureId, surface, twoSided, tint);
        if (materials.TryGetValue(key, out Material? material))
        {
            return material;
        }
        material = new Material(template) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = false, name = $"LethalCraft {surface} {textureId} {tint:X6}" };
        textures.TryGetValue(textureId, out Texture2D? texture);
        material.SetTexture(BaseColorMap, texture);
        material.mainTexture = texture;
        float r = ((tint >> 16) & 0xFF) / 255f, g = ((tint >> 8) & 0xFF) / 255f, b = (tint & 0xFF) / 255f;
        material.SetColor(BaseColor, new Color(r * brightness, g * brightness, b * brightness, 1f));
        if (material.HasProperty(NormalMap))
        {
            material.SetTexture(NormalMap, null);
        }
        if (material.HasProperty(MaskMap))
        {
            material.SetTexture(MaskMap, null);
        }
        if (material.HasProperty(EmissiveColorMap))
        {
            material.SetTexture(EmissiveColorMap, null);
        }
        if (material.HasProperty(EmissiveColor))
        {
            material.SetColor(EmissiveColor, Color.black);
        }
        if (material.HasProperty(Smoothness))
        {
            material.SetFloat(Smoothness, 0f);
        }
        if (material.HasProperty(Metallic))
        {
            material.SetFloat(Metallic, 0f);
        }
        // HDRP decides keywords and passes from these properties in ValidateMaterial; set them, then validate.
        HDMaterial.SetSurfaceType(material, surface == Surface.Translucent);
        HDMaterial.SetAlphaClipping(material, surface == Surface.Cutout);
        HDMaterial.SetAlphaCutoff(material, 0.1f);
        if (material.HasProperty(DoubleSidedEnable))
        {
            material.SetFloat(DoubleSidedEnable, twoSided ? 1f : 0f);
        }
        if (material.HasProperty(CullMode))
        {
            material.SetFloat(CullMode, twoSided ? (float)UnityEngine.Rendering.CullMode.Off : (float)UnityEngine.Rendering.CullMode.Back);
        }
        if (material.HasProperty(CullModeForward))
        {
            material.SetFloat(CullModeForward, twoSided ? (float)UnityEngine.Rendering.CullMode.Off : (float)UnityEngine.Rendering.CullMode.Back);
        }
        if (surface == Surface.Translucent)
        {
            if (material.HasProperty(BlendMode))
            {
                material.SetFloat(BlendMode, 0f); // alpha
            }
            if (material.HasProperty(ZWrite))
            {
                material.SetFloat(ZWrite, 0f);
            }
        }
        HDMaterial.ValidateMaterial(material);
        if (!loggedMaterial)
        {
            loggedMaterial = true;
            Plugin.Log.LogInfo($"blocks: material '{material.name}' keywords [{string.Join(" ", material.shaderKeywords)}] queue {material.renderQueue}");
        }
        materials[key] = material;
        return material;
    }
}
