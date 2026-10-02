using System.Collections.Generic;
using LethalCraft.Link;
using LethalCraft.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace LethalCraft.Render;

/// <summary>
/// Minecraft's placed blocks as Unity meshes, one per 16-block section, sitting in the host's world so
/// its depth buffer, lights, shadows and fog treat them like any other scenery.
/// </summary>
internal sealed unsafe class BlockSections
{
    private readonly Atlas atlas;
    private readonly Dictionary<(int, int, int), GameObject> sections = new();
    private readonly List<Vector3> positions = new();
    private readonly List<Vector2> uvs = new();
    private readonly List<Color32> colours = new();
    private readonly Dictionary<(Surface, uint), List<int>> groups = new();
    private GameObject? root;

    public BlockSections(Atlas atlas)
    {
        this.atlas = atlas;
    }

    /// <summary>Room (8): rendered by the gameplay camera and seen by the scout's own ray casts.</summary>
    public const int BlockLayer = 8;

    public int Count => sections.Count;

    /// <summary>The world slot's origin moved: put every section where its Minecraft coordinates now land.</summary>
    public void Reposition()
    {
        foreach (KeyValuePair<(int, int, int), GameObject> pair in sections)
        {
            (int sx, int sy, int sz) = pair.Key;
            pair.Value.transform.position = Coords.ToUnity(sx * 16.0, sy * 16.0, sz * 16.0);
        }
    }

    public void SetVisible(bool visible)
    {
        if (root != null && root.activeSelf != visible)
        {
            root.SetActive(visible);
        }
    }

    /// <summary>kRenClearAll: Minecraft's world changed (or it restarted); every section goes.</summary>
    public void Clear()
    {
        foreach (GameObject section in sections.Values)
        {
            Remove(section);
        }
        sections.Clear();
    }

    private static void Remove(GameObject section)
    {
        MeshFilter filter = section.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            Object.Destroy(filter.sharedMesh);
        }
        Object.Destroy(section);
    }

    /// <summary>kRenSection: a section's whole mesh as a triangle list; no vertices removes it.</summary>
    public void SetSection(in Proto.RenSectionHdr header, Proto.RenVertex* vertices)
    {
        var key = (header.sx, header.sy, header.sz);
        if (sections.TryGetValue(key, out GameObject? existing))
        {
            Remove(existing);
            sections.Remove(key);
        }
        int count = (int)header.vertexCount / 3 * 3;
        if (count == 0 || !atlas.Usable)
        {
            return;
        }
        positions.Clear();
        uvs.Clear();
        colours.Clear();
        groups.Clear();
        for (int i = 0; i < count; i += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                Proto.RenVertex* v = &vertices[i + k];
                positions.Add(new Vector3(v->x, v->y, -v->z)); // Minecraft Z is Unity -Z
                uvs.Add(new Vector2(v->u, v->v));
                uint c = v->color;
                colours.Add(new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24)));
            }
            // Minecraft marks every non-translucent quad "cutout", so the texture decides: only faces
            // that really have holes (leaves, torches, plants) take the clipped path.
            Proto.RenVertex* t = &vertices[i];
            Surface surface = (t->flags & 2) != 0 ? Surface.Translucent : atlas.Classify(0, t[0].u, t[0].v, t[1].u, t[1].v, t[2].u, t[2].v);
            if (surface == Surface.Empty)
            {
                continue;
            }
            // The host's material cannot read vertex colour: faces are grouped by tint instead.
            uint tint = atlas.SupportsVertexColor ? 0xFFFFFF : Atlas.TintKey(t->color);
            if (!groups.TryGetValue((surface, tint), out List<int>? indices))
            {
                indices = new List<int>();
                groups[(surface, tint)] = indices;
            }
            // The Z flip mirrors the triangle, so two corners swap to keep it facing outwards.
            indices.Add(i);
            indices.Add(i + 2);
            indices.Add(i + 1);
        }

        var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(positions);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colours);
        var materials = new List<Material>(groups.Count);
        mesh.subMeshCount = groups.Count;
        int sub = 0;
        foreach (KeyValuePair<(Surface surface, uint tint), List<int>> group in groups)
        {
            Material? material = atlas.Material(0, group.Key.surface, twoSided: group.Key.surface == Surface.Cutout, group.Key.tint);
            if (material == null)
            {
                continue;
            }
            mesh.SetTriangles(group.Value, sub++);
            materials.Add(material);
        }
        mesh.subMeshCount = sub;
        if (materials.Count == 0)
        {
            Object.Destroy(mesh);
            return;
        }
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (root == null)
        {
            root = new GameObject("LethalCraft blocks") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(root);
        }
        var section = new GameObject($"section {header.sx} {header.sy} {header.sz}");
        section.transform.SetParent(root.transform, worldPositionStays: false);
        section.transform.position = Coords.ToUnity(header.sx * 16.0, header.sy * 16.0, header.sz * 16.0);
        section.transform.localScale = Vector3.one * Coords.Scale;
        section.layer = BlockLayer;
        section.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = section.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials.ToArray();
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        sections[key] = section;
    }
}
