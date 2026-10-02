using UnityEngine;

namespace LethalCraft.World;

/// <summary>
/// Which part of the mirror world the Company is in right now. Slot 0 is the ship in orbit (it
/// always parks in the same spot); each moon gets its own slot, 4096 blocks further along X, so
/// what is built on Experimentation is not standing on Vow. The slot only changes while the host
/// holds the player (the ship is moving), and a change asks Minecraft for a fresh teleport.
/// Minecraft also keys its dug cells and water on the slot id (<c>worldId</c>).
/// </summary>
internal static class WorldSlots
{
    private const double Spacing = 4096.0;

    public static int Current { get; private set; } = -1;

    /// <summary>The slot the game is in, or -1 while the ship is between places (keep the current one).</summary>
    public static int Desired()
    {
        StartOfRound round = StartOfRound.Instance;
        if (round == null)
        {
            return 0;
        }
        if (round.shipHasLanded)
        {
            return Mathf.Clamp(round.currentLevelID, 0, 14) + 1;
        }
        if (round.inShipPhase && !round.shipIsLeaving)
        {
            return 0;
        }
        return -1;
    }

    /// <summary>Applies the slot; true when it changed (the caller resets collision and repositions blocks).</summary>
    public static bool Apply(int slot)
    {
        if (slot < 0 || slot == Current)
        {
            return false;
        }
        Current = slot;
        Coords.OriginX = Spacing * slot;
        Coords.OriginZ = 0.0;
        Plugin.Log.LogInfo($"world: slot {slot} (origin x {Coords.OriginX})");
        return true;
    }

    public static uint WorldId => (uint)Mathf.Max(Current, 0) + 1;
}
