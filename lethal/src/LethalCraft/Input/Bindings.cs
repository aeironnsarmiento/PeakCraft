using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace LethalCraft.Input;

/// <summary>
/// Reads Lethal Company's own rebindable actions straight from their controls, so they keep
/// working while the action asset is disabled (Minecraft has the input). Also knows which keys
/// and mouse buttons those actions use, so the bridge keeps them away from Minecraft.
/// </summary>
internal sealed class Bindings
{
    /// <summary>Actions the host keeps while Minecraft owns the body.</summary>
    public static readonly string[] Reserved = { "Interact", "OpenMenu" };

    private readonly Dictionary<string, InputAction?> actions = new();
    private readonly HashSet<Key> reservedKeys = new();
    private readonly HashSet<int> reservedButtons = new(); // SDL numbers
    private float refreshedAt = -10f;

    public bool IsKeyReserved(Key key) => reservedKeys.Contains(key);
    public bool IsButtonReserved(int sdlButton) => reservedButtons.Contains(sdlButton);

    public InputAction? Action(string name)
    {
        if (!actions.TryGetValue(name, out InputAction? action) || action == null)
        {
            action = InputSystem.actions != null ? InputSystem.actions.FindAction(name) : null;
            actions[name] = action;
        }
        return action;
    }

    /// <summary>True while any control bound to the action is pressed, whether or not the action is enabled.</summary>
    public bool IsPressed(string name)
    {
        InputAction? action = Action(name);
        if (action == null)
        {
            return false;
        }
        foreach (InputControl control in action.controls)
        {
            if (control is ButtonControl button && button.isPressed)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The mod's default for Interact is the right mouse button (Minecraft's "use", which the
    /// plugin arbitrates), applied as a binding override only while the keyboard binding is still
    /// the game's own default (E). Rebinding in the pause menu wins; "reset to default" brings it back.
    /// </summary>
    private void ApplyDefaults()
    {
        InputAction? interact = Action("Interact");
        if (interact == null)
        {
            return;
        }
        for (int i = 0; i < interact.bindings.Count; i++)
        {
            InputBinding binding = interact.bindings[i];
            if (binding.path == "<Keyboard>/e" && string.IsNullOrEmpty(binding.overridePath))
            {
                interact.ApplyBindingOverride(i, "<Mouse>/rightButton");
                Plugin.Log.LogInfo("bindings: Interact defaults to the right mouse button (rebind it in the pause menu if you like)");
            }
        }
        // Scan on the right mouse button would fight Interact: it moves to the middle button.
        InputAction? scan = Action("PingScan");
        if (scan != null)
        {
            for (int i = 0; i < scan.bindings.Count; i++)
            {
                if (scan.bindings[i].effectivePath == "<Mouse>/rightButton")
                {
                    scan.ApplyBindingOverride(i, "<Mouse>/middleButton");
                    Plugin.Log.LogInfo("bindings: Scan was on the right mouse button; moved to the middle button");
                }
            }
        }
    }

    /// <summary>Re-reads the reserved actions' bindings now and then (the player can rebind in the pause menu).</summary>
    public void Refresh()
    {
        if (Time.unscaledTime - refreshedAt < 2f)
        {
            return;
        }
        refreshedAt = Time.unscaledTime;
        ApplyDefaults();
        reservedKeys.Clear();
        reservedButtons.Clear();
        foreach (string name in Reserved)
        {
            InputAction? action = Action(name);
            if (action == null)
            {
                continue;
            }
            foreach (InputControl control in action.controls)
            {
                switch (control)
                {
                    case KeyControl key:
                        reservedKeys.Add(key.keyCode);
                        break;
                    case ButtonControl button when button.device is Mouse mouse:
                        int sdl = button == mouse.leftButton ? 1 : button == mouse.middleButton ? 2 : button == mouse.rightButton ? 3
                            : button == mouse.backButton ? 4 : button == mouse.forwardButton ? 5 : 0;
                        if (sdl != 0)
                        {
                            reservedButtons.Add(sdl);
                        }
                        break;
                }
            }
        }
    }

    /// <summary>A short name of the interact binding for prompts: "RMB", "E", ...</summary>
    public string InteractName()
    {
        InputAction? action = Action("Interact");
        if (action == null)
        {
            return "?";
        }
        foreach (InputControl control in action.controls)
        {
            if (control.device is Mouse mouse)
            {
                return control == mouse.rightButton ? "RMB" : control == mouse.leftButton ? "LMB" : control == mouse.middleButton ? "MMB" : control.displayName;
            }
            if (control is KeyControl key)
            {
                return key.displayName.ToUpperInvariant();
            }
        }
        return "?";
    }
}
