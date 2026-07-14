#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using MLA_SIM.Dooms.Registries;

namespace MLA_SIM.EditorTools
{
    /// <summary>
    /// Single source of truth for registry option lists used by both the standard
    /// Unity inspector and the NodeCanvas canvas drawers.
    /// </summary>
    public static class SogRegistryProvider
    {
        public static string[] GetOptions(RegistryType type)
        {
            return GetOptionsInternal((int)type);
        }

        public static string[] GetOptions(MLA_SIM.Dooms.RegistryType type)
        {
            return GetOptionsInternal((int)type);
        }

        private static string[] GetOptionsInternal(int registryType)
        {
            switch ((RegistryType)registryType)
            {
                case RegistryType.Faction:
                    return GetFactionIds();
                case RegistryType.InteractionPoint:
                    return GetInteractionPointTags();
                case RegistryType.Scene:
                    return GetSceneIds();
                case RegistryType.AnimationState:
                    return GetAnimatorStateNames();
                case RegistryType.AnimationSequence:
                    return GetSequenceIds();
                case RegistryType.Prop:
                    return GetPropIds();
                case RegistryType.ContextTag:
                    return GetContextTags();
                case RegistryType.Action:
                    return GetActionVocabulary();
                case RegistryType.Item:
                    return GetItemIds();
                case RegistryType.ObjectArchetype:
                    return GetArchetypeIds();
                case RegistryType.ObjectId:
                    return GetObjectIds();
                case RegistryType.ObjectState:
                    return GetObjectStates();
                default:
                    return Array.Empty<string>();
            }
        }

        private static string[] GetFactionIds()
        {
            var registry = FactionRegistrySO.Instance;
            if (registry?.factions == null) return Array.Empty<string>();
            return registry.factions.Where(f => f != null && !string.IsNullOrWhiteSpace(f.factionId))
                .Select(f => f.factionId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] GetInteractionPointTags()
        {
            var registry = InteractionPointRegistrySO.Instance;
            if (registry?.pointTags == null) return Array.Empty<string>();
            return registry.pointTags.Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] GetSceneIds()
        {
            var registry = SceneRegistrySO.Instance;
            if (registry?.scenes == null) return Array.Empty<string>();
            return registry.scenes.Where(s => s != null && !string.IsNullOrWhiteSpace(s.sceneId))
                .Select(s => s.sceneId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] GetAnimatorStateNames()
        {
            var registry = AnimationStateRegistrySO.Instance;
            if (registry?.states == null) return Array.Empty<string>();
            return registry.states.Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] GetSequenceIds()
        {
            var registry = AnimationSequenceRegistry.Instance;
            if (registry?.sequences == null) return Array.Empty<string>();
            return registry.sequences.Where(s => s != null && !string.IsNullOrWhiteSpace(s.sequenceId))
                .Select(s => s.sequenceId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] GetPropIds()
        {
            var registry = PropRegistrySO.Instance;
            if (registry?.props == null) return Array.Empty<string>();
            return registry.props.Where(p => p != null && !string.IsNullOrWhiteSpace(p.propId))
                .Select(p => p.propId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] GetContextTags()
        {
            var catalog = InteractableCatalog.Instance;
            if (catalog == null) return Array.Empty<string>();
            return catalog.GetContextTags();
        }

        private static string[] GetActionVocabulary()
        {
            var catalog = InteractableCatalog.Instance;
            if (catalog == null) return Array.Empty<string>();
            return catalog.GetActionVocabulary();
        }

        private static string[] GetItemIds()
        {
            var catalog = InteractableCatalog.Instance;
            if (catalog == null) return Array.Empty<string>();
            return catalog.GetItemIds();
        }

        private static string[] GetArchetypeIds()
        {
            var catalog = InteractableCatalog.Instance;
            if (catalog == null) return Array.Empty<string>();
            return catalog.GetArchetypeIds();
        }

        private static string[] GetObjectIds()
        {
            var catalog = InteractableCatalog.Instance;
            if (catalog == null) return Array.Empty<string>();
            return catalog.GetRegisteredObjectIds();
        }

        private static string[] GetObjectStates()
        {
            var catalog = InteractableCatalog.Instance;
            if (catalog != null)
            {
                var ids = catalog.GetObjectStateIds();
                if (ids != null && ids.Length > 0)
                    return ids;
            }

            return Enum.GetNames(typeof(InteractableObject.ObjectState))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
#endif
