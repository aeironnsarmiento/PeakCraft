using GameNetcodeStuff;
using LethalCraft.Link;
using LethalCraft.Player;
using UnityEngine;

namespace LethalCraft.Hazards;

/// <summary>
/// A Minecraft death in Survival is the scout's death: Lethal Company's own death flow runs
/// (body, spectating, revived at the ship when the day ends). Minecraft's player is respawned for
/// the player and waits, parked, until the host hands the scout back; then it is teleported to it.
/// </summary>
internal sealed class Death
{
    private readonly HostLink link;
    private float deadSince = -1f;
    private float nextClick;
    private bool buttonDown;

    public Death(HostLink link)
    {
        this.link = link;
    }

    /// <summary>True while this class is calling the host's death itself (the KillPlayer prefix lets it through).</summary>
    public bool Killing { get; private set; }

    /// <summary>Minecraft's kEvPlayerDied.</summary>
    public void OnMinecraftDeath()
    {
        PlayerControllerB? player = Ownership.LocalPlayer;
        if (player == null || player.isPlayerDead)
        {
            return;
        }
        Plugin.Log.LogInfo("death: Minecraft player died; running Lethal Company's death for the scout");
        Killing = true;
        try
        {
            Core.Instance!.Follower.Release();
            player.KillPlayer(Vector3.zero, spawnBody: true, CauseOfDeath.Unknown, 0);
        }
        finally
        {
            Killing = false;
        }
    }

    /// <summary>Clicks "Respawn" on Minecraft's death screen so the player is never stuck behind it during the host's death flow.</summary>
    public void Update(bool linked, in Proto.GuestState guest)
    {
        bool dead = linked && (guest.flags & (uint)Proto.GuestFlags.Dead) != 0;
        if (!dead)
        {
            deadSince = -1f;
            if (buttonDown)
            {
                buttonDown = false;
                link.PushInput(Proto.InputType.MouseButton, 1, 0);
            }
            return;
        }
        if (deadSince < 0f)
        {
            deadSince = Time.unscaledTime;
            nextClick = deadSince + 2f; // the button is disabled for the first second
        }
        if (buttonDown)
        {
            buttonDown = false;
            link.PushInput(Proto.InputType.MouseButton, 1, 0);
            return;
        }
        if (Time.unscaledTime < nextClick || (guest.flags & (uint)Proto.GuestFlags.ScreenOpen) == 0)
        {
            return;
        }
        nextClick = Time.unscaledTime + 1f;
        // DeathScreen puts "Respawn" at (width / 2 - 100, height / 4 + 72), 200 x 20, in GUI-scaled pixels.
        int scale = Mathf.Max(1, (int)guest.guiScale);
        int x = Screen.width / 2;
        int y = (Screen.height / scale / 4 + 82) * scale;
        link.PushInput(Proto.InputType.Cursor, 0, x, y);
        link.PushInput(Proto.InputType.MouseButton, 1, 1);
        buttonDown = true;
        Plugin.Log.LogInfo($"death: clicking Respawn on Minecraft's death screen at ({x}, {y})");
    }
}
