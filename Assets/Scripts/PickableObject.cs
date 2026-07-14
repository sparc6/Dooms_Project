using UnityEngine;

namespace MLA_SIM
{
    /// <summary>
    /// Minimal metadata for pickable objects. Keeps Phase 1 changes small and Unity-only.
    /// </summary>
    public class PickableObject : MonoBehaviour
    {
        [Header("Pickable Settings")]
        public bool isPickable = true;
        [Tooltip("Stable item id to store in inventory. Defaults to GameObject.name if empty")] 
        public string itemId = "";
        [Tooltip("Weight used for inventory capacity checks")] 
        public float weight = 1f;

        private void Awake()
        {
            if (string.IsNullOrEmpty(itemId))
            {
                itemId = gameObject.name;
            }
            if (weight <= 0f)
            {
                weight = 1f;
            }
        }

        public bool TryResolveRequestedAction(string requestedAction, out string resolvedAction)
        {
            resolvedAction = requestedAction;

            if (!isPickable || string.IsNullOrWhiteSpace(requestedAction))
            {
                return false;
            }

            if (string.Equals(requestedAction, "InteractWith", System.StringComparison.OrdinalIgnoreCase))
            {
                resolvedAction = "Pickup";
                return true;
            }

            return false;
        }

        public bool TryPickup(AgentInventory inventory, out string failureReason)
        {
            failureReason = string.Empty;

            if (!isPickable)
            {
                failureReason = $"object_not_pickable:{itemId}";
                return false;
            }

            if (inventory == null)
            {
                failureReason = $"inventory_missing:{itemId}";
                return false;
            }

            if (!inventory.AddItem(itemId, 1, weight))
            {
                failureReason = $"inventory_cannot_carry:{itemId}";
                return false;
            }

            gameObject.SetActive(false);
            return true;
        }
    }
}
