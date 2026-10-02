using GameNetcodeStuff;
using LethalCraft.Link;
using LethalCraft.Player;
using LethalCraft.World;
using Unity.Netcode;
using UnityEngine;

namespace LethalCraft.Dev;

/// <summary>Dev harness commands that stand in for the keyboard and mouse (see DevHarness).</summary>
internal static class DevCommands
{
    public static void Register(Core plugin, DevHarness dev)
    {
        HostLink link = plugin.Link;
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
            PlayerControllerB? p = Ownership.LocalPlayer;
            if (p != null)
            {
                if (p.quickMenuManager.isMenuOpen)
                {
                    p.quickMenuManager.CloseQuickMenu();
                }
                else
                {
                    p.quickMenuManager.OpenQuickMenu();
                }
            }
        });
        // tp <unity x> <y> <z>: put the scout there (the host's own teleport; Minecraft follows).
        dev.Register("tp", a =>
        {
            Ownership.LocalPlayer?.TeleportPlayer(new Vector3(DevHarness.F(a[0]), DevHarness.F(a[1]), DevHarness.F(a[2])));
        });
        // land: pull the lever; leave: pull it again. moon <level id>: route the ship.
        dev.Register("land", _ => StartOfRound.Instance.StartGame());
        dev.Register("leave", _ => StartOfRound.Instance.EndGameServerRpc((int)(Ownership.LocalPlayer?.playerClientId ?? 0)));
        dev.Register("moon", a => StartOfRound.Instance.ChangeLevelServerRpc(int.Parse(a[0]), Object.FindObjectOfType<Terminal>().groupCredits));
        // give <item name words>: spawn an item from the game's list at the scout's feet (host only).
        dev.Register("give", a =>
        {
            string wanted = string.Join(" ", a).ToLowerInvariant();
            PlayerControllerB? p = Ownership.LocalPlayer;
            if (p == null)
            {
                return;
            }
            foreach (Item item in StartOfRound.Instance.allItemsList.itemsList)
            {
                if (item.itemName.ToLowerInvariant().Contains(wanted) && item.spawnPrefab != null)
                {
                    GameObject obj = Object.Instantiate(item.spawnPrefab, p.transform.position + p.transform.forward * 1.5f + Vector3.up * 0.5f, Quaternion.identity, StartOfRound.Instance.propsContainer);
                    GrabbableObject grabbable = obj.GetComponent<GrabbableObject>();
                    grabbable.fallTime = 0f;
                    obj.GetComponent<NetworkObject>().Spawn();
                    if (item.isScrap)
                    {
                        grabbable.SetScrapValue(Random.Range(item.minValue, item.maxValue));
                    }
                    Plugin.Log.LogInfo($"dev: spawned '{item.itemName}'");
                    return;
                }
            }
            Plugin.Log.LogWarning($"dev: no item matching '{wanted}'");
        });
        // find <type name>: where are the host's objects of that component type?
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
        // res <width> <height> [fullscreen mode]: change the resolution (the overlay must follow).
        dev.Register("res", a =>
        {
            Plugin.Log.LogInfo($"dev: resolution was {Screen.width}x{Screen.height} mode {(int)Screen.fullScreenMode}");
            Screen.SetResolution(int.Parse(a[0]), int.Parse(a[1]), a.Length > 2 ? (FullScreenMode)int.Parse(a[2]) : FullScreenMode.Windowed);
        });
        dev.Register("where", _ =>
        {
            PlayerControllerB? p = Ownership.LocalPlayer;
            if (p == null)
            {
                Plugin.Log.LogInfo("dev: no local scout");
                return;
            }
            Vector3 feet = Follower.HostFeet(p);
            Coords.ToMc(feet, out double x, out double y, out double z);
            Proto.GuestState g = plugin.Guest;
            bool hit = Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 50f, StartOfRound.Instance.walkableSurfacesMask, QueryTriggerInteraction.Ignore);
            Plugin.Log.LogInfo($"dev: ground under the feet at unity y {(hit ? ground.point.y : float.NaN):F3} = mc y {(hit ? ground.point.y / Coords.Scale + Coords.YOffset : double.NaN):F3} ({(hit ? ground.collider.name : "none")}); camera mode {g.cameraMode} distance {g.cameraDistance:F2}");
            Plugin.Log.LogInfo($"dev: state {plugin.Ownership.State}; scout feet {feet:F3} = mc ({x:F3}, {y:F3}, {z:F3}); Minecraft feet ({g.x:F3}, {g.y:F3}, {g.z:F3}) yaw {g.yaw:F1} pitch {g.pitch:F1} flags 0x{g.flags:X}; look yaw {plugin.Camera.Yaw:F1} pitch {plugin.Camera.Pitch:F1}; camera {p.gameplayCamera.transform.position:F3}; controller {p.thisController.enabled}; slot {WorldSlots.Current}");
        });
    }
}
