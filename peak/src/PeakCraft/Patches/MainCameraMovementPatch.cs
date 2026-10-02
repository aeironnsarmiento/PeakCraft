using HarmonyLib;

namespace PeakCraft.Patches;

/// <summary>
/// PEAK places its camera in MainCameraMovement.LateUpdate (script order 500). Overwriting it
/// right after means PEAK's Interaction (order 600) and the renderer both use Minecraft's view.
/// </summary>
[HarmonyPatch(typeof(MainCameraMovement), "LateUpdate")]
internal static class MainCameraMovementPatch
{
    private static void Postfix()
    {
        Plugin plugin = Plugin.Instance;
        if (!plugin.Ownership.PeakCamera)
        {
            plugin.Camera.Apply(plugin.Follower, plugin.Guest);
        }
    }
}
