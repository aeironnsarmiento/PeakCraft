using PeakCraft.Link;
using UnityEngine;

namespace PeakCraft.Hazards;

/// <summary>
/// A Minecraft death in Survival is the scout's death: PEAK's own death flow runs (skeleton, end
/// of a solo run, back to the airport). Minecraft's player is respawned for the player and waits,
/// parked, until PEAK hands the character back; then it is teleported to it.
/// </summary>
internal sealed class Death
{
    private readonly PeakLink link;
    private float deadSince = -1f;
    private float nextClick;
    private bool buttonDown;

    public Death(PeakLink link)
    {
        this.link = link;
    }

    /// <summary>True while this class is calling PEAK's death itself (the DieInstantly prefix lets it through).</summary>
    public bool Killing { get; private set; }

    /// <summary>Minecraft's kEvPlayerDied.</summary>
    public void OnMinecraftDeath()
    {
        Character character = Character.localCharacter;
        if (character == null || character.data.dead)
        {
            return;
        }
        if (character.inAirport)
        {
            Plugin.Log.LogInfo("death: Minecraft player died in the airport; PEAK has no death there, Minecraft just respawns");
            return;
        }
        Plugin.Log.LogInfo("death: Minecraft player died; running PEAK's death for the scout");
        Killing = true;
        try
        {
            character.DieInstantly();
        }
        finally
        {
            Killing = false;
        }
    }

    /// <summary>Clicks "Respawn" on Minecraft's death screen so the player is never stuck behind it during PEAK's death flow.</summary>
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
