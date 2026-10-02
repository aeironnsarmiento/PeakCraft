using HarmonyLib;

namespace PeakCraft.Patches;

/// <summary>
/// Character.DieInstantly is PEAK killing the scout outright (kill planes under the map). While
/// Minecraft owns the body that becomes lethal Minecraft damage instead, so Creative survives it
/// and a Survival death still arrives through Minecraft's own death event.
/// </summary>
[HarmonyPatch(typeof(Character), nameof(Character.DieInstantly))]
internal static class CharacterPatch
{
    private static bool Prefix(Character __instance)
    {
        Plugin plugin = Plugin.Instance;
        if (plugin.Death.Killing || !plugin.Ownership.BodyFollows || __instance != Character.localCharacter)
        {
            return true;
        }
        plugin.Hazards.Lethal();
        return false;
    }
}
