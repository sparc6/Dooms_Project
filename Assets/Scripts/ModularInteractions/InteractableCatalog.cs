using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace MLA_SIM
{
    /// <summary>
    /// Central catalog for defining reusable interactable archetypes and items.
    /// This reduces manual entry on individual InteractableObjects in the scene.
    /// </summary>
    [CreateAssetMenu(fileName = "InteractableCatalog", menuName = "MLA SIM/Interactable Catalog", order = 3)]
    public class InteractableCatalog : ScriptableObject
    {
        [Header("Interactable Archetypes")]
        public List<InteractableArchetype> archetypes = new List<InteractableArchetype>();

        [Header("Item Definitions")]
        public List<InventoryItemDefinition> items = new List<InventoryItemDefinition>();

        // ── A4.3: catalog-as-registry vocabularies ───────────────────────────
        [Header("Vocabulary (A4.3 — dropdown sources)")]
        [Tooltip("Known context tags/groups selectable on InteractableObject.contextTags via dropdown.")]
        public List<string> contextTags = new List<string>();

        [Tooltip("Known interaction action verbs selectable on ActionAffordance.actionName via dropdown. " +
                 "Keep aligned with the backend coroutines.json action vocabulary.")]
        public List<string> actionVocabulary = new List<string>
        {
            "InteractWith", "Pickup", "Fix", "Destroy", "SleepAt", "HideAt", "TalkTo", "Use"
        };

        [Header("Custom Object States (A7 — SOG state dropdown source)")]
        [Tooltip("Extra state ids beyond the built-in ObjectState enum (Usable/Broken/Occupied/Locked).")]
        public List<string> customObjectStates = new List<string>();

        [Header("Scene Object Registry (A6.5 — auto-populated from SceneObjectGraph)")]
        [Tooltip("All objectIds present in the active SceneObjectGraph. " +
                 "Injected into AI prompts and used by RegistryType.ObjectId dropdowns. " +
                 "Sync via: MLA SIM / Setup / Sync Catalog from Scene Object Graph.")]
        public List<string> registeredObjectIds = new List<string>();

        private static InteractableCatalog _instance;

        /// <summary>
        /// A4.3: best-effort shared catalog used as the dropdown registry. In the
        /// editor it resolves the first InteractableCatalog asset; at runtime it
        /// falls back to any loaded instance. Returns null if none exist.
        /// </summary>
        public static InteractableCatalog Instance
        {
            get
            {
#if UNITY_EDITOR
                var guids = UnityEditor.AssetDatabase.FindAssets("t:InteractableCatalog");
                if (guids != null && guids.Length > 0)
                {
                    var c = UnityEditor.AssetDatabase.LoadAssetAtPath<InteractableCatalog>(
                        UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
                    if (c != null) return c;
                }
                return null;
#else
                if (_instance == null)
                {
                    var found = Resources.FindObjectsOfTypeAll<InteractableCatalog>();
                    if (found != null && found.Length > 0) _instance = found[0];
                }
                return _instance;
#endif
            }
        }

        public string[] GetArchetypeIds()
        {
            if (archetypes == null) return System.Array.Empty<string>();
            return archetypes.Where(a => a != null && !string.IsNullOrWhiteSpace(a.archetypeId))
                             .Select(a => a.archetypeId).ToArray();
        }

        public string[] GetItemIds()
        {
            if (items == null) return System.Array.Empty<string>();
            return items.Where(i => i != null && !string.IsNullOrWhiteSpace(i.itemId))
                        .Select(i => i.itemId).ToArray();
        }

        public string[] GetContextTags()
        {
            if (contextTags == null) return System.Array.Empty<string>();
            return contextTags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToArray();
        }

        public string[] GetActionVocabulary()
        {
            if (actionVocabulary == null) return System.Array.Empty<string>();
            return actionVocabulary.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToArray();
        }

        public string[] GetObjectStateIds()
        {
            var states = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var stateName in System.Enum.GetNames(typeof(InteractableObject.ObjectState)))
            {
                if (!string.IsNullOrWhiteSpace(stateName))
                    states.Add(stateName.Trim());
            }

            if (archetypes != null)
            {
                foreach (var archetype in archetypes)
                {
                    if (archetype == null) continue;
                    string defaultStateName = archetype.defaultState.ToString();
                    if (!string.IsNullOrWhiteSpace(defaultStateName))
                        states.Add(defaultStateName.Trim());
                }
            }

            if (customObjectStates != null)
            {
                foreach (var state in customObjectStates)
                {
                    if (!string.IsNullOrWhiteSpace(state))
                        states.Add(state.Trim());
                }
            }

            return states.OrderBy(s => s, System.StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public string[] GetRegisteredObjectIds()
        {
            if (registeredObjectIds == null) return System.Array.Empty<string>();
            return registeredObjectIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        }

        /// <summary>
        /// Get archetype by ID
        /// </summary>
        public InteractableArchetype GetArchetype(string id)
        {
            return archetypes.Find(a => string.Equals(a.archetypeId, id, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get item definition by ID
        /// </summary>
        public InventoryItemDefinition GetItemDefinition(string id)
        {
            return items.Find(i => string.Equals(i.itemId, id, System.StringComparison.OrdinalIgnoreCase));
        }
    }

    [System.Serializable]
    public class InteractableArchetype
    {
        public string archetypeId = "Generator";
        public string defaultName = "Generator";
        
        [TextArea(2, 4)]
        public string defaultDescription = "A machine that produces power.";
        
        public InteractableObject.ObjectState defaultState = InteractableObject.ObjectState.Locked;
        
        public string[] defaultContextTags = { "machine", "power" };

        [TextArea(1, 3)]
        [Tooltip("LLM-facing interaction hint surfaced through the exporter pipeline. " +
                 "Describes what an agent can do with objects of this archetype.")]
        public string interactionHint = "";
        
        // Legacy: affordances are now defined on InteractionRuleSet rules, not on archetypes.
        // Hidden from the inspector but preserved on disk so existing .asset files don't lose data.
        [HideInInspector]
        public List<ActionAffordance> affordances = new List<ActionAffordance>();
    }

    [System.Serializable]
    public class InventoryItemDefinition
    {
        public string itemId = "toolbox";
        public string displayName = "Toolbox";
        
        [TextArea(1, 3)]
        public string description = "Used for fixing broken machinery.";

        [TextArea(1, 3)]
        [Tooltip("LLM-facing hint surfaced through the same exporter path as archetype hints. " +
                 "Used when this item is placed in the world via an InteractableObject component.")]
        public string interactionHint = "";

        [Tooltip("Default context tags applied to scene InteractableObjects bound to this item.")]
        public string[] defaultContextTags = { "item" };

        public float weight = 5f;
        public string category = "tools";
        
        [Header("Visual/World Setup (Optional)")]
        public GameObject dropPrefab;
    }
}
