using HarmonyLib;

namespace PeakCraft.Patches;

/// <summary>
/// PEAK's ragdoll movement. FixedUpdate animates, pushes and drags every bodypart; CameraLook
/// turns the mouse into the character's look. Both are replaced while the body follows Minecraft.
/// </summary>
[HarmonyPatch(typeof(CharacterMovement))]
internal static class CharacterMovementPatch
{
    [HarmonyPatch("FixedUpdate")]
    [HarmonyPrefix]
    private static bool FixedUpdatePrefix(CharacterMovement __instance)
    {
        return !(Plugin.Instance.Ownership.BodyFollows && IsLocal(__instance));
    }

    [HarmonyPatch("CameraLook")]
    [HarmonyPrefix]
    private static bool CameraLookPrefix(CharacterMovement __instance)
    {
        Plugin plugin = Plugin.Instance;
        if (!plugin.Ownership.BodyFollows || !IsLocal(__instance))
        {
            return true;
        }
        plugin.Camera.ApplyLookToPeak(__instance.character);
        return false;
    }

    private static bool IsLocal(CharacterMovement movement)
    {
        Character local = Character.localCharacter;
        return local != null && movement.character == local;
    }
}
