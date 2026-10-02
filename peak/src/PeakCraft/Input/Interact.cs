using UnityEngine;

namespace PeakCraft.Input;

/// <summary>
/// The one key that still reaches PEAK while Minecraft owns the body. It stands in for PEAK's own
/// interact action, so PEAK's Interaction component does the targeting (from the camera) and the
/// hold-to-interact timing exactly as it always does: kiosk, campfire, luggage.
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

    public void Update(bool keyDown)
    {
        bool down = keyDown || Time.unscaledTime < devHoldUntil;
        Pressed = down && !held;
        Released = !down && held;
        held = down;
        if (Pressed)
        {
            IInteractible? target = Interaction.instance != null ? Interaction.instance.bestInteractable : null;
            Plugin.Log.LogInfo(target != null
                ? $"interact: PEAK target '{target.GetTransform().name}' ({target.GetInteractionText()})"
                : "interact: nothing targeted");
        }
    }

    /// <summary>Called from the CharacterInput.Sample postfix.</summary>
    public void Apply(CharacterInput input)
    {
        input.interactWasPressed = Pressed;
        input.interactIsPressed = held;
        input.interactWasReleased = Released;
    }
}
