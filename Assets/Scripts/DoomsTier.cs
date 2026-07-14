/// <summary>
/// DOOMS agent tier flags. Used by InteractableObject, InteractionTransformPoint,
/// and InteractionGraph edges to gate which tiers may interact.
/// Tier int values from DoomsAgentTag (1–4) map to these flags via TierToFlag().
/// </summary>
[System.Flags]
public enum DoomsTier
{
    None       = 0,
    LeadFull   = 1 << 0,   // tier 1
    LeadShadow = 1 << 1,   // tier 2
    NPC        = 1 << 2,   // tier 3
    Skip       = 1 << 3,   // tier 4

    Leads      = LeadFull | LeadShadow,
    All        = LeadFull | LeadShadow | NPC | Skip
}

public static class DoomsTierUtil
{
    /// <summary>
    /// Maps the sequential tier int (1–4) from DoomsAgentTag to the corresponding flag bit.
    /// Returns DoomsTier.None for out-of-range values (including 0 = untagged).
    /// </summary>
    public static DoomsTier TierToFlag(int tier)
    {
        if (tier >= 1 && tier <= 4) return (DoomsTier)(1 << (tier - 1));
        return DoomsTier.None;
    }

    /// <summary>
    /// Check whether a given tier int is allowed by the flags mask.
    /// DoomsTier.None (empty mask) means any tier is allowed.
    /// </summary>
    public static bool IsTierAllowed(DoomsTier mask, int tier)
    {
        if (mask == DoomsTier.None) return true;
        var flag = TierToFlag(tier);
        if (flag == DoomsTier.None) return false; // untagged/out-of-range → fail against non-empty mask
        return (mask & flag) != 0;
    }

    /// <summary>
    /// Convert a legacy int[] (e.g. {1, 2}) to a DoomsTier flags mask.
    /// </summary>
    public static DoomsTier FromIntArray(int[] tiers)
    {
        if (tiers == null || tiers.Length == 0) return DoomsTier.None;
        DoomsTier result = DoomsTier.None;
        for (int i = 0; i < tiers.Length; i++)
            result |= TierToFlag(tiers[i]);
        return result;
    }
}
