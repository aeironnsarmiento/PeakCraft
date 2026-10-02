using System.Collections.Generic;
using GameNetcodeStuff;
using LethalCraft.Link;
using LethalCraft.World;
using UnityEngine;

namespace LethalCraft.Player;

/// <summary>
/// Keeps the scout where Minecraft's player is. The scout stays in the world as a hidden follower
/// with its CharacterController still enabled (but overruled every frame), so Lethal Company's
/// triggers (ship room, elevator, quicksand, enemies' line of sight, the Company desk) keep
/// seeing a player at the right spot.
/// See docs/LC-NOTES.md for the members used.
/// </summary>
internal sealed class Follower
{
    private readonly HostLink link;
    private PlayerControllerB? held;
    private readonly List<Renderer> hidden = new();
    private bool controllerWasEnabled;

    public Follower(HostLink link)
    {
        this.link = link;
    }

    /// <summary>Minecraft's interpolated feet this frame, in Unity space.</summary>
    public Vector3 Feet { get; private set; }

    /// <summary>Minecraft's eye height above the feet this frame, in blocks.</summary>
    public float EyeHeight { get; private set; } = 1.62f;

    /// <summary>Where the scout stands (its feet), in Unity space. The controller's root is at the feet.</summary>
    public static Vector3 HostFeet(PlayerControllerB player) => player.transform.position;

    /// <summary>Call every frame with the current ownership.</summary>
    public void Update(Ownership ownership, in Proto.GuestState guest)
    {
        PlayerControllerB? player = Ownership.LocalPlayer;
        bool follow = ownership.BodyFollows && player != null;
        if (held != null && (!follow || held != player))
        {
            Release();
        }
        if (!follow)
        {
            return;
        }
        if (held == null)
        {
            Take(player!);
        }
        Interpolate(guest);
        Place(player!, guest);
    }

    private void Take(PlayerControllerB player)
    {
        held = player;
        controllerWasEnabled = player.thisController.enabled;
        // The controller stays enabled: its collider is what the ship room, the elevator and the
        // reverb volumes detect the scout with. Its Move is harmless (fallValue is zeroed every
        // frame) and the position is snapped around a disable/enable, as TeleportPlayer does.
        hidden.Clear();
        Hide(player.thisPlayerModel);
        Hide(player.thisPlayerModelLOD1);
        Hide(player.thisPlayerModelLOD2);
        Hide(player.thisPlayerModelArms);
        Plugin.Log.LogInfo($"follower: took the scout ({hidden.Count} renderers hidden)");
    }

    private void Hide(Renderer? renderer)
    {
        if (renderer != null && renderer.enabled)
        {
            renderer.enabled = false;
            hidden.Add(renderer);
        }
    }

    /// <summary>Hands the body back to Lethal Company's own physics, standing where it is.</summary>
    public void Release()
    {
        if (held == null)
        {
            return;
        }
        foreach (Renderer renderer in hidden)
        {
            if (renderer != null)
            {
                renderer.enabled = true;
            }
        }
        hidden.Clear();
        // A destroyed player (scene change) compares equal to null.
        if (held != null)
        {
            held.thisController.enabled = controllerWasEnabled;
            held.fallValue = 0f;
            held.fallValueUncapped = 0f;
            held.takingFallDamage = false;
            held.isFallingNoJump = false;
            held.isFallingFromJump = false;
            held.isJumping = false;
        }
        held = null;
        Plugin.Log.LogInfo("follower: released the scout");
    }

    // Same idea as Minecraft's renderer: blend the last two 20 Hz physics ticks on our own frame
    // clock, so the two games' frame phases don't beat against each other.
    private void Interpolate(in Proto.GuestState guest)
    {
        Vector3 reported = Coords.ToUnity(guest.x, guest.y, guest.z);
        float eye = guest.eyeHeight > 0.1f ? guest.eyeHeight : 1.62f;
        if (guest.tickQpc != 0 && guest.tickMs > 1f)
        {
            double ageMs = (HostLink.Qpc() - guest.tickQpc) * 1000.0 / link.QpcFrequency;
            if (ageMs >= 0 && ageMs < 250)
            {
                float t = Mathf.Clamp01((float)(ageMs / guest.tickMs));
                Vector3 previous = Coords.ToUnity(guest.prevX, guest.prevY, guest.prevZ);
                Vector3 current = Coords.ToUnity(guest.curX, guest.curY, guest.curZ);
                Vector3 blended = Vector3.Lerp(previous, current, t);
                // After a teleport the tick pair is stale for a moment; trust the reported position then.
                if ((blended - reported).sqrMagnitude < 4f * Coords.Scale * Coords.Scale)
                {
                    reported = blended;
                    eye = Mathf.Lerp(guest.tickEyeO, guest.tickEye, t);
                }
            }
        }
        Feet = reported;
        EyeHeight = eye;
    }

    /// <summary>
    /// The host moved the scout itself (TeleportPlayer: entrance doors, the ship's teleporters,
    /// the spawn at a new day). Minecraft is asked to follow; until it does, the body stays put.
    /// </summary>
    public void HostMoved(PlayerControllerB player, Vector3 pos)
    {
        Feet = pos;
        hostMoved = true;
        Plugin.Log.LogInfo($"follower: host moved the scout to {pos:F2}");
    }

    private bool hostMoved;

    /// <summary>After PlayerControllerB.Update ran (with its controller off): re-assert what it may have changed.</summary>
    public void AfterHostUpdate(PlayerControllerB player)
    {
        if (held != player)
        {
            return;
        }
        if (hostMoved)
        {
            hostMoved = false;
            return;
        }
        Snap(player, Feet);
        player.fallValue = 0f;
        player.fallValueUncapped = 0f;
        player.takingFallDamage = false;
        player.isFallingNoJump = false;
        player.isFallingFromJump = false;
        player.isJumping = false;
        player.sprintMeter = 1f;
    }

    /// <summary>Moves the scout the way TeleportPlayer does: the controller is off while its transform is set.</summary>
    private static void Snap(PlayerControllerB player, Vector3 feet)
    {
        float apart = (feet - player.transform.position).sqrMagnitude;
        if (apart < 1e-8f || apart > 1e12f)
        {
            return;
        }
        bool was = player.thisController.enabled;
        player.thisController.enabled = false;
        player.transform.position = feet;
        player.thisController.enabled = was;
        player.serverPlayerPosition = feet;
    }

    private void Place(PlayerControllerB player, in Proto.GuestState guest)
    {
        if (hostMoved)
        {
            return; // a host teleport is pending; Minecraft has not caught up yet
        }
        Snap(player, Feet);
        // The scout's own ground checks are off (its controller is disabled): tell it what Minecraft
        // knows, so nothing downstream thinks it is in a long fall or out of breath.
        player.fallValue = 0f;
        player.fallValueUncapped = 0f;
        player.takingFallDamage = false;
        player.isFallingNoJump = false;
        player.isFallingFromJump = false;
        player.isJumping = false;
        player.isSprinting = (guest.flags & (uint)Proto.GuestFlags.Sprinting) != 0;
        player.isCrouching = (guest.flags & (uint)Proto.GuestFlags.Sneaking) != 0;
        player.sprintMeter = 1f;
        player.isExhausted = false;
        player.isMovementHindered = 0;
    }
}
