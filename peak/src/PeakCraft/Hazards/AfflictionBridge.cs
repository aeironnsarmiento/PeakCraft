using BepInEx.Configuration;
using PeakCraft.Link;
using PeakCraft.Player;
using UnityEngine;
using STATUSTYPE = CharacterAfflictions.STATUSTYPE;

namespace PeakCraft.Hazards;

/// <summary>
/// PEAK's hazards as Minecraft damage (KTD12). While Minecraft owns the body, whatever PEAK tries
/// to add to the character's afflictions is collected here instead of applied, so PEAK never
/// knocks the character out on its own; at most every half second the collected amount goes to
/// Minecraft as one hurt event. Minecraft health is the only health: in Creative it ignores hurt
/// events, which is the whole Survival toggle.
/// </summary>
internal sealed class AfflictionBridge
{
    // Minecraft drops repeat hits inside its half-second invulnerability window.
    private const float SendInterval = 0.5f;
    // A full PEAK status bar (1.0) is a dead scout: 20 Minecraft health.
    private const float HealthPerFullBar = 20f;
    // The Fabric mod divides the sent value by 100, then by 5 (Skyrim's damage scale).
    private const float WireScale = 500f;

    private readonly PeakLink link;
    private readonly ConfigEntry<float> scale;
    private float pending;
    private float lastSend;
    private float lastLog;
    private float loggedDamage;

    public AfflictionBridge(PeakLink link, ConfigFile config)
    {
        this.link = link;
        scale = config.Bind("Hazards", "DamageScale", 3f,
            "Multiplies the Minecraft damage PEAK's hazards deal in Survival. At 1, filling PEAK's whole status bar costs 10 hearts, which Minecraft's natural healing outruns; 3 makes the fog kill in under a minute.");
    }

    /// <summary>PEAK's hunger and weight never cross over; everything that hurts does.</summary>
    private static bool Hurts(STATUSTYPE type) => type is STATUSTYPE.Cold or STATUSTYPE.Hot or STATUSTYPE.Poison
        or STATUSTYPE.Thorns or STATUSTYPE.Injury or STATUSTYPE.Spores or STATUSTYPE.Drowsy;

    /// <summary>From the CharacterAfflictions.AddStatus prefix. True: PEAK's own handling is skipped.</summary>
    public bool Intercept(CharacterAfflictions afflictions, STATUSTYPE type, float amount)
    {
        if (!Plugin.Instance.Ownership.BodyFollows || afflictions.character != Character.localCharacter)
        {
            return false;
        }
        if (amount > 0f && Hurts(type))
        {
            pending += amount;
        }
        return true;
    }

    /// <summary>Something in PEAK kills outright (a kill plane): more damage than any health bar holds.</summary>
    public void Lethal() => pending += 100f;

    public void Update(Ownership ownership)
    {
        Character character = Character.localCharacter;
        if (!ownership.BodyFollows || character == null)
        {
            pending = 0f;
            return;
        }
        // Statuses PEAK sets directly (carry weight, stuck thorns) must not add up to a pass-out either.
        float[] statuses = character.refs.afflictions.currentStatuses;
        for (int i = 0; i < statuses.Length; i++)
        {
            statuses[i] = 0f;
        }

        if (pending <= 0f || Time.unscaledTime - lastSend < SendInterval)
        {
            return;
        }
        float damage = pending * HealthPerFullBar * scale.Value;
        pending = 0f;
        lastSend = Time.unscaledTime;
        link.PushInput(Proto.InputType.Hurt, Proto.HurtOther, Mathf.Min((int)(damage * WireScale), 1000000));
        loggedDamage += damage;
        if (Time.unscaledTime - lastLog > 5f)
        {
            Plugin.Log.LogInfo($"hazards: {loggedDamage:F2} Minecraft damage sent in the last {Time.unscaledTime - lastLog:F0} s (Creative ignores it)");
            lastLog = Time.unscaledTime;
            loggedDamage = 0f;
        }
    }
}
