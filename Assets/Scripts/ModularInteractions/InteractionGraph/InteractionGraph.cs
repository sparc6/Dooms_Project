using System.Collections.Generic;
using UnityEngine;
using MLA_SIM;
using MLA_SIM.Interactions;

namespace MLA_SIM.ModularInteractions
{
    /// <summary>
    /// AREA 04 — A4.5. Object state-machine graph, mirroring the DOOMS SceneGraph
    /// pattern (plain serialized container, no hard NodeCanvas dependency).
    ///
    ///   NODE = an object state (Usable / Broken / Locked / custom string).
    ///   EDGE = an affordance: action + requirements + tier gate + animation
    ///          sequence + hold + yields, leading to a resulting state. An edge may
    ///          optionally be gated on ANOTHER object's current state (dependency
    ///          chains, the migration target for IO_DependencyManager / InteractionRuleSet).
    ///
    /// A6.1: InteractionEdge enriched with id, preconditions, effects, hints from
    ///       InteractionRule data model. A6.2 adds conditionNode typed reference.
    /// </summary>
    [System.Serializable]
    public class InteractionEdge
    {
        [Tooltip("Stable GUID. Auto-assigned, do not edit.")]
        public string id = "";

        [RegistryDropdown(RegistryType.Action)]
        [MLA_SIM.Dooms.RegistryDropdownNC((MLA_SIM.Dooms.RegistryType)RegistryType.Action)]
        public string actionName = "InteractWith";

        [Tooltip("Items required to traverse this edge.")]
        [RegistryDropdown(RegistryType.Item)]
        [MLA_SIM.Dooms.RegistryDropdownNC((MLA_SIM.Dooms.RegistryType)RegistryType.Item)]
        public string[] requiredItemIds = new string[0];

        [Tooltip("Items yielded when this edge is traversed.")]
        [RegistryDropdown(RegistryType.Item)]
        [MLA_SIM.Dooms.RegistryDropdownNC((MLA_SIM.Dooms.RegistryType)RegistryType.Item)]
        public string[] yieldItemIds = new string[0];

        [Tooltip("DOOMS tiers permitted to use this affordance. None = any tier.")]
        public DoomsTier allowedTiers = DoomsTier.None;

        [Tooltip("Animation sequence played while traversing (Area 01 sequence).")]
        [RegistryDropdown(RegistryType.AnimationSequence)]
        [MLA_SIM.Dooms.RegistryDropdownNC((MLA_SIM.Dooms.RegistryType)RegistryType.AnimationSequence)]
        public string sequenceId = "";

        [Tooltip("Fallback animator state if no sequence is set.")]
        [RegistryDropdown(RegistryType.AnimationState)]
        [MLA_SIM.Dooms.RegistryDropdownNC((MLA_SIM.Dooms.RegistryType)RegistryType.AnimationState)]
        public string animatorStateName = "";

        [Tooltip("Object-owned hold time for this affordance (A1.6 authority).")]
        public float holdSeconds = 2f;

        [Tooltip("State id this object transitions to after the edge succeeds.")]
        [RegistryDropdown(RegistryType.ObjectState)]
        [MLA_SIM.Dooms.RegistryDropdownNC((MLA_SIM.Dooms.RegistryType)RegistryType.ObjectState)]
        public string resultingStateId = "";

        [Header("Optional cross-object dependency gate")]
        // Legacy string kept for JSON migration only — do not use at runtime.
        [HideInInspector] public string conditionObjectName = "";

        // Typed reference set by SceneObjectGraph.WireConditionNodes() at load time.
        [System.NonSerialized] public SceneObjectNode conditionNode;
        [RegistryDropdown(RegistryType.ObjectState)]
        [MLA_SIM.Dooms.RegistryDropdownNC((MLA_SIM.Dooms.RegistryType)RegistryType.ObjectState)]
        public string conditionStateId = "";

        [Header("Preconditions & Effects (A6.1)")]
        [SerializeReference]
        public List<InteractionCondition> preconditions = new List<InteractionCondition>();

        [SerializeReference]
        public List<InteractionEffect> effects = new List<InteractionEffect>();

        public RuleHints hints = new RuleHints();

        [TextArea(1, 2)] public string successMessage = "";
        [TextArea(1, 2)] public string failureMessage = "";

        public void EnsureId()
        {
            if (string.IsNullOrEmpty(id))
                id = System.Guid.NewGuid().ToString("N");
        }
    }

    [System.Serializable]
    public class InteractionStateNode
    {
        [Tooltip("Logical state id for this node (e.g. Usable / Broken / Locked).")]
        [RegistryDropdown(RegistryType.ObjectState)]
        [MLA_SIM.Dooms.RegistryDropdownNC((MLA_SIM.Dooms.RegistryType)RegistryType.ObjectState)]
        public string stateId = "Usable";
        public Vector2 position;
        public List<InteractionEdge> edges = new List<InteractionEdge>();
    }

    [CreateAssetMenu(fileName = "InteractionGraph", menuName = "MLA SIM/Interaction Graph")]
    public class InteractionGraph : ScriptableObject
    {
        [Tooltip("Initial state id when the object spawns. Defaults to the first node.")]
        [RegistryDropdown(RegistryType.ObjectState)]
        [MLA_SIM.Dooms.RegistryDropdownNC((MLA_SIM.Dooms.RegistryType)RegistryType.ObjectState)]
        public string initialStateId = "";

        public List<InteractionStateNode> nodes = new List<InteractionStateNode>();

        public InteractionStateNode AddNode(string stateId)
        {
            if (nodes == null) nodes = new List<InteractionStateNode>();
            var node = new InteractionStateNode { stateId = stateId };
            nodes.Add(node);
            if (string.IsNullOrEmpty(initialStateId)) initialStateId = stateId;
            return node;
        }

        public InteractionStateNode FindState(string stateId)
        {
            if (string.IsNullOrEmpty(stateId) || nodes == null) return null;
            return nodes.Find(n => n != null &&
                string.Equals(n.stateId, stateId, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Resolve the affordance edge for an action from a given state.</summary>
        public InteractionEdge FindEdge(string fromStateId, string actionName)
        {
            var node = FindState(fromStateId);
            if (node == null || node.edges == null) return null;
            return node.edges.Find(e => e != null &&
                string.Equals(e.actionName, actionName, System.StringComparison.OrdinalIgnoreCase));
        }

        public string ResolveInitialState()
        {
            if (!string.IsNullOrEmpty(initialStateId)) return initialStateId;
            return (nodes != null && nodes.Count > 0 && nodes[0] != null) ? nodes[0].stateId : "";
        }
    }
}
