using UnityEngine;

namespace LethalCraft;

/// <summary>
/// The MonoBehaviour that ticks the plugin. BepInEx's own manager object does not survive Lethal
/// Company's start-up (it is destroyed right after the chainloader finishes), so the plugin runs
/// from a GameObject of its own, marked DontDestroyOnLoad.
/// </summary>
[DefaultExecutionOrder(-1000)]
internal sealed class Driver : MonoBehaviour
{
    private bool first = true;

    private void Update()
    {
        if (first)
        {
            first = false;
            Plugin.Log.LogInfo("driver: first Update");
        }
        Core.Instance?.Update();
    }

    private void OnGUI() => Core.Instance?.OnGUI();

    private void OnDestroy()
    {
        Plugin.Log.LogInfo("driver: destroyed");
        Core.Instance?.Shutdown();
    }
}
