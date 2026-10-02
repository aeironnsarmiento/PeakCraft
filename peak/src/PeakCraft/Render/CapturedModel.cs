using System.Collections.Generic;
using PeakCraft.Link;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakCraft.Render;

/// <summary>
/// Geometry Minecraft's own entity renderer produced, sent as batches (one texture each) of
/// triangles: the player's model in third person, the player's standing body (for PEAK's mirrors
/// in first person), and the scene of other entities and particles.
/// </summary>
internal sealed unsafe class CapturedModel
{
    private readonly Atlas atlas;
    private readonly string name;
    private readonly List<Vector3> positions = new();
    private readonly List<Vector2> uvs = new();
    private readonly List<Color32> colours = new();
    private readonly List<int> solid = new();
    private readonly List<int> cutout = new();
    private readonly List<int> blended = new();
    private readonly List<Material> materials = new();
    private Mesh? mesh;
    private bool logged;

    public CapturedModel(Atlas atlas, string name)
    {
        this.atlas = atlas;
        this.name = name;
    }

    public bool Shown { get; private set; }

    public void Hide() => Shown = false;

    /// <summary>Batches then vertices, positions relative to the model's origin; no batches means nothing to draw.</summary>
    public void Set(uint batchCount, uint vertexCount, Proto.RenBatch* batches, Proto.RenVertex* vertices)
    {
        Shown = batchCount > 0 && vertexCount > 0 && atlas.Usable;
        if (!Shown)
        {
            return;
        }
        if (mesh == null)
        {
            mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
        }
        positions.Clear();
        uvs.Clear();
        colours.Clear();
        materials.Clear();
        for (uint i = 0; i < vertexCount; i++)
        {
            Proto.RenVertex* v = &vertices[i];
            positions.Add(new Vector3(v->x, v->y, -v->z)); // Minecraft Z is Unity -Z
            uvs.Add(new Vector2(v->u, v->v));
            uint c = v->color;
            colours.Add(new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24)));
        }
        mesh.Clear();
        mesh.SetVertices(positions);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colours);
        // Up to three submeshes per batch: its solid faces, its faces with holes, its blended faces.
        mesh.subMeshCount = (int)batchCount * 3;
        int sub = 0;
        for (int b = 0; b < batchCount; b++)
        {
            solid.Clear();
            cutout.Clear();
            blended.Clear();
            uint texture = batches[b].texture;
            bool translucent = (batches[b].flags & 1) != 0;
            uint end = System.Math.Min(batches[b].first + batches[b].count, vertexCount);
            for (uint i = batches[b].first; i + 2 < end; i += 3)
            {
                Proto.RenVertex* v = &vertices[i];
                Surface surface = translucent ? Surface.Translucent : atlas.Classify(texture, v[0].u, v[0].v, v[1].u, v[1].v, v[2].u, v[2].v);
                if (surface == Surface.Empty)
                {
                    continue; // a skin's unused outer layer, mostly
                }
                List<int> indices = surface == Surface.Solid ? solid : surface == Surface.Cutout ? cutout : blended;
                indices.Add((int)i);
                indices.Add((int)i + 1);
                indices.Add((int)i + 2);
            }
            AddSubmesh(solid, texture, Surface.Solid, ref sub);
            AddSubmesh(cutout, texture, Surface.Cutout, ref sub);
            AddSubmesh(blended, texture, Surface.Translucent, ref sub);
        }
        mesh.subMeshCount = sub;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (!logged)
        {
            logged = true;
            Plugin.Log.LogInfo($"{name}: first model received ({batchCount} batches, {vertexCount} vertices)");
        }
    }

    private void AddSubmesh(List<int> indices, uint texture, Surface surface, ref int sub)
    {
        Material? material = indices.Count > 0 ? atlas.Material(texture, surface, twoSided: true) : null;
        if (material == null)
        {
            return;
        }
        mesh!.SetTriangles(indices, sub++);
        materials.Add(material);
    }

    /// <summary>Draws for one camera, or for every camera when <paramref name="camera"/> is null.</summary>
    public void Draw(Matrix4x4 matrix, Camera? camera = null, int layer = 0)
    {
        if (!Shown || mesh == null)
        {
            return;
        }
        for (int b = 0; b < materials.Count && b < mesh.subMeshCount; b++)
        {
            Graphics.DrawMesh(mesh, matrix, materials[b], layer, camera, b, null, ShadowCastingMode.On, receiveShadows: true);
        }
    }
}
