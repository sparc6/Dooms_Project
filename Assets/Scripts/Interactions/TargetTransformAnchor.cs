using UnityEngine;

namespace MLA_SIM.Interactions
{
    /// <summary>
    /// Plan M4: Standalone NPC animation anchor (no InteractableObject required).
    ///
    /// Used by Tier-3 NPCs (DOOMS faction NPCs). The npc_action_picker (M7)
    /// returns an action plus a <see cref="targetClass"/>; the runtime queries
    /// <see cref="TargetClassRegistry"/> for the closest free anchor of that
    /// class and routes the agent to it.
    ///
    /// Unlike <see cref="InteractionTransformPoint"/> there is no rule logic,
    /// no inventory consumption, and no state mutation: anchors purely drive
    /// "play this animation here for this long".
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MLA SIM/Interactions/Target Transform Anchor")]
    public class TargetTransformAnchor : MonoBehaviour
    {
        [Tooltip("World transform the agent should snap to. Defaults to this GameObject's transform.")]
        public Transform anchor;

        [Tooltip("Logical class name. Picker queries by this string (e.g. 'GuardPost', 'CampfireSeat'). " +
                 "Multiple anchors can share a class; the registry treats them as a pool.")]
        public string targetClass = "";

        [Tooltip("Animator state to play while occupied. Empty = action verb is used as fallback.")]
        public string animatorStateName = "";

        [Tooltip("Seconds to hold the action. <= 0 means the agent stays until something else moves them.")]
        public float holdSeconds = 4f;

        [Tooltip("Max simultaneous occupants. 1 for single-seat anchors, higher for crowd points.")]
        public int capacity = 1;

        [Tooltip("If true, occupying this anchor counts as performing an 'infectious' action that the " +
                 "infection mechanic (M8) can spread to nearby same-faction NPCs.")]
        public bool infectious = false;

        [Tooltip("Optional faction filter. Empty = any faction. Otherwise only NPCs whose faction is in " +
                 "this list will be considered by the picker.")]
        public string[] allowedFactions = new string[0];

        // ---- runtime occupancy ----------------------------------------------
        private readonly System.Collections.Generic.HashSet<string> _occupants
            = new System.Collections.Generic.HashSet<string>();

        public Transform GetAnchor() => anchor != null ? anchor : transform;

        public bool IsFactionAllowed(string factionId)
        {
            if (allowedFactions == null || allowedFactions.Length == 0) return true;
            if (string.IsNullOrEmpty(factionId)) return false;
            for (int i = 0; i < allowedFactions.Length; i++)
            {
                if (string.Equals(allowedFactions[i], factionId, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public int OccupancyCount => _occupants.Count;
        public bool HasFreeSlot => _occupants.Count < Mathf.Max(1, capacity);

        public bool TryOccupy(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return false;
            if (_occupants.Contains(agentId)) return true;
            if (_occupants.Count >= Mathf.Max(1, capacity)) return false;
            _occupants.Add(agentId);
            return true;
        }

        public void Release(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return;
            _occupants.Remove(agentId);
        }

        private void OnEnable()
        {
            TargetClassRegistry.Register(this);
        }

        private void OnDisable()
        {
            TargetClassRegistry.Unregister(this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Transform a = GetAnchor();
            if (a == null) return;
            Gizmos.color = HasFreeSlot ? new Color(0.2f, 1f, 0.4f, 1f) : new Color(1f, 0.5f, 0.1f, 1f);
            Gizmos.DrawWireSphere(a.position, 0.30f);
            Gizmos.DrawLine(a.position, a.position + a.forward * 0.7f);
        }
#endif
    }
}
