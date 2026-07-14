using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms
{
    [DisallowMultipleComponent]
    [AddComponentMenu("DOOMS/Interaction Point")]
    public class InteractionPoint : MonoBehaviour
    {
        [Tooltip("World transform the agent should snap to. Defaults to this GameObject's transform.")]
        public Transform anchor;

        [Tooltip("Logical tag for this interaction point, chosen from the registry.")]
        [RegistryDropdown(RegistryType.InteractionPoint)]
        public string pointTag = "";

        [Tooltip("Animator state to play while occupied.")]
        [RegistryDropdown(RegistryType.AnimationState)]
        public string animatorStateName = "";

        [Tooltip("Optional animation sequence ID (e.g., 'Sleep' climb -> sleep -> getup). If non-empty, overrides animatorStateName.")]
        [RegistryDropdown(RegistryType.AnimationSequence)]
        [RegistryDropdownNC(RegistryType.AnimationSequence)]
        public string sequenceId = "";

        [Tooltip("Optional prop to spawn on the agent while occupying this point. Resolved via PropRegistrySO.")]
        [RegistryDropdown(RegistryType.Prop)]
        public string propId = "";

        [Tooltip("Seconds to hold the action. <= 0 means the agent stays until something else moves them.")]
        public float holdSeconds = 4f;

        [Tooltip("Max simultaneous occupants.")]
        public int capacity = 1;

        [Tooltip("If true, occupying this anchor counts as performing an 'infectious' action.")]
        public bool infectious = false;

        [Tooltip("Optional faction filter. Empty = any faction allowed.")]
        public List<string> allowedFactions = new List<string>();

        // ---- runtime occupancy ----------------------------------------------
        private readonly HashSet<string> _occupants = new HashSet<string>();

        public Transform GetAnchor() => anchor != null ? anchor : transform;

        public bool IsFactionAllowed(string factionId)
        {
            if (allowedFactions == null || allowedFactions.Count == 0) return true;
            if (string.IsNullOrEmpty(factionId)) return false;
            for (int i = 0; i < allowedFactions.Count; i++)
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
            InteractionPointIndex.Register(this);
        }

        private void OnDisable()
        {
            InteractionPointIndex.Unregister(this);
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
