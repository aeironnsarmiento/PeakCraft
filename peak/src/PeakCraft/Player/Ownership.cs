using PeakCraft.Link;
using UnityEngine;

namespace PeakCraft.Player;

/// <summary>Who owns the player. Every patch and module reads this one value.</summary>
internal enum Owner
{
    /// <summary>No link (or no character): PEAK runs as if the plugin were not there.</summary>
    PeakOwns,

    /// <summary>
    /// Minecraft is about to take over: its teleport to PEAK's character is published but not yet
    /// acknowledged. PEAK keeps camera, HUD and body, with its controls frozen so nothing drifts.
    /// </summary>
    Handoff,

    /// <summary>Minecraft physics drives the body; PEAK's character is a hidden follower.</summary>
    MinecraftOwns,

    /// <summary>A PEAK menu is open over a Minecraft-owned body: PEAK gets the input and its HUD.</summary>
    PeakMenu,

    /// <summary>PEAK took the character (loading, flight, crash, pass-out, death, ending): PEAK's camera and body show.</summary>
    Cutscene,
}

/// <summary>
/// The ownership state machine (see the state diagram in the plan). Heartbeat loss drops to
/// <see cref="Owner.PeakOwns"/>, which restores PEAK's movement, camera, HUD and character together.
///
/// | State          | PEAK movement | PEAK input | PEAK camera | PEAK HUD | Character shown | Overlay and blocks |
/// | PeakOwns       | on            | on         | on          | on       | yes             | off                |
/// | Handoff        | frozen        | off        | on          | on       | yes             | off                |
/// | MinecraftOwns  | off           | off        | off         | off      | no              | on                 |
/// | PeakMenu       | off           | on         | off         | on       | no              | on                 |
/// | Cutscene       | on            | off        | on          | on       | yes             | on                 |
/// </summary>
internal sealed class Ownership
{
    public Owner State { get; private set; } = Owner.PeakOwns;

    /// <summary>The teleport Minecraft must acknowledge before it gets the body.</summary>
    public uint TeleportSeq { get; private set; } = (uint)(PeakLink.TickCount / 1000 % 1000000) * 64 + 2; // never a number an earlier session used

    /// <summary>Why PEAK holds the character, for the log.</summary>
    public string CutsceneReason { get; private set; } = "";

    public bool PeakMovement => State is Owner.PeakOwns or Owner.Handoff or Owner.Cutscene;
    public bool PeakInput => State is Owner.PeakOwns or Owner.PeakMenu;
    public bool PeakCamera => State is Owner.PeakOwns or Owner.Handoff or Owner.Cutscene;
    public bool PeakHud => State != Owner.MinecraftOwns;
    public bool CharacterShown => State is Owner.PeakOwns or Owner.Handoff or Owner.Cutscene;
    public bool OverlayAndBlocks => State is Owner.MinecraftOwns or Owner.PeakMenu or Owner.Cutscene;

    /// <summary>The body follows Minecraft (MinecraftOwns or PeakMenu).</summary>
    public bool BodyFollows => State is Owner.MinecraftOwns or Owner.PeakMenu;

    /// <summary>Asks Minecraft to teleport to the published position again (after PEAK moved the character).</summary>
    public void RequestTeleport() => TeleportSeq++;

    public void Update(bool linkUp, in Proto.GuestState guest)
    {
        Owner next = Decide(linkUp, guest);
        if (next == State)
        {
            return;
        }
        // A fresh takeover starts from where PEAK's character is now.
        if (next == Owner.Handoff && State is Owner.PeakOwns or Owner.Cutscene)
        {
            RequestTeleport();
        }
        Plugin.Log.LogInfo($"ownership: {State} -> {next}{(next == Owner.Cutscene ? $" ({CutsceneReason})" : "")}{(next == Owner.Handoff ? $" (teleport {TeleportSeq})" : "")}");
        State = next;
    }

    private Owner Decide(bool linkUp, in Proto.GuestState guest)
    {
        if (!linkUp)
        {
            return Owner.PeakOwns;
        }
        Character character = Character.localCharacter;
        if (character == null)
        {
            // Title screen, or between scenes. Nothing to own; Minecraft parks its player.
            return LoadingScreenHandler.loading ? Cutscene("loading") : Owner.PeakOwns;
        }
        string? reason = PeakHoldsCharacter(character);
        if (reason != null)
        {
            return Cutscene(reason);
        }
        if ((guest.flags & (uint)Proto.GuestFlags.InWorld) == 0)
        {
            return Owner.PeakOwns;
        }
        if ((guest.flags & (uint)Proto.GuestFlags.Dead) != 0)
        {
            // Minecraft's player is on its death screen; it comes back to PEAK's character when it respawns.
            return Cutscene("Minecraft player dead");
        }
        if (State is Owner.PeakOwns or Owner.Cutscene)
        {
            return Owner.Handoff;
        }
        if (guest.teleportAck != TeleportSeq)
        {
            return Owner.Handoff;
        }
        return PeakMenuOpen() ? Owner.PeakMenu : Owner.MinecraftOwns;
    }

    private Owner Cutscene(string reason)
    {
        CutsceneReason = reason;
        return Owner.Cutscene;
    }

    /// <summary>Non-null while PEAK itself is moving or showing the character (see docs/PEAK-NOTES.md).</summary>
    private static string? PeakHoldsCharacter(Character character)
    {
        CharacterData data = character.data;
        if (LoadingScreenHandler.loading)
        {
            return "loading";
        }
        if (data.dead)
        {
            return "dead";
        }
        if (data.passedOut || data.fullyPassedOut)
        {
            return "passed out";
        }
        if (data.passedOutOnTheBeach > 0f || data.fallSeconds > 0f)
        {
            return "crash landing";
        }
        if (character.warping)
        {
            return "warp";
        }
        if (data.isCarried)
        {
            return "carried";
        }
        if (MainCamera.instance != null && MainCamera.instance.camOverride)
        {
            return "camera override";
        }
        if (Singleton_MainCameraMovement_IsGodCam())
        {
            return "god cam";
        }
        GUIManager gui = GUIManager.instance;
        if (gui != null && gui.endScreen != null && gui.endScreen.isOpen)
        {
            return "end screen";
        }
        if (character.refs.stats != null && character.refs.stats.won)
        {
            return "ending";
        }
        return null;
    }

    private static bool Singleton_MainCameraMovement_IsGodCam()
    {
        MainCameraMovement movement = Zorro.Core.Singleton<MainCameraMovement>.Instance;
        return movement != null && movement.isGodCam;
    }

    /// <summary>The same test PEAK's CursorHandler uses to free the mouse cursor.</summary>
    public static bool PeakMenuOpen()
    {
        GUIManager gui = GUIManager.instance;
        if (gui != null && (gui.windowShowingCursor || gui.windowBlockingInput || gui.wheelActive))
        {
            return true;
        }
        return Zorro.UI.Modal.Modal.IsOpen;
    }
}
