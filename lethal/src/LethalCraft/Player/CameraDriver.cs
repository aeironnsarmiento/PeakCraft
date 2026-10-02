using GameNetcodeStuff;
using LethalCraft.Link;
using LethalCraft.World;
using UnityEngine;

namespace LethalCraft.Player;

/// <summary>
/// Puts the scout's camera where Minecraft's is. Runs after Lethal Company's own camera update
/// (postfix on PlayerControllerB.LateUpdate), so its interaction ray and visor aim with it.
/// </summary>
internal sealed class CameraDriver
{
    private float lastCompare;

    /// <summary>The authoritative look, Minecraft degrees. The input bridge integrates the mouse into it.</summary>
    public float Yaw;
    public float Pitch;

    public void SetLookFromHost(PlayerControllerB player)
    {
        Yaw = Coords.McYawFromUnity(player.transform.eulerAngles.y);
        Pitch = Mathf.Clamp(player.cameraUp, -90f, 90f);
    }

    /// <summary>Writes the look into the scout so its own facing (interaction, enemies' checks, animations) agrees.</summary>
    public void ApplyLookToHost(PlayerControllerB player)
    {
        Vector3 euler = player.transform.eulerAngles;
        player.transform.eulerAngles = new Vector3(0f, Coords.UnityYawFromMc(Yaw), 0f);
        player.cameraUp = Mathf.Clamp(Pitch, -80f, 80f);
        player.cameraContainerTransform.localEulerAngles = new Vector3(player.cameraUp, 0f, 0f);
        _ = euler;
    }

    public void Apply(PlayerControllerB player, Follower follower, in Proto.GuestState guest)
    {
        Camera camera = player.gameplayCamera;
        if (camera == null)
        {
            return;
        }
        Vector3 eye = follower.Feet + Vector3.up * (follower.EyeHeight * Coords.Scale);
        Quaternion rotation = Coords.Rotation(Yaw, Pitch);
        Vector3 position = eye;
        // Minecraft's F5: 1 = behind the player, 2 = in front, looking back. The distance already
        // includes Minecraft's own zoom collision against blocks and the host's triangles.
        if (guest.cameraMode == 1)
        {
            position = eye - rotation * Vector3.forward * (guest.cameraDistance * Coords.Scale);
        }
        else if (guest.cameraMode == 2)
        {
            position = eye + rotation * Vector3.forward * (guest.cameraDistance * Coords.Scale);
            rotation = Coords.Rotation(Yaw + 180f, -Pitch);
        }
        player.cameraContainerTransform.SetPositionAndRotation(position, rotation);
        camera.transform.SetPositionAndRotation(position, rotation);
        if (guest.fovDeg > 1f)
        {
            camera.fieldOfView = guest.fovDeg; // both are vertical degrees
            player.targetFOV = guest.fovDeg;
        }

        if (Time.unscaledTime - lastCompare > 5f)
        {
            lastCompare = Time.unscaledTime;
            Vector3 mcEye = Coords.ToUnity(guest.eyeX, guest.eyeY, guest.eyeZ);
            Plugin.Log.LogInfo($"camera: scout camera {camera.transform.position:F3}, Minecraft eye {mcEye:F3} (Unity space), apart {(eye - mcEye).magnitude * 100f:F1} cm, mode {guest.cameraMode}, fov {guest.fovDeg:F1}");
        }
    }
}
