using System;
using UnityEngine;

namespace MLA_SIM.Interactions
{
    /// <summary>
    /// Precondition: the subject InteractableObject must currently be in one of
    /// the listed states.
    /// </summary>
    [Serializable]
    public class RequireSubjectStateCondition : InteractionCondition
    {
        public InteractableObject.ObjectState[] allowedStates =
            { InteractableObject.ObjectState.Usable };

        public override bool Evaluate(InteractionContext ctx, out string failureReason)
        {
            if (ctx.subjectInteractable == null)
            {
                failureReason = "Target is not interactable.";
                return false;
            }
            var current = ctx.subjectInteractable.currentState;
            for (int i = 0; i < allowedStates.Length; i++)
            {
                if (allowedStates[i] == current)
                {
                    failureReason = null;
                    return true;
                }
            }
            string allowed = string.Join(" or ", allowedStates);
            failureReason = $"It must be {allowed.ToLower()} but is currently {current.ToString().ToLower()}.";
            return false;
        }

        public override string Describe()
        {
            return $"Subject state in [{string.Join(",", allowedStates)}]";
        }
    }

    /// <summary>
    /// Precondition: the actor must hold the given item (and quantity).
    /// </summary>
    [Serializable]
    public class RequireItemCondition : InteractionCondition
    {
        [CatalogItemId]
        public string itemId = "toolbox";
        public int quantity = 1;
        [Tooltip("If true, this item is consumed by ConsumeItemEffect when applied (declarative; not enforced here).")]
        public bool consumed = false;

        public override bool Evaluate(InteractionContext ctx, out string failureReason)
        {
            if (ctx.actorInventory == null)
            {
                failureReason = $"You need {itemId} but you carry no inventory.";
                return false;
            }
            if (!ctx.actorInventory.HasItem(itemId, quantity))
            {
                string qty = quantity > 1 ? $" x{quantity}" : "";
                failureReason = $"You need {itemId}{qty}.";
                return false;
            }
            failureReason = null;
            return true;
        }

        public override string Describe()
        {
            return quantity > 1 ? $"Requires {itemId} x{quantity}" : $"Requires {itemId}";
        }
    }

    /// <summary>
    /// Precondition: subject must not be reserved by another agent.
    /// </summary>
    [Serializable]
    public class RequireUnreservedCondition : InteractionCondition
    {
        public override bool Evaluate(InteractionContext ctx, out string failureReason)
        {
            if (ctx.subjectInteractable == null)
            {
                failureReason = null;
                return true;
            }
            if (ctx.subjectInteractable.IsReserved() &&
                !ctx.subjectInteractable.IsReservedBy(ctx.actorId))
            {
                failureReason = $"{ctx.subjectInteractable.GetObjectName()} is currently in use.";
                return false;
            }
            failureReason = null;
            return true;
        }

        public override string Describe() => "Subject not reserved by another agent";
    }

    /// <summary>
    /// Precondition: subject must be within reach of the actor (uses
    /// InteractableObject.interactionRange).
    /// </summary>
    [Serializable]
    public class RequireWithinRangeCondition : InteractionCondition
    {
        [Tooltip("Override range. Negative = use subject's interactionRange.")]
        public float overrideRange = -1f;

        public override bool Evaluate(InteractionContext ctx, out string failureReason)
        {
            if (ctx.actorGameObject == null || ctx.subjectInteractable == null)
            {
                failureReason = null;
                return true;
            }
            float r = overrideRange > 0f ? overrideRange : ctx.subjectInteractable.interactionRange;
            float d = Vector3.Distance(
                ctx.actorGameObject.transform.position,
                ctx.subjectInteractable.transform.position);
            if (d > r)
            {
                failureReason = $"You are too far from {ctx.subjectInteractable.GetObjectName()} ({d:F1}m, need {r:F1}m).";
                return false;
            }
            failureReason = null;
            return true;
        }

        public override string Describe() => "Within interaction range";
    }
}
