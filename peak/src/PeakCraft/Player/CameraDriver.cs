using PeakCraft.Link;
using PeakCraft.World;
using UnityEngine;

namespace PeakCraft.Player;

/// <summary>
/// Puts PEAK's camera where Minecraft's is. Runs after PEAK's own camera update
/// (postfix on MainCameraMovement.LateUpdate), so PEAK's interaction ray aims with it.
/// </summary>
internal sealed class CameraDriver
{
    private float lastCompare;

    /// <summary>The authoritative look, Minecraft degrees. The input bridge integrates the mouse into it.</summary>
    public float Yaw;
    public float Pitch;

    public void SetLookFromPeak(Character character)
    {
        Yaw = Coords.McYawFromUnity(character.data.lookValues.x);
        Pitch = Mathf.Clamp(-character.data.lookValues.y, -90f, 90f);
    }

    /// <summary>Writes the look into PEAK's character so its own look direction (interaction, triggers) agrees.</summary>
    public void ApplyLookToPeak(Character character)
    {
        character.data.lookValues = new Vector2(Coords.UnityYawFromMc(Yaw), Mathf.Clamp(-Pitch, -85f, 85f));
        character.RecalculateLookDirections();
    }

    public void Apply(Follower follower, in Proto.GuestState guest)
    {
        MainCamera main = MainCamera.instance;
        if (main == null || main.cam == null)
        {
            return;
        }
        Vector3 eye = follower.Feet + Vector3.up * follower.EyeHeight;
        Quaternion rotation = Coords.Rotation(Yaw, Pitch);
        Vector3 position = eye;
        // Minecraft's F5: 1 = behind the player, 2 = in front, looking back. The distance already
        // includes Minecraft's own zoom collision against blocks and PEAK's triangles.
        if (guest.cameraMode == 1)
        {
            position = eye - rotation * Vector3.forward * guest.cameraDistance;
        }
        else if (guest.cameraMode == 2)
        {
            position = eye + rotation * Vector3.forward * guest.cameraDistance;
            rotation = Coords.Rotation(Yaw + 180f, -Pitch);
        }
        main.transform.SetPositionAndRotation(position, rotation);
        if (guest.fovDeg > 1f)
        {
            main.cam.fieldOfView = guest.fovDeg; // both are vertical degrees
        }

        if (Time.unscaledTime - lastCompare > 5f)
        {
            lastCompare = Time.unscaledTime;
            Vector3 mcEye = Coords.ToUnity(guest.eyeX, guest.eyeY, guest.eyeZ);
            Plugin.Log.LogInfo($"camera: PEAK camera {main.transform.position:F3}, Minecraft eye {mcEye:F3} (Unity space), apart {(eye - mcEye).magnitude * 100f:F1} cm, mode {guest.cameraMode}, fov {guest.fovDeg:F1}");
        }
    }
}
