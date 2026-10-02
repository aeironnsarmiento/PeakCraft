using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalCraft.Patches;

/// <summary>
/// The funnels on the scout (see docs/LC-NOTES.md). Movement runs inside Update with the action
/// asset disabled and the CharacterController off, so it moves nothing; the postfix then puts the
/// body where Minecraft says. The camera is overwritten after LateUpdate (which is where
/// Lethal Company's look input and hover targeting run), and the host's own hold-to-interact
/// sampler is replaced by the plugin's key while Minecraft has the input.
/// </summary>
[HarmonyPatch(typeof(PlayerControllerB))]
internal static class PlayerControllerBPatch
{
    private static bool IsFollowedLocal(PlayerControllerB player)
    {
        Core? plugin = Core.Instance;
        return plugin != null && plugin.Ownership.BodyFollows && player == GameNetworkManager.Instance?.localPlayerController;
    }

    [HarmonyPatch("Update")]
    [HarmonyPostfix]
    private static void UpdatePostfix(PlayerControllerB __instance)
    {
        if (IsFollowedLocal(__instance))
        {
            Core.Instance!.Follower.AfterHostUpdate(__instance);
        }
    }

    [HarmonyPatch("PlayerLookInput")]
    [HarmonyPrefix]
    private static bool PlayerLookInputPrefix(PlayerControllerB __instance)
    {
        if (!IsFollowedLocal(__instance))
        {
            return true;
        }
        Core.Instance!.Camera.ApplyLookToHost(__instance);
        return false;
    }

    // Before LateUpdate too: the hover targeting inside it must ray-cast from Minecraft's camera,
    // not from where the animator left the scout's head.
    [HarmonyPatch("LateUpdate")]
    [HarmonyPrefix]
    private static void LateUpdatePrefix(PlayerControllerB __instance) => LateUpdatePostfix(__instance);

    [HarmonyPatch("LateUpdate")]
    [HarmonyPostfix]
    private static void LateUpdatePostfix(PlayerControllerB __instance)
    {
        Core? plugin = Core.Instance;
        if (plugin != null && !plugin.Ownership.HostCamera && __instance == GameNetworkManager.Instance?.localPlayerController)
        {
            plugin.Camera.Apply(__instance, plugin.Follower, plugin.Guest);
        }
    }

    [HarmonyPatch("ClickHoldInteraction")]
    [HarmonyPrefix]
    private static bool ClickHoldInteractionPrefix(PlayerControllerB __instance)
    {
        if (!IsFollowedLocal(__instance))
        {
            return true;
        }
        Core.Instance!.Interact.Hold(__instance);
        return false;
    }

    [HarmonyPatch("TeleportPlayer")]
    [HarmonyPostfix]
    private static void TeleportPlayerPostfix(PlayerControllerB __instance, Vector3 pos)
    {
        if (IsFollowedLocal(__instance))
        {
            Core.Instance!.Follower.HostMoved(__instance, pos);
            Core.Instance!.Ownership.HostTeleported();
        }
    }

    [HarmonyPatch(nameof(PlayerControllerB.DamagePlayer))]
    [HarmonyPrefix]
    private static bool DamagePlayerPrefix(PlayerControllerB __instance, int damageNumber, CauseOfDeath causeOfDeath, bool fallDamage)
    {
        if (!IsFollowedLocal(__instance))
        {
            return true;
        }
        Core.Instance!.Hazards.Intercept(damageNumber, causeOfDeath, fallDamage);
        return false;
    }

    [HarmonyPatch(nameof(PlayerControllerB.KillPlayer))]
    [HarmonyPrefix]
    private static bool KillPlayerPrefix(PlayerControllerB __instance, CauseOfDeath causeOfDeath)
    {
        Core? plugin = Core.Instance;
        if (plugin == null || plugin.Death.Killing || !IsFollowedLocal(__instance))
        {
            return true;
        }
        plugin.Hazards.Lethal(causeOfDeath);
        return false;
    }
}
