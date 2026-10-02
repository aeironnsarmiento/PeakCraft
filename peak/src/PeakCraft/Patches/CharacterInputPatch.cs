using HarmonyLib;
using PeakCraft.Player;

namespace PeakCraft.Patches;

/// <summary>
/// PEAK samples its input actions here (called from CharacterMovement.Update). While Minecraft
/// owns the body none of PEAK's gameplay actions may fire; only the interact key (ours) and
/// Esc (PEAK's pause menu) get through.
/// </summary>
[HarmonyPatch(typeof(CharacterInput), nameof(CharacterInput.Sample))]
internal static class CharacterInputPatch
{
    private static void Postfix(CharacterInput __instance)
    {
        Plugin plugin = Plugin.Instance;
        Character local = Character.localCharacter;
        if (plugin.Ownership.PeakInput || local == null || local.input != __instance)
        {
            return;
        }
        bool pause = __instance.pauseWasPressed;
        __instance.ResetInput();
        __instance.scrollInput = 0f;
        __instance.sprintToggleIsPressed = false;
        __instance.scrollBackwardWasPressed = false;
        __instance.scrollForwardWasPressed = false;
        __instance.scrollBackwardIsPressed = false;
        __instance.scrollForwardIsPressed = false;
        __instance.pingWasPressed = false;
        __instance.selectSlotForwardWasPressed = false;
        __instance.selectSlotBackwardWasPressed = false;
        __instance.unselectSlotWasPressed = false;
        __instance.selectBackpackWasPressed = false;
        __instance.spectateLeftWasPressed = false;
        __instance.spectateRightWasPressed = false;
        __instance.pushToTalkPressed = false;
        if (plugin.Ownership.State == Owner.MinecraftOwns)
        {
            plugin.Interact.Apply(__instance);
            // Esc closes a Minecraft screen first; only with none open does it reach PEAK's pause menu.
            __instance.pauseWasPressed = pause && !plugin.InputBridge.ScreenOpen;
        }
        else
        {
            __instance.pauseWasPressed = false;
        }
    }
}
