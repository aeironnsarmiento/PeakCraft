using System.Collections.Generic;
using BepInEx.Configuration;
using GameNetcodeStuff;
using LethalCraft.Link;
using LethalCraft.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace LethalCraft.Input;

/// <summary>
/// Reads raw keyboard and mouse state from Unity and re-encodes it for Minecraft's input ring:
/// keys as SDL scancodes, mouse buttons as SDL numbers (1 left, 2 middle, 3 right, 4/5 extra).
/// The host integrates the mouse into the authoritative look, as in SkyCraft.
///
/// While Minecraft owns the body, Lethal Company's action asset is disabled so none of its
/// gameplay actions fire; the plugin reads the reserved ones (Interact, OpenMenu) from their
/// bindings itself. With the mod's default bindings Interact sits on the right mouse button,
/// shared with Minecraft's "use": a press goes to whichever game has the nearer target.
/// Mirrors skse/src/Input.cpp; the receiving end is fabric/.../client/InputBridge.java.
/// </summary>
internal sealed class InputBridge
{
    private readonly HostLink link;
    private readonly CameraDriver camera;
    private readonly Interact interact;
    private readonly Bindings bindings;
    private readonly ConfigEntry<Key> menuKey;

    private readonly HashSet<Key> held = new();
    private readonly List<Key> scratch = new();
    private readonly bool[] buttons = new bool[6];
    private readonly Queue<char> text = new();
    private Keyboard? textSource;
    private bool routing;
    private bool hostActionsDisabled;
    private bool screenOpen;
    private float cursorX, cursorY;
    private float sensitivity = 0.5f;
    private Key repeatKey = Key.None;
    private float repeatAt;
    private int interactLatch; // 0 none, 1 host has the interact button, 2 Minecraft has it

    public InputBridge(HostLink link, CameraDriver camera, Interact interact, Bindings bindings, ConfigFile config)
    {
        this.link = link;
        this.camera = camera;
        this.interact = interact;
        this.bindings = bindings;
        menuKey = config.Bind("Keys", "MinecraftMenu", Key.Backquote,
            "Opens Minecraft's pause menu. Esc opens Lethal Company's menu, or closes an open Minecraft screen first.");
    }

    /// <summary>True while a Minecraft screen (inventory, chat, pause) is open: the overlay cursor is shown.</summary>
    public bool ScreenOpen => routing && screenOpen;
    public Vector2 Cursor => new(cursorX, cursorY);
    public string InteractKeyName => bindings.InteractName();

    public void Update(Ownership ownership, in Proto.GuestState guest)
    {
        bindings.Refresh();
        SetHostActions(ownership.HostInput);

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        PlayerControllerB? player = Ownership.LocalPlayer;
        bool route = ownership.State == Owner.GuestOwns && Application.isFocused && keyboard != null && mouse != null;
        if (route != routing)
        {
            routing = route;
            ReleaseAll();
            Plugin.Log.LogInfo(route ? "input: keyboard and mouse go to Minecraft" : "input: released (release-all sent)");
        }
        if (!route)
        {
            interact.Update(false, null);
            interactLatch = 0;
            return;
        }
        if (guest.sensitivity > 0f)
        {
            sensitivity = guest.sensitivity;
        }
        HookText(keyboard!);

        bool nowOpen = (guest.flags & (uint)Proto.GuestFlags.ScreenOpen) != 0;
        if (nowOpen && !screenOpen)
        {
            cursorX = Screen.width * 0.5f;
            cursorY = Screen.height * 0.5f;
            link.PushInput(Proto.InputType.Cursor, 0, (int)cursorX, (int)cursorY);
        }
        screenOpen = nowOpen;
        SetHostActions(ownership.HostInput);

        Vector2 delta = mouse!.delta.ReadValue();
        if (screenOpen)
        {
            if (delta != Vector2.zero)
            {
                // Overlay pixels, origin top left; Unity's mouse delta has Y up.
                cursorX = Mathf.Clamp(cursorX + delta.x, 0f, Screen.width - 1);
                cursorY = Mathf.Clamp(cursorY - delta.y, 0f, Screen.height - 1);
                link.PushInput(Proto.InputType.Cursor, 0, (int)cursorX, (int)cursorY);
            }
            while (text.Count > 0)
            {
                link.PushInput(Proto.InputType.Text, 0, text.Dequeue());
            }
        }
        else
        {
            text.Clear();
            // Minecraft's own mouse curve (MouseHandler.turnPlayer): (s * 0.6 + 0.2)^3 * 8, times 0.15 degrees.
            float s = sensitivity * 0.6f + 0.2f;
            float factor = s * s * s * 8f * 0.15f;
            camera.Yaw = Mathf.DeltaAngle(0f, camera.Yaw + delta.x * factor);
            camera.Pitch = Mathf.Clamp(camera.Pitch - delta.y * factor, -90f, 90f);
        }

        bool interactDown = PumpKeys(keyboard!);
        interactDown |= PumpButtons(mouse, player);
        float scroll = mouse.scroll.ReadValue().y;
        if (scroll != 0f)
        {
            link.PushInput(Proto.InputType.Scroll, 0, scroll > 0f ? 120 : -120);
        }
        interact.Update(interactDown, player);
    }

    /// <summary>
    /// Lethal Company's actions: off while Minecraft has the input, on otherwise. OpenMenu (Esc)
    /// stays on so the pause menu always opens, except while a Minecraft screen is up (Esc closes
    /// that first).
    /// </summary>
    private void SetHostActions(bool enabled)
    {
        InputActionAsset actions = InputSystem.actions;
        if (actions == null)
        {
            return;
        }
        bool disable = !enabled;
        if (disable != hostActionsDisabled)
        {
            hostActionsDisabled = disable;
            foreach (InputAction action in actions)
            {
                if (action.name == "OpenMenu")
                {
                    continue;
                }
                if (disable)
                {
                    action.Disable();
                }
                else
                {
                    action.Enable();
                }
            }
            Plugin.Log.LogInfo(disable ? "input: Lethal Company's actions disabled (OpenMenu stays)" : "input: Lethal Company's actions enabled");
        }
        InputAction? openMenu = bindings.Action("OpenMenu");
        if (openMenu != null)
        {
            bool wantOpenMenu = enabled || !screenOpen;
            if (wantOpenMenu && !openMenu.enabled)
            {
                openMenu.Enable();
            }
            else if (!wantOpenMenu && openMenu.enabled)
            {
                openMenu.Disable();
            }
        }
    }

    /// <summary>Returns true while a key bound to Interact is down.</summary>
    private bool PumpKeys(Keyboard keyboard)
    {
        bool interactDown = false;
        foreach (KeyControl control in keyboard.allKeys)
        {
            if (control == null)
            {
                continue;
            }
            Key key = control.keyCode;
            bool down = control.isPressed;
            // With a Minecraft screen up every key is Minecraft's, so typing works and Esc closes it.
            if (!screenOpen)
            {
                if (key == Key.Escape)
                {
                    continue; // the host's pause menu, through its own (enabled-again) action
                }
                if (bindings.IsKeyReserved(key))
                {
                    interactDown |= down;
                    continue;
                }
                if (key == menuKey.Value)
                {
                    if (down && held.Add(key))
                    {
                        ReleaseKeys();
                        held.Add(key);
                        link.PushInput(Proto.InputType.OpenMenu);
                    }
                    else if (!down)
                    {
                        held.Remove(key);
                    }
                    continue;
                }
            }
            if (!Scancodes.FromKey.TryGetValue(key, out ushort scancode))
            {
                continue;
            }
            if (down && held.Add(key))
            {
                link.PushInput(Proto.InputType.Key, scancode, 1);
                repeatKey = key;
                repeatAt = Time.unscaledTime + 0.4f;
            }
            else if (!down && held.Remove(key))
            {
                link.PushInput(Proto.InputType.Key, scancode, 0);
                if (repeatKey == key)
                {
                    repeatKey = Key.None;
                }
            }
        }
        // Key repeat for Minecraft's text fields (held backspace, arrows): a second "down" is a repeat there.
        if (screenOpen && repeatKey != Key.None && Time.unscaledTime >= repeatAt && Scancodes.FromKey.TryGetValue(repeatKey, out ushort repeat))
        {
            repeatAt = Time.unscaledTime + 0.04f;
            link.PushInput(Proto.InputType.Key, repeat, 1);
        }
        return interactDown;
    }

    /// <summary>Returns true while a mouse button bound to Interact is down and the host claimed it.</summary>
    private bool PumpButtons(Mouse mouse, PlayerControllerB? player)
    {
        bool interactDown = false;
        for (int sdl = 1; sdl <= 5; sdl++)
        {
            ButtonControl control = sdl switch
            {
                1 => mouse.leftButton,
                2 => mouse.middleButton,
                3 => mouse.rightButton,
                4 => mouse.backButton,
                _ => mouse.forwardButton,
            };
            bool down = control != null && control.isPressed;
            bool reserved = !screenOpen && bindings.IsButtonReserved(sdl);
            if (reserved)
            {
                // Shared button: decided on the press and latched until release, so a hold stays on one side.
                if (down && interactLatch == 0)
                {
                    interactLatch = player != null && Interact.HasTarget(player, out _) ? 1 : 2;
                }
                else if (!down)
                {
                    interactLatch = 0;
                }
                if (interactLatch == 1)
                {
                    interactDown = true;
                    down = false; // Minecraft sees it released
                }
            }
            if (down != buttons[sdl])
            {
                buttons[sdl] = down;
                link.PushInput(Proto.InputType.MouseButton, (ushort)sdl, down ? 1 : 0);
            }
        }
        return interactDown;
    }

    private void HookText(Keyboard keyboard)
    {
        if (textSource == keyboard)
        {
            return;
        }
        if (textSource != null)
        {
            textSource.onTextInput -= OnText;
        }
        textSource = keyboard;
        keyboard.onTextInput += OnText;
    }

    private void OnText(char c)
    {
        if (routing && screenOpen && c >= ' ' && c != 127 && text.Count < 256)
        {
            text.Enqueue(c);
        }
    }

    private void ReleaseKeys()
    {
        scratch.Clear();
        scratch.AddRange(held);
        foreach (Key key in scratch)
        {
            if (Scancodes.FromKey.TryGetValue(key, out ushort scancode))
            {
                link.PushInput(Proto.InputType.Key, scancode, 0);
            }
        }
        held.Clear();
        repeatKey = Key.None;
    }

    /// <summary>Input focus left Minecraft (host menu, cutscene, alt-tab, link loss): lift everything.</summary>
    public void ReleaseAll()
    {
        held.Clear();
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i] = false;
        }
        text.Clear();
        repeatKey = Key.None;
        screenOpen = false;
        interactLatch = 0;
        link.PushInput(Proto.InputType.ReleaseAll);
    }

    /// <summary>Plugin shutdown: never leave the host's actions off.</summary>
    public void Restore() => SetHostActions(true);
}
