using BepInEx.Configuration;
using LethalCraft.Link;
using LethalCraft.Player;
using UnityEngine;

namespace LethalCraft.Hazards;

/// <summary>
/// Lethal Company's hazards as Minecraft damage. Every turret shot, landmine, spike trap and
/// monster swipe ends in PlayerControllerB.DamagePlayer (outright kills in KillPlayer). While
/// Minecraft owns the body the amount is collected here instead of applied; at most every half
/// second it goes to Minecraft as one hurt event. Minecraft health is the only health: 100 scout
/// health is 20 Minecraft health, times a config scale. Fall damage is dropped (Minecraft does its own).
/// </summary>
internal sealed class DamageBridge
{
    // Minecraft drops repeat hits inside its half-second invulnerability window.
    private const float SendInterval = 0.5f;
    // The Fabric mod divides the sent value by 100, then by 5 (Skyrim's damage scale): one scout
    // health point of damage is 0.2 Minecraft health, which on the wire is exactly 100.
    private const int WirePerHostHealth = 100;

    private readonly HostLink link;
    private readonly ConfigEntry<float> scale;
    private float pending;
    private float lastSend;
    private float lastLog;
    private float loggedDamage;

    public DamageBridge(HostLink link, ConfigFile config)
    {
        this.link = link;
        scale = config.Bind("Hazards", "DamageScale", 1f,
            "Multiplies the Minecraft damage Lethal Company's hazards deal in Survival. At 1, 100 scout health is 10 hearts.");
    }

    /// <summary>From the DamagePlayer prefix. The host's own handling is skipped.</summary>
    public void Intercept(int damage, CauseOfDeath cause, bool fallDamage)
    {
        if (fallDamage || damage <= 0)
        {
            return;
        }
        pending += damage;
    }

    /// <summary>Something in Lethal Company kills outright: more damage than any health bar holds.</summary>
    public void Lethal(CauseOfDeath cause)
    {
        Plugin.Log.LogInfo($"hazards: Lethal Company kill ({cause}) becomes lethal Minecraft damage");
        pending += 1000f;
    }

    public void Update(Ownership ownership)
    {
        if (!ownership.BodyFollows)
        {
            pending = 0f;
            return;
        }
        if (pending <= 0f || Time.unscaledTime - lastSend < SendInterval)
        {
            return;
        }
        float damage = pending * scale.Value;
        pending = 0f;
        lastSend = Time.unscaledTime;
        link.PushInput(Proto.InputType.Hurt, Proto.HurtOther, Mathf.Min((int)(damage * WirePerHostHealth), 1000000));
        loggedDamage += damage / 5f;
        if (Time.unscaledTime - lastLog > 5f)
        {
            Plugin.Log.LogInfo($"hazards: {loggedDamage:F2} Minecraft damage sent in the last {Time.unscaledTime - lastLog:F0} s (Creative ignores it)");
            lastLog = Time.unscaledTime;
            loggedDamage = 0f;
        }
    }
}
