using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Interactions
{
    // A6.6: InteractionRule class deleted — its data model is now absorbed into
    // InteractionEdge (A6.1). Base types below remain as they are used by
    // InteractionConditions.cs, InteractionEffects.cs, and InteractionEdge.

    /// <summary>
    /// Unified natural-language text block exposed to the LLM / UI.
    /// Lives on InteractionEdge.hints (A6.1).
    /// </summary>
    [Serializable]
    public class RuleHints
    {
        [Tooltip("Short label shown to the agent, e.g. 'Fix the generator'.")]
        public string actionLabel = "";

        [Tooltip("Precondition summary, e.g. 'Requires a Toolbox.'. Surfaced to the LLM.")]
        [TextArea(1, 3)]
        public string preconditionHint = "";

        [Tooltip("Message returned on success. Use {subject} as a placeholder for the object label.")]
        [TextArea(1, 3)]
        public string successMessage = "";

        [Tooltip("Message returned on failure. Use {reason} as a placeholder for the auto-generated reason.")]
        [TextArea(1, 3)]
        public string failureTemplate = "{reason}";

        [Tooltip("Downstream world-impact hint, e.g. 'Power returns to the vending area.'")]
        [TextArea(1, 3)]
        public string worldImpactHint = "";
    }

    /// <summary>
    /// Evaluation context passed to every Condition and Effect.
    /// Kept runtime-safe (no UnityEditor references).
    /// </summary>
    public class InteractionContext
    {
        public string actorId;
        public GameObject actorGameObject;
        public AgentInventory actorInventory;

        public GameObject subject;
        public InteractableObject subjectInteractable;

        // Effect output channel. Effects append signals for downstream systems
        // (VFX, audio, events, analytics, LLM perception).
        public List<string> emittedSignals = new List<string>();
    }

    /// <summary>
    /// Base class for all preconditions. Concrete subclasses are stored
    /// via [SerializeReference] so one InteractionEdge can hold a heterogeneous list.
    /// </summary>
    [Serializable]
    public abstract class InteractionCondition
    {
        /// <summary>
        /// Evaluate this condition against the given context.
        /// Return true if satisfied. If false, write a human-readable
        /// reason into <paramref name="failureReason"/>.
        /// </summary>
        public abstract bool Evaluate(InteractionContext ctx, out string failureReason);

        /// <summary>Short human summary for the LLM / editor tooltip.</summary>
        public virtual string Describe() => GetType().Name;
    }

    /// <summary>
    /// Base class for all effects. Concrete subclasses are stored via
    /// [SerializeReference]. Effects mutate world state; they are applied
    /// in list order after all preconditions succeed.
    /// </summary>
    [Serializable]
    public abstract class InteractionEffect
    {
        public abstract void Apply(InteractionContext ctx);

        public virtual string Describe() => GetType().Name;
    }
}
