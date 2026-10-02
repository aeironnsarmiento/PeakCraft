using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace LethalCraft;

/// <summary>Entry point: builds the <see cref="Core"/> and the GameObject that ticks it (see <see cref="Driver"/>).</summary>
[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;
        Log.LogInfo($"Plugin {Name} {Version} is loaded!");
        var core = new Core(Config, Id);
        if (!core.Enabled)
        {
            return;
        }
        var driver = new GameObject("LethalCraft") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(driver);
        driver.AddComponent<Driver>();
    }
}
