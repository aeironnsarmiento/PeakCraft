using System.Collections.Generic;
using BepInEx.Configuration;
using PeakCraft.Link;
using PeakCraft.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace PeakCraft.Input;

/// <summary>
/// Reads raw keyboard and mouse state from Unity and re-encodes it for Minecraft's input ring:
/// keys as SDL scancodes, mouse buttons as SDL numbers (1 left, 2 middle, 3 right, 4/5 extra).
/// The host integrates the mouse into the authoritative look, as in SkyCraft.
/// Mirrors skse/src/Input.cpp; the receiving end is fabric/.../client/InputBridge.java.
/// </summary>
internal sealed class InputBridge
{
    private readonly PeakLink link;
    private readonly CameraDriver camera;
    private readonly Interact interact;
    private readonly ConfigEntry<Key> interactKey;
    private readonly ConfigEntry<Key> menuKey;

    private readonly HashSet<Key> held = new();
    private readonly List<Key> scratch = new();
    private readonly bool[] buttons = new bool[6];
    private readonly Queue<char> text = new();
    private Keyboard? textSource;
    private bool routing;
    private bool screenOpen;
    private float cursorX, cursorY;
    private float sensitivity = 0.5f;
    private Key repeatKey = Key.None;
    private float repeatAt;

    public InputBridge(PeakLink link, CameraDriver camera, Interact interact, ConfigFile config)
    {
        this.link = link;
        this.camera = camera;
        this.interact = interact;
        // SkyCraft's choices; neither is bound by default in Minecraft, and PEAK's own bindings are off.
        interactKey = config.Bind("Keys", "PeakInteract", Key.G,
            "Triggers PEAK's interact on whatever PEAK targets (kiosk, campfire, luggage). Hold it where PEAK wants a hold.");
        menuKey = config.Bind("Keys", "MinecraftMenu", Key.O,
            "Opens Minecraft's pause menu. Esc opens PEAK's menu, or closes an open Minecraft screen first.");
    }

    /// <summary>True while a Minecraft screen (inventory, chat, pause) is open: the overlay cursor is shown.</summary>
    public bool ScreenOpen => routing && screenOpen;
    public Vector2 Cursor => new(cursorX, cursorY);
    public string InteractKeyName => interactKey.Value.ToString();

    public void Update(Ownership ownership, in Proto.GuestState guest)
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        bool route = ownership.State == Owner.MinecraftOwns && Application.isFocused && keyboard != null && mouse != null;
        if (route != routing)
        {
            routing = route;
            ReleaseAll();
            Plugin.Log.LogInfo(route ? "input: keyboard and mouse go to Minecraft" : "input: released (release-all sent)");
        }
        if (!route)
        {
            interact.Update(false);
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

        PumpKeys(keyboard!);
        PumpButton(1, mouse.leftButton);
        PumpButton(2, mouse.middleButton);
        PumpButton(3, mouse.rightButton);
        PumpButton(4, mouse.backButton);
        PumpButton(5, mouse.forwardButton);
        float scroll = mouse.scroll.ReadValue().y;
        if (scroll != 0f)
        {
            link.PushInput(Proto.InputType.Scroll, 0, scroll > 0f ? 120 : -120);
        }
    }

    private void PumpKeys(Keyboard keyboard)
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
                    continue; // PEAK's pause menu
                }
                if (key == interactKey.Value)
                {
                    interactDown = down;
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
        interact.Update(interactDown);
    }

    private void PumpButton(int sdlButton, ButtonControl control)
    {
        bool down = control != null && control.isPressed;
        if (down != buttons[sdlButton])
        {
            buttons[sdlButton] = down;
            link.PushInput(Proto.InputType.MouseButton, (ushort)sdlButton, down ? 1 : 0);
        }
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

    /// <summary>Input focus left Minecraft (PEAK menu, cutscene, alt-tab, link loss): lift everything.</summary>
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
        link.PushInput(Proto.InputType.ReleaseAll);
    }
}
