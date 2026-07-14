namespace MLA_SIM.Dooms
{
    /// <summary>
    /// DOOMS compatibility shim. Real definition has moved to MLA_SIM.RegistryDropdownAttribute
    /// (Assets/Scripts/ModularInteractions/RegistryDropdownAttribute.cs).
    /// All DOOMS-internal code continues to compile unchanged via this re-export.
    /// This file can be deleted when the DOOMS add-on is removed.
    /// </summary>

    // Re-declare enum with identical values so DOOMS files using
    // "using MLA_SIM.Dooms;" keep resolving RegistryType.X without changes.
    // Integer values are identical to MLA_SIM.RegistryType — safe to cast.
    public enum RegistryType
    {
        Faction          = 0,
        InteractionPoint = 1,
        Scene            = 2,
        AnimationState   = 3,
        AnimationSequence = 4,
        Prop             = 5,
        ContextTag       = 6,
        Action           = 7,
        Item             = 8,
        ObjectArchetype  = 9,
        ObjectId         = 10,
        ObjectState      = 11
    }

    // Thin subclass so [RegistryDropdown(RegistryType.X)] in DOOMS files compiles
    // and the core drawer (registered with useForChildren=true) handles the GUI.
    public class RegistryDropdownAttribute : MLA_SIM.RegistryDropdownAttribute
    {
        public RegistryDropdownAttribute(RegistryType type)
            : base((MLA_SIM.RegistryType)type) { }
    }
}
