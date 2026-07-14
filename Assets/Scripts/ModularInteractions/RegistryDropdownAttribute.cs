using UnityEngine;

namespace MLA_SIM
{
    /// <summary>
    /// Registry types available for dropdown resolution.
    /// Faction/InteractionPoint/Scene/Prop are DOOMS-specific registries;
    /// when the DOOMS add-on is absent the drawer falls back to a plain text field.
    /// ContextTag/Action/Item/ObjectArchetype/AnimationSequence/AnimationState are
    /// backed by InteractableCatalog and the core animation registries — available
    /// in all projects regardless of DOOMS.
    /// </summary>
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
        ObjectId         = 10,  // A6.5: backed by InteractableCatalog.registeredObjectIds
        ObjectState      = 11   // A7: canonical object states + catalog custom list
    }

    /// <summary>
    /// Marks a string field as registry-backed so the Inspector renders a
    /// dropdown instead of a plain text box.  Works with or without the DOOMS
    /// add-on; DOOMS-specific registry types (Faction, Scene, etc.) fall back
    /// to a text field when the DOOMS folder is absent.
    /// </summary>
    public class RegistryDropdownAttribute : PropertyAttribute
    {
        public readonly RegistryType registryType;

        public RegistryDropdownAttribute(RegistryType type)
        {
            registryType = type;
        }
    }
}
