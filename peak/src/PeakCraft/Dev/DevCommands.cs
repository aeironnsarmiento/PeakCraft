using PeakCraft.Link;
using PeakCraft.Player;
using PeakCraft.World;
using UnityEngine;

namespace PeakCraft.Dev;

/// <summary>Dev harness commands that stand in for the keyboard and mouse (see DevHarness).</summary>
internal static class DevCommands
{
    public static void Register(Plugin plugin, DevHarness dev)
    {
        PeakLink link = plugin.Link;
        // key <sdl scancode> <1|0>, mouse <sdl button> <1|0>, scroll <notches>, text <words...>
        dev.Register("key", a => link.PushInput(Proto.InputType.Key, ushort.Parse(a[0]), int.Parse(a[1])));
        dev.Register("mouse", a => link.PushInput(Proto.InputType.MouseButton, ushort.Parse(a[0]), int.Parse(a[1])));
        dev.Register("scroll", a => link.PushInput(Proto.InputType.Scroll, 0, int.Parse(a[0]) * 120));
        dev.Register("cursor", a => link.PushInput(Proto.InputType.Cursor, 0, int.Parse(a[0]), int.Parse(a[1])));
        dev.Register("text", a =>
        {
            foreach (char c in string.Join(" ", a))
            {
                link.PushInput(Proto.InputType.Text, 0, c);
            }
        });
        dev.Register("releaseall", _ => link.PushInput(Proto.InputType.ReleaseAll));
        dev.Register("mcmenu", _ => link.PushInput(Proto.InputType.OpenMenu));
        // look <mc yaw> <mc pitch>
        dev.Register("look", a =>
        {
            plugin.Camera.Yaw = DevHarness.F(a[0]);
            plugin.Camera.Pitch = DevHarness.F(a[1]);
        });
        dev.Register("interact", a => plugin.Interact.DevHold(a.Length > 0 ? DevHarness.F(a[0]) : 0.2f));
        dev.Register("teleport", _ => plugin.Ownership.RequestTeleport());
        dev.Register("pause", _ =>
        {
            if (Character.localCharacter != null)
            {
                Character.localCharacter.input.pauseWasPressed = true;
                GUIManager.instance.UpdatePaused();
            }
        });
        // tp <unity x> <y> <z>: put PEAK's character there and have Minecraft follow.
        dev.Register("tp", a =>
        {
            Character c = Character.localCharacter;
            // PEAK's own warp: the plugin sees it as a cutscene and hands back afterwards.
            c.WarpPlayer(new Vector3(DevHarness.F(a[0]), DevHarness.F(a[1]) + Coords.CenterAboveFeet, DevHarness.F(a[2])), poof: false);
        });
        // find <type name>: where are PEAK's objects of that component type?
        dev.Register("find", a =>
        {
            foreach (MonoBehaviour b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (b.GetType().Name == a[0])
                {
                    Plugin.Log.LogInfo($"dev: {a[0]} '{b.name}' at {b.transform.position:F2} forward {b.transform.forward:F2} active {b.isActiveAndEnabled}");
                }
            }
        });
        dev.Register("progress", _ =>
        {
            MountainProgressHandler handler = Zorro.Core.Singleton<MountainProgressHandler>.Instance;
            foreach (MountainProgressHandler.ProgressPoint point in handler.progressPoints)
            {
                Plugin.Log.LogInfo($"dev: progress point '{point.title}' at {(point.transform != null ? point.transform.position.ToString("F1") : "none")}");
            }
        });
        // fog <size>: shrink PEAK's fog sphere so the player stands in the fog (the real hazard source).
        dev.Register("fog", a =>
        {
            OrbFogHandler fog = Zorro.Core.Singleton<OrbFogHandler>.Instance;
            fog.currentSize = DevHarness.F(a[0]);
            fog.sphere.ENABLE = 1f; // as when the fog has started to move
            Plugin.Log.LogInfo($"dev: fog size {fog.currentSize}, player in fog: {Character.localCharacter.data.isInFog}");
        });
        // res <width> <height> [fullscreen mode]: change PEAK's resolution (the overlay must follow).
        dev.Register("res", a =>
        {
            Plugin.Log.LogInfo($"dev: resolution was {Screen.width}x{Screen.height} mode {(int)Screen.fullScreenMode}");
            Screen.SetResolution(int.Parse(a[0]), int.Parse(a[1]), a.Length > 2 ? (FullScreenMode)int.Parse(a[2]) : FullScreenMode.Windowed);
        });
        dev.Register("endreturn", _ => GUIManager.instance.endScreen.ReturnToAirport());
        dev.Register("board", _ => GUIManager.instance.boardingPass.StartGame());
        dev.Register("closewindows", _ => MenuWindow.CloseAllWindows());
        dev.Register("where", _ =>
        {
            Character c = Character.localCharacter;
            if (c == null)
            {
                Plugin.Log.LogInfo("dev: no local character");
                return;
            }
            Vector3 feet = Follower.PeakFeet(c);
            Coords.ToMc(feet, out double x, out double y, out double z);
            Proto.GuestState g = plugin.Guest;
            bool hit = Physics.Raycast(feet + Vector3.up * 1.5f, Vector3.down, out RaycastHit ground, 50f, LayerMask.GetMask("Terrain", "Map", "Default"), QueryTriggerInteraction.Ignore);
            Plugin.Log.LogInfo($"dev: PEAK ground under the feet at unity y {(hit ? ground.point.y : float.NaN):F3} = mc y {(hit ? ground.point.y + Coords.YOffset : double.NaN):F3} ({(hit ? ground.collider.name : "none")}); camera mode {g.cameraMode} distance {g.cameraDistance:F2}");
            Plugin.Log.LogInfo($"dev: state {plugin.Ownership.State}; PEAK feet {feet:F3} = mc ({x:F3}, {y:F3}, {z:F3}); Minecraft feet ({g.x:F3}, {g.y:F3}, {g.z:F3}) yaw {g.yaw:F1} pitch {g.pitch:F1} flags 0x{g.flags:X}; look yaw {plugin.Camera.Yaw:F1} pitch {plugin.Camera.Pitch:F1}; camera {MainCamera.instance.transform.position:F3}; kinematic {c.data.isKinecmatic}; stamina {c.data.currentStamina:F2}");
        });
    }
}
