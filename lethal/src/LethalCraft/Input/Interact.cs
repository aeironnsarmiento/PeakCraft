using GameNetcodeStuff;
using UnityEngine;

namespace LethalCraft.Input;

/// <summary>
/// Lethal Company's interact while Minecraft owns the body. The game's own "Interact" action is
/// disabled with the rest of its input, so the plugin reads the action's bindings itself and
/// drives the same two code paths the action would: a press grabs or interacts (as
/// Interact_performed does) and a hold feeds the hold-to-interact fill (ClickHoldInteraction).
/// The target comes from the scout's own ray from the (Minecraft-driven) camera.
/// </summary>
internal sealed class Interact
{
    private bool held;
    private float devHoldUntil = -1f;

    public bool Held => held;
    public bool Pressed { get; private set; }
    public bool Released { get; private set; }

    /// <summary>Dev harness: hold the key for a while without a keyboard.</summary>
    public void DevHold(float seconds) => devHoldUntil = Time.unscaledTime + seconds;

    /// <summary>What the scout's interact ray would act on right now: a trigger or a grabbable prop.</summary>
    public static bool HasTarget(PlayerControllerB player, out float distance)
    {
        distance = -1f;
        if (player.hoveringOverTrigger != null && player.hoveringOverTrigger.interactable)
        {
            distance = Vector3.Distance(player.gameplayCamera.transform.position, player.hoveringOverTrigger.transform.position);
            return true;
        }
        var ray = new Ray(player.gameplayCamera.transform.position, player.gameplayCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, player.grabDistance, player.interactableObjectsMask)
            && hit.collider.gameObject.layer != 8 && hit.collider.gameObject.layer != 30
            && (hit.collider.CompareTag("PhysicsProp") || hit.collider.CompareTag("InteractTrigger")))
        {
            distance = hit.distance;
            return true;
        }
        return false;
    }

    public void Update(bool keyDown, PlayerControllerB? player)
    {
        bool down = keyDown || Time.unscaledTime < devHoldUntil;
        Pressed = down && !held;
        Released = !down && held;
        held = down;
        if (Pressed && player != null)
        {
            Press(player);
        }
    }

    /// <summary>What Interact_performed does, minus the checks that belong to the dead and the spectating.</summary>
    private static void Press(PlayerControllerB player)
    {
        if (player.isGrabbingObjectAnimation || player.isTypingChat || player.inTerminalMenu || player.throwingObject
            || player.IsInspectingItem || player.inAnimationWithEnemy != null || player.jetpackControls
            || player.disablingJetpackControls || StartOfRound.Instance.suckingPlayersOutOfShip || player.inSpecialMenu
            || player.timeSinceSwitchingSlots < 0.2f)
        {
            return;
        }
        ShipBuildModeManager.Instance.CancelBuildMode();
        if (!player.activatingItem && !player.waitingToDropItem)
        {
            player.BeginGrabObject();
        }
        InteractTrigger trigger = player.hoveringOverTrigger;
        if (trigger != null && !trigger.holdInteraction && (!player.isHoldingObject || trigger.oneHandedItemAllowed)
            && (!player.twoHanded || (trigger.twoHandedItemAllowed && !trigger.specialCharacterAnimation)) && player.InteractTriggerUseConditionsMet())
        {
            Plugin.Log.LogInfo($"interact: '{trigger.hoverTip}' on {trigger.name}");
            trigger.Interact(player.thisPlayerBody);
        }
    }

    /// <summary>What ClickHoldInteraction does, with our key instead of the (disabled) action.</summary>
    public void Hold(PlayerControllerB player)
    {
        player.isHoldingInteract = held;
        InteractTrigger trigger = player.hoveringOverTrigger;
        if (!held || trigger == null || !trigger.interactable || !trigger.gameObject.activeInHierarchy || !trigger.holdInteraction
            || trigger.currentCooldownValue > 0f || (player.isHoldingObject && !trigger.oneHandedItemAllowed)
            || (player.twoHanded && !trigger.twoHandedItemAllowed) || player.isGrabbingObjectAnimation || player.isTypingChat
            || (player.inSpecialInteractAnimation && !trigger.allowUseWhileInAnimation) || player.throwingObject)
        {
            HUDManager.Instance.holdFillAmount = 0f;
            player.previousHoveringOverTrigger?.StopInteraction();
            trigger?.StopInteraction();
        }
        else if (!HUDManager.Instance.HoldInteractionFill(trigger.timeToHold, trigger.timeToHoldSpeedMultiplier))
        {
            trigger.HoldInteractNotFilled();
        }
        else
        {
            trigger.Interact(player.thisPlayerBody);
        }
    }
}
