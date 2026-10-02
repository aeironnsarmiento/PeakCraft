using GameNetcodeStuff;
using LethalCraft.Link;
using UnityEngine;

namespace LethalCraft.Player;

/// <summary>Who owns the player. Every patch and module reads this one value.</summary>
internal enum Owner
{
    /// <summary>No link (or no player): Lethal Company runs as if the plugin were not there.</summary>
    HostOwns,

    /// <summary>
    /// Minecraft is about to take over: its teleport to the scout is published but not yet
    /// acknowledged. The host keeps camera, HUD and body, with its controls frozen so nothing drifts.
    /// </summary>
    Handoff,

    /// <summary>Minecraft physics drives the body; the scout is a hidden follower.</summary>
    GuestOwns,

    /// <summary>A Lethal Company menu (pause, chat) is open over a Minecraft-owned body: the host gets the input and its HUD.</summary>
    HostMenu,

    /// <summary>The host took the scout (ship moving, terminal, ladder, enemy animation, death): its camera, body and input show.</summary>
    Cutscene,
}

/// <summary>
/// The ownership state machine. Heartbeat loss drops to <see cref="Owner.HostOwns"/>, which
/// restores the host's movement, camera, HUD and body together.
///
/// | State      | Host movement | Host input | Host camera | Host HUD | Scout shown | Overlay and blocks |
/// | HostOwns   | on            | on         | on          | on       | yes         | off                |
/// | Handoff    | frozen        | off        | on          | on       | yes         | off                |
/// | GuestOwns  | off           | off        | off         | off      | no          | on                 |
/// | HostMenu   | off           | on         | off         | on       | no          | on                 |
/// | Cutscene   | on            | on         | on          | on       | yes         | off                |
///
/// Unlike PEAK, the host keeps its input in a cutscene: the terminal is typed into, a dead scout
/// spectates, and Esc must always reach the pause menu.
/// </summary>
internal sealed class Ownership
{
    public Owner State { get; private set; } = Owner.HostOwns;

    /// <summary>The teleport Minecraft must acknowledge before it gets the body.</summary>
    public uint TeleportSeq { get; private set; } = (uint)(HostLink.TickCount / 1000 % 1000000) * 64 + 2; // never a number an earlier session used

    /// <summary>Why the host holds the scout, for the log.</summary>
    public string CutsceneReason { get; private set; } = "";

    /// <summary>Set by a host-side teleport (entrance doors, ship teleporter) while Minecraft owns the body.</summary>
    public bool HostTeleportPending { get; private set; }

    public bool HostMovement => State is Owner.HostOwns or Owner.Handoff or Owner.Cutscene;
    public bool HostInput => State is Owner.HostOwns or Owner.HostMenu or Owner.Cutscene;
    public bool HostCamera => State is Owner.HostOwns or Owner.Handoff or Owner.Cutscene;
    public bool HostHud => State != Owner.GuestOwns;
    public bool CharacterShown => State is Owner.HostOwns or Owner.Handoff or Owner.Cutscene;
    public bool OverlayAndBlocks => State is Owner.GuestOwns or Owner.HostMenu;

    /// <summary>The body follows Minecraft (GuestOwns or HostMenu).</summary>
    public bool BodyFollows => State is Owner.GuestOwns or Owner.HostMenu;

    /// <summary>Asks Minecraft to teleport to the published position again (after the host moved the scout).</summary>
    public void RequestTeleport() => TeleportSeq++;

    /// <summary>The host moved the scout itself (TeleportPlayer): Minecraft must follow before it drives again.</summary>
    public void HostTeleported()
    {
        if (!BodyFollows)
        {
            return;
        }
        HostTeleportPending = true;
        RequestTeleport();
        Plugin.Log.LogInfo($"ownership: host teleported the scout; teleport {TeleportSeq} requested");
    }

    public void Update(bool linkUp, in Proto.GuestState guest)
    {
        Owner next = Decide(linkUp, guest);
        if (next == State)
        {
            return;
        }
        // A fresh takeover starts from where the scout is now.
        if (next == Owner.Handoff && State is Owner.HostOwns or Owner.Cutscene)
        {
            RequestTeleport();
        }
        if (next == Owner.GuestOwns)
        {
            HostTeleportPending = false;
        }
        Plugin.Log.LogInfo($"ownership: {State} -> {next}{(next == Owner.Cutscene ? $" ({CutsceneReason})" : "")}{(next == Owner.Handoff ? $" (teleport {TeleportSeq})" : "")}");
        State = next;
    }

    private Owner Decide(bool linkUp, in Proto.GuestState guest)
    {
        if (!linkUp)
        {
            return Owner.HostOwns;
        }
        PlayerControllerB? player = LocalPlayer;
        if (player == null)
        {
            // Main menu, or between scenes. Nothing to own; Minecraft parks its player.
            return Owner.HostOwns;
        }
        string? reason = HostHoldsPlayer(player);
        if (reason != null)
        {
            return Cutscene(reason);
        }
        if ((guest.flags & (uint)Proto.GuestFlags.InWorld) == 0)
        {
            return Owner.HostOwns;
        }
        if ((guest.flags & (uint)Proto.GuestFlags.Dead) != 0)
        {
            // Minecraft's player is on its death screen; it comes back to the scout when it respawns.
            return Cutscene("Minecraft player dead");
        }
        if (State is Owner.HostOwns or Owner.Cutscene)
        {
            return Owner.Handoff;
        }
        if (guest.teleportAck != TeleportSeq)
        {
            return Owner.Handoff;
        }
        return HostMenuOpen(player) ? Owner.HostMenu : Owner.GuestOwns;
    }

    private Owner Cutscene(string reason)
    {
        CutsceneReason = reason;
        return Owner.Cutscene;
    }

    /// <summary>The scout this client controls, once it is spawned and controlled.</summary>
    public static PlayerControllerB? LocalPlayer
    {
        get
        {
            GameNetworkManager manager = GameNetworkManager.Instance;
            if (manager == null || manager.localPlayerController == null)
            {
                return null;
            }
            PlayerControllerB player = manager.localPlayerController;
            return player.isPlayerControlled || player.isPlayerDead ? player : null;
        }
    }

    /// <summary>Non-null while Lethal Company itself is moving or showing the scout (see docs/LC-NOTES.md).</summary>
    private static string? HostHoldsPlayer(PlayerControllerB player)
    {
        StartOfRound round = StartOfRound.Instance;
        if (round == null)
        {
            return "no round";
        }
        if (player.isPlayerDead)
        {
            return "dead";
        }
        if (!player.isPlayerControlled)
        {
            return "not controlled";
        }
        if (round.newGameIsLoading)
        {
            return "loading";
        }
        if (round.travellingToNewLevel)
        {
            return "travelling";
        }
        if (!round.inShipPhase && !round.shipHasLanded)
        {
            return round.shipIsLeaving ? "ship leaving" : "ship landing";
        }
        if (round.shipIsLeaving)
        {
            return "ship leaving";
        }
        if (round.firingPlayersCutsceneRunning || round.suckingPlayersOutOfShip)
        {
            return "fired";
        }
        if (player.inTerminalMenu)
        {
            return "terminal";
        }
        if (player.inAnimationWithEnemy != null)
        {
            return "enemy animation";
        }
        if (player.isClimbingLadder)
        {
            return "ladder";
        }
        if (player.inSpecialInteractAnimation)
        {
            return "special animation";
        }
        if (player.inVehicleAnimation)
        {
            return "vehicle";
        }
        if (player.jetpackControls)
        {
            return "jetpack";
        }
        if (player.isFreeCamera)
        {
            return "free camera";
        }
        return null;
    }

    /// <summary>A host window that wants the keyboard and mouse: pause menu, chat, a belt-bag style special menu.</summary>
    public static bool HostMenuOpen(PlayerControllerB player)
    {
        return (player.quickMenuManager != null && player.quickMenuManager.isMenuOpen) || player.isTypingChat || player.inSpecialMenu;
    }
}
