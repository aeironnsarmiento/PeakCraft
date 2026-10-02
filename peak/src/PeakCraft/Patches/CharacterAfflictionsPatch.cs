using HarmonyLib;

namespace PeakCraft.Patches;

/// <summary>
/// Every PEAK hazard ends up in CharacterAfflictions.AddStatus (the fog's cold comes from
/// Fog.MakePlayerCold, lava's heat from Lava, and so on). While Minecraft owns the body the amount
/// goes to the affliction bridge instead of onto the character.
/// </summary>
[HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
internal static class CharacterAfflictionsPatch
{
    private static bool Prefix(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, float amount, ref bool __result)
    {
        if (!Plugin.Instance.Hazards.Intercept(__instance, statusType, amount))
        {
            return true;
        }
        __result = false;
        return false;
    }
}
