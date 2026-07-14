using UnityEngine;

namespace MLA_SIM.Interactions
{
    /// <summary>
    /// Plan M4: Per-Interactive-Object animation anchor.
    ///
    /// Sits as a sibling (or child) of <see cref="InteractableObject"/>. When
    /// AgentActionSystem (M5) executes a rule that touches this object, it:
    ///   1. Reserves the anchor (single occupant per point).
    ///   2. NavMeshes to <see cref="anchor"/> position, faces +Z of the anchor.
    ///   3. Calls <see cref="AnimatorLocomotionDriver.PlayActionState"/> with
    ///      <see cref="animatorStateName"/> for <see cref="holdSeconds"/>.
    ///
    /// Tier-gating: only agents whose DOOMS tier is in <see cref="allowedTiers"/>
    /// may use this anchor. Defaults to T1+T2 (the leads). T3 NPCs are routed
    /// through <see cref="TargetTransformAnchor"/> instead.
    ///
    /// This component is intentionally tiny: it stores authoring data, owns
    /// reservation state, and does NOT issue navigation calls itself.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MLA SIM/Interactions/Interaction Transform Point")]
    public class InteractionTransformPoint : MonoBehaviour
    {
        [Tooltip("World transform the agent should snap to before playing the action. " +
                 "Defaults to this GameObject's transform if left empty.")]
        public Transform anchor;

        [Tooltip("Animator state played by AnimatorLocomotionDriver.PlayActionState " +
                 "while the agent is on this anchor. Empty = falls back to InteractionRule.actorVerb.")]
        public string animatorStateName = "";

        [Tooltip("Seconds the action animation should hold. <= 0 lets the Animator state finish naturally " +
                 "(or the driver waits for an explicit transition).")]
        public float holdSeconds = 2f;

        [Tooltip("DOOMS tiers allowed to use this anchor. None = any tier may use it.")]
        public DoomsTier allowedTiers = DoomsTier.Leads;

        [Tooltip("Optional rule id this anchor is dedicated to. Empty = matches any rule on the parent IO.")]
        public string ruleId = "";

        // ---- runtime reservation ---------------------------------------------
        private string _reservedByAgentId = "";
        private float _reservedAtTime = 0f;
        private const float ReservationTimeoutSec = 30f;

        public Transform GetAnchor() => anchor != null ? anchor : transform;

        public bool IsTierAllowed(int tier)
        {
            return DoomsTierUtil.IsTierAllowed(allowedTiers, tier);
        }

        public bool TryReserve(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return false;
            if (string.IsNullOrEmpty(_reservedByAgentId) || _reservedByAgentId == agentId)
            {
                _reservedByAgentId = agentId;
                _reservedAtTime = Time.time;
                return true;
            }
            // Auto-release stale reservations.
            if (Time.time - _reservedAtTime > ReservationTimeoutSec)
            {
                _reservedByAgentId = agentId;
                _reservedAtTime = Time.time;
                return true;
            }
            return false;
        }

        public void Release(string agentId)
        {
            if (_reservedByAgentId == agentId)
            {
                _reservedByAgentId = "";
                _reservedAtTime = 0f;
            }
        }

        public bool IsReserved => !string.IsNullOrEmpty(_reservedByAgentId);
        public string ReservedBy => _reservedByAgentId;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Transform a = GetAnchor();
            if (a == null) return;
            Gizmos.color = IsReserved ? Color.red : new Color(0.2f, 0.8f, 1f, 1f);
            Gizmos.DrawWireSphere(a.position, 0.25f);
            Gizmos.DrawLine(a.position, a.position + a.forward * 0.6f);
        }
#endif
    }
}
