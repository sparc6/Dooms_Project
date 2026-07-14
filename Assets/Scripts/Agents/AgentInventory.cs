using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace MLA_SIM
{
    /// <summary>
    /// Agent inventory system for tracking carried items.
    /// Integrates with affordance checking and action preconditions.
    /// </summary>
    public class AgentInventory : MonoBehaviour
    {
        [Header("Inventory Configuration")]
        [Tooltip("Optional catalog to auto-fill weight/category for items")]
        [SerializeField] private InteractableCatalog sharedCatalog;
        [SerializeField] private int maxCapacity = 10;
        [SerializeField] private float maxWeight = 50f;
        [SerializeField] private bool enableDebugLogging = true;
        
        [Header("Current Inventory (Read Only)")]
        [SerializeField] private List<InventoryItem> items = new List<InventoryItem>();
        
        // Runtime identity cache. Avoids a hard dependency on AgentController in
        // stripped-down T4-only packages while still producing stable logs/events.
        private string resolvedAgentId;
        private string resolvedAgentName;
        
        void Start()
        {
            ResolveAgentIdentity();
            
            if (enableDebugLogging)
            {
                Debug.Log($"Inventory initialized for {resolvedAgentName} - Capacity: {maxCapacity}, Max Weight: {maxWeight}");
            }
        }

        private void ResolveAgentIdentity()
        {
            resolvedAgentId = gameObject.name;
            resolvedAgentName = gameObject.name;

            // Prefer a DOOMS tag when present because T4 agents always carry one.
            var doomsTag = GetComponent("MLA_SIM.Dooms.DoomsAgentTag") ?? GetComponent("DoomsAgentTag");
            if (doomsTag != null)
            {
                var tagType = doomsTag.GetType();
                var tagIdField = tagType.GetField("agentId");
                var tagIdValue = tagIdField?.GetValue(doomsTag) as string;
                if (!string.IsNullOrWhiteSpace(tagIdValue))
                    resolvedAgentId = tagIdValue;
            }

            // If a legacy AgentController exists, read it via reflection instead of
            // keeping a compile-time dependency on the full MLA agent stack.
            var agentController = GetComponent("AgentController") ?? GetComponent("MLA_SIM.AgentController");
            if (agentController == null)
                return;

            var controllerType = agentController.GetType();

            var getName = controllerType.GetMethod("GetAgentName");
            var getId = controllerType.GetMethod("GetAgentID");
            if (getName != null)
            {
                var value = getName.Invoke(agentController, null) as string;
                if (!string.IsNullOrWhiteSpace(value))
                    resolvedAgentName = value;
            }
            if (getId != null)
            {
                var value = getId.Invoke(agentController, null) as string;
                if (!string.IsNullOrWhiteSpace(value))
                    resolvedAgentId = value;
            }

            var nameField = controllerType.GetField("agentName");
            var idField = controllerType.GetField("agentID");
            if (nameField != null)
            {
                var value = nameField.GetValue(agentController) as string;
                if (!string.IsNullOrWhiteSpace(value))
                    resolvedAgentName = value;
            }
            if (idField != null)
            {
                var value = idField.GetValue(agentController) as string;
                if (!string.IsNullOrWhiteSpace(value))
                    resolvedAgentId = value;
            }
        }

        /// <summary>
        /// Set the shared catalog used for item defaults.
        /// </summary>
        public void SetSharedCatalog(InteractableCatalog catalog)
        {
            sharedCatalog = catalog;
        }

        /// <summary>
        /// Get the shared catalog used for item defaults.
        /// </summary>
        public InteractableCatalog GetSharedCatalog()
        {
            return sharedCatalog;
        }
        
        /// <summary>
        /// Check if agent can carry a specific item
        /// </summary>
        public bool CanCarry(string itemId, int quantity = 1, float weight = 1f)
        {
            // Check capacity
            if (items.Count + quantity > maxCapacity)
            {
                return false;
            }
            
            // Check weight
            float currentWeight = GetTotalWeight();
            if (currentWeight + (weight * quantity) > maxWeight)
            {
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Add item to inventory
        /// </summary>
        public bool AddItem(string itemId, int quantity = 1, float weight = -1f, string category = "")
        {
            // Try to resolve missing properties from catalog
            if (sharedCatalog != null)
            {
                var def = sharedCatalog.GetItemDefinition(itemId);
                if (def != null)
                {
                    if (weight < 0) weight = def.weight;
                    if (string.IsNullOrEmpty(category)) category = def.category;
                }
            }

            // Fallbacks if not in catalog
            if (weight < 0) weight = 1f;
            if (string.IsNullOrEmpty(category)) category = "misc";

            if (!CanCarry(itemId, quantity, weight))
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning($"{resolvedAgentName} cannot carry {itemId} x{quantity} - capacity/weight exceeded");
                }
                return false;
            }
            
            // Check if item already exists (for stackable items)
            var existingItem = items.FirstOrDefault(i => i.itemId == itemId);
            if (existingItem != null)
            {
                existingItem.quantity += quantity;
                if (enableDebugLogging)
                {
                    Debug.Log($"{resolvedAgentName} added {quantity} {itemId} (now has {existingItem.quantity})");
                }
            }
            else
            {
                var newItem = new InventoryItem
                {
                    itemId = itemId,
                    quantity = quantity,
                    weight = weight,
                    category = category,
                    acquiredTime = System.DateTime.Now.ToString("o")
                };
                items.Add(newItem);
                
                if (enableDebugLogging)
                {
                    Debug.Log($"{resolvedAgentName} acquired {quantity} {itemId}");
                }
            }
            
            // Publish inventory change event (observational only)
            PublishInventoryChangeEvent("item_added", itemId, quantity);
            
            return true;
        }
        
        /// <summary>
        /// Remove item from inventory
        /// </summary>
        public bool RemoveItem(string itemId, int quantity = 1)
        {
            var existingItem = items.FirstOrDefault(i => i.itemId == itemId);
            if (existingItem == null || existingItem.quantity < quantity)
            {
                if (enableDebugLogging)
                {
                    Debug.LogWarning($"{resolvedAgentName} cannot remove {quantity} {itemId} - insufficient quantity");
                }
                return false;
            }
            
            existingItem.quantity -= quantity;
            if (existingItem.quantity <= 0)
            {
                items.Remove(existingItem);
            }
            
            if (enableDebugLogging)
            {
                Debug.Log($"{resolvedAgentName} removed {quantity} {itemId}");
            }
            
            // Publish inventory change event (observational only)
            PublishInventoryChangeEvent("item_removed", itemId, quantity);
            
            return true;
        }
        
        /// <summary>
        /// Check if agent has specific item(s)
        /// </summary>
        public bool HasItem(string itemId, int requiredQuantity = 1)
        {
            var item = items.FirstOrDefault(i => i.itemId == itemId);
            return item != null && item.quantity >= requiredQuantity;
        }
        
        /// <summary>
        /// Find items by category
        /// </summary>
        public List<InventoryItem> FindItemsByCategory(string category)
        {
            return items.Where(i => i.category.Equals(category, System.StringComparison.OrdinalIgnoreCase)).ToList();
        }
        
        /// <summary>
        /// Find items by tag (if itemId contains tag)
        /// </summary>
        public List<InventoryItem> FindItemsByTag(string tag)
        {
            return items.Where(i => i.itemId.ToLower().Contains(tag.ToLower())).ToList();
        }
        
        /// <summary>
        /// Get total weight of all items
        /// </summary>
        public float GetTotalWeight()
        {
            return items.Sum(i => i.weight * i.quantity);
        }
        
        /// <summary>
        /// Get current item count
        /// </summary>
        public int GetItemCount()
        {
            return items.Sum(i => i.quantity);
        }
        
        /// <summary>
        /// Get read-only inventory snapshot
        /// </summary>
        public InventorySnapshot GetInventorySnapshot()
        {
            return new InventorySnapshot
            {
                items = new List<InventoryItem>(items),
                totalWeight = GetTotalWeight(),
                totalCount = GetItemCount(),
                capacity = maxCapacity,
                maxWeight = maxWeight,
                utilizationPercent = (float)GetItemCount() / maxCapacity
            };
        }
        
        /// <summary>
        /// Clear all inventory (for testing/reset)
        /// </summary>
        public void ClearInventory()
        {
            items.Clear();
            PublishInventoryChangeEvent("inventory_cleared", "", 0);
            
            if (enableDebugLogging)
            {
                Debug.Log($"{resolvedAgentName} inventory cleared");
            }
        }
        
        /// <summary>
        /// Publish inventory change event for environmental data collection
        /// </summary>
        private void PublishInventoryChangeEvent(string changeType, string itemId, int quantity)
        {
            var worldEventType = LegacyEventTypeResolver.ResolveType("MLA_SIM.WorldEvent", "WorldEvent");
            var eventBusType = LegacyEventTypeResolver.ResolveType("MLA_SIM.GlobalEventBus", "GlobalEventBus");
            if (worldEventType == null || eventBusType == null)
            {
                return;
            }

            var publishMethod = eventBusType.GetMethod(
                "PublishLocalEvent",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new[] { worldEventType, typeof(List<string>) },
                null);
            if (publishMethod == null)
            {
                return;
            }

            var inventoryEvent = System.Activator.CreateInstance(worldEventType);
            LegacyEventTypeResolver.SetField(worldEventType, inventoryEvent, "eventType", "inventory_change");
            LegacyEventTypeResolver.SetField(worldEventType, inventoryEvent, "description", $"{resolvedAgentName} {changeType}: {itemId} x{quantity}");
            LegacyEventTypeResolver.SetField(worldEventType, inventoryEvent, "sourceAgentId", resolvedAgentId);
            LegacyEventTypeResolver.SetField(worldEventType, inventoryEvent, "position", transform.position);
            LegacyEventTypeResolver.SetField(worldEventType, inventoryEvent, "parameters", new Dictionary<string, object>
            {
                ["observational_only"] = true,
                ["decision_impact"] = "none",
                ["change_type"] = changeType,
                ["item_id"] = itemId,
                ["quantity"] = quantity,
                ["total_items"] = GetItemCount(),
                ["total_weight"] = GetTotalWeight()
            });

            publishMethod.Invoke(null, new object[] { inventoryEvent, new List<string> { resolvedAgentId } });
        }
        
        /// <summary>
        /// Get inventory summary for debugging
        /// </summary>
        public string GetInventorySummary()
        {
            if (items.Count == 0)
            {
                return "Empty inventory";
            }
            
            var summary = $"Inventory ({GetItemCount()}/{maxCapacity} items, {GetTotalWeight():F1}/{maxWeight}kg):\n";
            foreach (var item in items)
            {
                summary += $"- {item.itemId} x{item.quantity} ({item.category})\n";
            }
            
            return summary.TrimEnd();
        }
    }
    
    [System.Serializable]
    public class InventoryItem
    {
        public string itemId;
        public int quantity;
        public float weight;
        public string category;
        public string acquiredTime;
        
        public InventoryItem()
        {
            quantity = 1;
            weight = 1f;
            category = "misc";
        }
    }
    
    [System.Serializable]
    public class InventorySnapshot
    {
        public List<InventoryItem> items;
        public float totalWeight;
        public int totalCount;
        public int capacity;
        public float maxWeight;
        public float utilizationPercent;
    }

    internal static class LegacyEventTypeResolver
    {
        public static System.Type ResolveType(params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                var type = System.Type.GetType($"{candidate}, Assembly-CSharp") ?? System.Type.GetType(candidate);
                if (type != null)
                {
                    return type;
                }
            }

            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var candidate in candidates)
                {
                    var type = asm.GetType(candidate);
                    if (type != null)
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        public static void SetField(System.Type type, object instance, string fieldName, object value)
        {
            var field = type.GetField(fieldName);
            if (field != null)
            {
                field.SetValue(instance, value);
            }
        }
    }
}
