using UnityEngine;

namespace LethalCraft.World;

/// <summary>
/// The one place Unity space and Minecraft space meet. Lethal Company's scout is about 2.5 units
/// tall against Minecraft's 1.8 blocks, so one block is <see cref="Scale"/> Unity units (config).
/// Unity is left-handed (X right, Y up, Z forward); Minecraft is right-handed (X east, Y up,
/// Z south), so Z flips. A world-slot origin keeps every moon (and the ship in orbit) in its own
/// part of the one mirror world, so blocks built on one moon are not found on another.
///
///     mc.x = unity.x / Scale + OriginX      mc.y = unity.y / Scale + YOffset      mc.z = -unity.z / Scale + OriginZ
///     mc.yaw = unity yaw + 180 (Minecraft yaw 0 looks along +Z, south)
///     mc.pitch = Lethal Company's cameraUp (both are positive looking down)
/// </summary>
internal static class Coords
{
    /// <summary>Unity units per Minecraft block.</summary>
    public static float Scale = 1.4f;

    /// <summary>Added to Unity Y / Scale to get Minecraft Y. A multiple of 8 so regions line up.</summary>
    public static double YOffset = 0.0;

    /// <summary>The current world slot's origin, Minecraft blocks (see <see cref="WorldSlots"/>).</summary>
    public static double OriginX, OriginZ;

    public static void ToMc(Vector3 unity, out double x, out double y, out double z)
    {
        x = unity.x / Scale + OriginX;
        y = unity.y / Scale + YOffset;
        z = -unity.z / Scale + OriginZ;
    }

    public static Vector3 ToUnity(double x, double y, double z)
        => new((float)((x - OriginX) * Scale), (float)((y - YOffset) * Scale), (float)(-(z - OriginZ) * Scale));

    /// <summary>A Minecraft-space offset (no origin) in Unity units.</summary>
    public static Vector3 OffsetToUnity(float x, float y, float z) => new(x * Scale, y * Scale, -z * Scale);

    public static float McYawFromUnity(float unityYaw) => Mathf.DeltaAngle(0f, unityYaw + 180f);

    public static float UnityYawFromMc(float mcYaw) => Mathf.DeltaAngle(0f, mcYaw - 180f);

    /// <summary>Unity rotation of a view with Minecraft yaw and pitch.</summary>
    public static Quaternion Rotation(float mcYaw, float mcPitch) => Quaternion.Euler(mcPitch, mcYaw + 180f, 0f);
}
