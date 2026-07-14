using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using MLA_SIM;

public class InteractableObject : MonoBehaviour, INameProvider
{
    [Header("Object Properties")]
    public string objectName = "Supply Crate";
    [TextArea(2, 4)]
    public string description = "A metal crate that might contain useful items";
    [TextArea(2, 4)]
    [Tooltip("Manual simulation hint exposed to editor/runtime graph tooling.")]
    public string environmentHint = "";
    public ObjectState currentState = ObjectState.Usable;
    public System.Action<ObjectState, ObjectState> onStateChangedEvent;
    
    [Header("Library Integration")]
    [Tooltip("Optional shared catalog for archetype defaults and affordance templates")]
    public MLA_SIM.InteractableCatalog sharedCatalog;
    [Tooltip("Optional catalog archetype identifier. If empty, the object name is used.")]
    public string archetypeId = "";
    
    [Header("Interaction")]
    public float interactionRange = 2f;
    [Tooltip("Optional world anchor the agent should use for sequence-driven interaction alignment. Defaults to this transform.")]
    public Transform interactionAnchor;

    [Tooltip("Optional animation sequence ID for this object's interaction. If set, it overrides the legacy state path.")]
    [RegistryDropdown(RegistryType.AnimationSequence)]
    public string sequenceId = "";

    [Tooltip("Optional fallback animator state name when no sequence ID is assigned.")]
    [RegistryDropdown(RegistryType.AnimationState)]
    public string animatorStateName = "";

    [Tooltip("Hold time for sequence-driven interaction. This is the single object-owned timing value.")]
    public float holdSeconds = 2f;

    [Tooltip("A1.6: Safety margin (seconds) added on top of holdSeconds when deriving the reservation timeout, " +
             "so the reservation never expires mid-interaction. The object's holdSeconds is the authoritative " +
             "timing source; the backend action/routine timeout should be derived from EffectiveHoldTimeout().")]
    public float holdTimeoutMargin = 5f;

    /// <summary>
    /// A1.6 hold-time authority. The single source of truth for how long this
    /// object's interaction takes, plus a safety margin. Reservation timeout and
    /// the backend action timeout should both be derived from this so a long
    /// sequence is never aborted early (the 'Routine_aborted' class of bug).
    /// </summary>
    public float EffectiveHoldTimeout()
    {
        float h = holdSeconds > 0f ? holdSeconds : 0f;
        return h + Mathf.Max(0f, holdTimeoutMargin);
    }

    [Tooltip("DOOMS tiers permitted to use this object's full interaction logic. " +
             "None (0) = any tier may interact. " +
             "AgentActionSystem rejects requests from a non-listed tier with a natural " +
             "language failure so T3 NPCs gracefully fall back to their pool action.")]
    public DoomsTier allowedTiers = DoomsTier.Leads;

    public bool IsTierAllowed(int tier)
    {
        return DoomsTierUtil.IsTierAllowed(allowedTiers, tier);
    }

    [Header("Action Affordances")]
    [Tooltip("Define what actions this object supports and their requirements")]
    public List<ActionAffordance> actionAffordances = new List<ActionAffordance>();

    [Header("Interaction Graph (A4.5 — opt-in state machine)")]
    [Tooltip("Optional object state-machine graph. When assigned, this object is graph-driven; " +
             "when null, it uses the flat actionAffordances list above (fully backward compatible).")]
    public MLA_SIM.ModularInteractions.InteractionGraph interactionGraph;

    /// <summary>A4.5: runtime current state id when graph-driven (else empty).</summary>
    [System.NonSerialized] public string currentGraphStateId = "";

    public bool IsGraphDriven => interactionGraph != null;
    
    [Header("Reservation System")]
    [SerializeField] private bool isReserved = false;
    [SerializeField] private string reservedByAgentId = "";
    private float reservationTimeout = 30f;
    private float reservationStartTime;
    
    [Header("Context")]
    [RegistryDropdown(RegistryType.ContextTag)]
    public string[] contextTags = {"container", "supplies", "metal"};
    
    public enum ObjectState { Usable, Broken, Occupied, Locked }
    
    void Start()
    {
        // Ensure proper layer assignment
        gameObject.layer = LayerMask.NameToLayer("Interactable");
        
        // Add collider if missing
        if (GetComponent<Collider>() == null)
        {
            gameObject.AddComponent<BoxCollider>();
        }
        
        ApplyCatalogData();

        // A4.5: initialise graph state if this object is graph-driven.
        if (interactionGraph != null)
            currentGraphStateId = interactionGraph.ResolveInitialState();
    }

    public string GetObjectName()
    {
        return string.IsNullOrEmpty(objectName) ? gameObject.name : objectName;
    }

    public void SetSharedCatalog(MLA_SIM.InteractableCatalog catalog)
    {
        sharedCatalog = catalog;
    }

    public MLA_SIM.InteractableCatalog GetSharedCatalog()
    {
        return sharedCatalog;
    }
    
    private void ApplyCatalogData()
    {
        if (sharedCatalog == null) return;
        
        string targetId = string.IsNullOrEmpty(archetypeId) ? GetObjectName() : archetypeId;
        var archetype = sharedCatalog.GetArchetype(targetId);
        
        if (archetype != null)
        {
            if (string.IsNullOrEmpty(objectName) || objectName == "Supply Crate") objectName = archetype.defaultName;
            if (string.IsNullOrEmpty(description) || description == "A metal crate that might contain useful items") description = archetype.defaultDescription;
            
            if (currentState == ObjectState.Usable && archetype.defaultState != ObjectState.Usable)
            {
                currentState = archetype.defaultState;
            }
            
            if (contextTags == null || contextTags.Length == 0 || (contextTags.Length == 3 && contextTags[0] == "container"))
            {
                contextTags = archetype.defaultContextTags;
            }
            
            foreach (var template in archetype.affordances)
            {
                if (!actionAffordances.Exists(a => a.actionName.Equals(template.actionName, System.StringComparison.OrdinalIgnoreCase)))
                {
                    var newAffordance = new ActionAffordance
                    {
                        actionName = template.actionName,
                        description = template.description,
                        requiredStates = (ObjectState[])template.requiredStates.Clone(),
                        resultingState = template.resultingState,
                        consumeItems = template.consumeItems,
                        estimatedDuration = template.estimatedDuration
                    };
                    
                    if (template.requiredItems != null)
                    {
                        newAffordance.requiredItems = new RequiredItem[template.requiredItems.Length];
                        for (int i = 0; i < template.requiredItems.Length; i++)
                        {
                            newAffordance.requiredItems[i] = new RequiredItem
                            {
                                itemId = template.requiredItems[i].itemId,
                                quantity = template.requiredItems[i].quantity,
                                consumed = template.requiredItems[i].consumed
                            };
                        }
                    }
                    
                    if (template.yieldItems != null)
                    {
                        newAffordance.yieldItems = new YieldItem[template.yieldItems.Length];
                        for (int i = 0; i < template.yieldItems.Length; i++)
                        {
                            newAffordance.yieldItems[i] = new YieldItem
                            {
                                itemId = template.yieldItems[i].itemId,
                                minQuantity = template.yieldItems[i].minQuantity,
                                maxQuantity = template.yieldItems[i].maxQuantity,
                                dropChance = template.yieldItems[i].dropChance,
                                dropInWorld = template.yieldItems[i].dropInWorld
                            };
                        }
                    }
                    
                    actionAffordances.Add(newAffordance);
                }
            }
        }
    }

    public string GetDescription()
    {
        return description;
    }
    
    public void ChangeState(ObjectState newState)
    {
        if (newState != currentState)
        {
            ObjectState oldState = currentState;
            currentState = newState;
            onStateChangedEvent?.Invoke(oldState, newState);
            OnStateChanged(oldState, newState);
        }
    }
    
    private void OnStateChanged(ObjectState oldState, ObjectState newState)
    {
        string context = GetNearbyContext();
        string desc = $"{objectName} changed from {oldState} to {newState} in {context}";

        // Optional legacy event bus integration. In stripped T4-only projects the
        // world-event stack may be absent, so state changes simply stay local.
        TryPublishSpatialStateChange(desc, oldState, newState);
    }
    
    private string GetNearbyContext()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, 5f, LayerMask.GetMask("Context"));
        if (hits.Length > 0)
        {
            var descComp = hits[0].GetComponent("EnvironmentDescriptor");
            if (descComp != null)
            {
                var descField = descComp.GetType().GetField("description");
                var descValue = descField?.GetValue(descComp) as string;
                if (!string.IsNullOrWhiteSpace(descValue))
                {
                    return descValue;
                }
            }
            return hits[0].gameObject.name;
        }
        return "unknown location";
    }

    private void TryPublishSpatialStateChange(string description, ObjectState oldState, ObjectState newState)
    {
        var worldEventType = ResolveType("MLA_SIM.WorldEvent", "WorldEvent");
        var eventBusType = ResolveType("MLA_SIM.GlobalEventBus", "GlobalEventBus");
        if (worldEventType == null || eventBusType == null)
        {
            return;
        }

        var publishMethod = eventBusType.GetMethod(
            "PublishSpatialEvent",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null,
            new[] { worldEventType, typeof(Transform), typeof(float) },
            null);
        if (publishMethod == null)
        {
            return;
        }

        var eventData = System.Activator.CreateInstance(worldEventType);
        SetField(worldEventType, eventData, "eventType", "spatial");
        SetField(worldEventType, eventData, "description", description);
        SetField(worldEventType, eventData, "position", transform.position);
        SetField(worldEventType, eventData, "perceptionRange", 10f);
        SetField(worldEventType, eventData, "sourceAgentId", "environment");
        SetField(worldEventType, eventData, "parameters", new Dictionary<string, object>
        {
            { "objectId", gameObject.name },
            { "oldState", oldState.ToString() },
            { "newState", newState.ToString() }
        });

        publishMethod.Invoke(null, new object[] { eventData, transform, 10f });
    }

    private static System.Type ResolveType(params string[] candidates)
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

    private static void SetField(System.Type type, object instance, string fieldName, object value)
    {
        var field = type.GetField(fieldName);
        if (field != null)
        {
            field.SetValue(instance, value);
        }
    }
    
    void Update()
    {
        // Check reservation timeout
        if (isReserved && Time.time - reservationStartTime > reservationTimeout)
        {
            Debug.Log($"Reservation timeout for {objectName}, releasing reservation");
            ReleaseReservation();
        }
    }
    
    /// <summary>
    /// Check if action can be performed on this object with agent's inventory
    /// </summary>
    public AffordanceCheckResult CanPerformAction(string actionName, AgentInventory agentInventory, string agentId)
    {
        var result = new AffordanceCheckResult();
        string objLabel = string.IsNullOrEmpty(objectName) ? gameObject.name : objectName;
        string actionVerb = ToActionVerb(actionName);

        // Check if object supports this action
        var affordance = actionAffordances.FirstOrDefault(a => 
            a.actionName.Equals(actionName, System.StringComparison.OrdinalIgnoreCase));
        
        if (affordance == null)
        {
            result.canPerform = false;
            var supported = GetSupportedActions();
            string supportedClause = (supported != null && supported.Count > 0)
                ? $" It supports: {string.Join(", ", supported)}."
                : " It has no defined interactions.";
            result.failureReason = $"You cannot {actionVerb} {objLabel}.{supportedClause}";
            return result;
        }
        
        // Check object state requirements
        if (affordance.requiredStates.Length > 0 && !affordance.requiredStates.Contains(currentState))
        {
            result.canPerform = false;
            string requiredStatesText = string.Join(" or ", affordance.requiredStates).ToLower();
            result.failureReason = $"You cannot {actionVerb} {objLabel} right now: it must be {requiredStatesText} but is currently {currentState.ToString().ToLower()}.";
            return result;
        }
        
        // Check reservation
        if (isReserved && reservedByAgentId != agentId)
        {
            result.canPerform = false;
            result.failureReason = $"{objLabel} is currently in use by {reservedByAgentId}.";
            return result;
        }
        
        // Check required items
        if (affordance.requiredItems.Length > 0 && agentInventory != null)
        {
            foreach (var requiredItem in affordance.requiredItems)
            {
                if (!agentInventory.HasItem(requiredItem.itemId, requiredItem.quantity))
                {
                    result.canPerform = false;
                    string qty = requiredItem.quantity > 1 ? $" x{requiredItem.quantity}" : "";
                    string stateClause = (affordance.requiredStates.Length > 0 && !affordance.requiredStates.Contains(InteractableObject.ObjectState.Usable))
                        ? $" (currently {currentState.ToString().ToLower()})"
                        : "";
                    result.failureReason = $"You need {requiredItem.itemId}{qty} to {actionVerb} {objLabel}{stateClause}.";
                    result.missingItems.Add(requiredItem);
                    return result;
                }
            }
        }
        
        result.canPerform = true;
        result.affordance = affordance;
        return result;
    }

    /// <summary>
    /// Convert an action name (e.g. "Fix", "InteractWith", "Pickup") into a lowercase
    /// verb suitable for first-person natural-language failure messages.
    /// </summary>
    private static string ToActionVerb(string actionName)
    {
        if (string.IsNullOrEmpty(actionName)) return "interact with";
        switch (actionName.ToLowerInvariant())
        {
            case "interactwith": return "interact with";
            case "pickup": return "pick up";
            case "talkto": return "talk to";
            case "sleepat": return "sleep at";
            case "hideat": return "hide at";
            case "moveto":
            case "goto": return "move to";
            default: return actionName.ToLowerInvariant();
        }
    }
    
    /// <summary>
    /// Reserve object for specific agent
    /// </summary>
    public bool TryReserve(string agentId, float timeoutOverride = -1f)
    {
        if (isReserved && reservedByAgentId != agentId)
        {
            return false; // Already reserved by someone else
        }
        
        isReserved = true;
        reservedByAgentId = agentId;
        reservationStartTime = Time.time;

        if (timeoutOverride > 0)
        {
            reservationTimeout = timeoutOverride;
        }

        // A1.6: object holdSeconds is authoritative. Never let the reservation
        // expire before the interaction's hold (+ margin) has elapsed.
        float minTimeout = EffectiveHoldTimeout();
        if (minTimeout > reservationTimeout)
        {
            reservationTimeout = minTimeout;
        }

        Debug.Log($"Object {objectName} reserved by {agentId} for {reservationTimeout}s");
        return true;
    }
    
    /// <summary>
    /// Release reservation
    /// </summary>
    public void ReleaseReservation()
    {
        if (isReserved)
        {
            Debug.Log($"Released reservation on {objectName} from {reservedByAgentId}");
            isReserved = false;
            reservedByAgentId = "";
        }
    }
    
    /// <summary>
    /// Check if object is reserved
    /// </summary>
    public bool IsReserved()
    {
        return isReserved;
    }
    
    /// <summary>
    /// Check if specific agent has reservation
    /// </summary>
    public bool IsReservedBy(string agentId)
    {
        return isReserved && reservedByAgentId == agentId;
    }
    
    /// <summary>
    /// Get all supported actions
    /// </summary>
    public List<string> GetSupportedActions()
    {
        return actionAffordances.Select(a => a.actionName).ToList();
    }
    
    /// <summary>
    /// Legacy compatibility method
    /// </summary>
    public bool CanInteract(string requiredTool = "")
    {
        if (currentState != ObjectState.Usable) return false;
        
        // Check if any affordance supports basic interaction
        var interactAffordance = actionAffordances.FirstOrDefault(a => 
            a.actionName.Equals("InteractWith", System.StringComparison.OrdinalIgnoreCase) ||
            a.actionName.Equals("Use", System.StringComparison.OrdinalIgnoreCase));
        
        if (interactAffordance == null) return true; // No specific requirements
        
        // Simple tool check for legacy compatibility
        if (interactAffordance.requiredItems.Length > 0 && string.IsNullOrEmpty(requiredTool))
        {
            return false;
        }
        
        return true;
    }
}

/// <summary>
/// Defines what actions an object supports and their requirements
/// </summary>
[System.Serializable]
public class ActionAffordance
{
    [Header("Action Definition")]
    [RegistryDropdown(RegistryType.Action)]
    public string actionName = "InteractWith";
    [TextArea(2, 3)]
    public string description = "General interaction with this object";
    
    [Header("Requirements")]
    public RequiredItem[] requiredItems = {};
    public InteractableObject.ObjectState[] requiredStates = { InteractableObject.ObjectState.Usable };
    
    [Header("Effects")]
    public InteractableObject.ObjectState resultingState = InteractableObject.ObjectState.Usable;
    public bool consumeItems = false;
    public YieldItem[] yieldItems = {};
    public float estimatedDuration = 2f;
}

/// <summary>
/// Item yielded when an action is performed
/// </summary>
[System.Serializable]
public class YieldItem
{
    [RegistryDropdown(RegistryType.Item)]
    public string itemId = "";
    public int minQuantity = 1;
    public int maxQuantity = 1;
    [Range(0f, 1f)]
    public float dropChance = 1f;
    [Tooltip("If true, drops in world. If false, tries to add to inventory first.")]
    public bool dropInWorld = false;
}

/// <summary>
/// Required item for an action
/// </summary>
[System.Serializable]
public class RequiredItem
{
    [RegistryDropdown(RegistryType.Item)]
    public string itemId = "";
    public int quantity = 1;
    [Tooltip("Should this item be consumed when action is performed?")]
    public bool consumed = false;
}

/// <summary>
/// Result of affordance checking
/// </summary>
public class AffordanceCheckResult
{
    public bool canPerform = false;
    public string failureReason = "";
    public ActionAffordance affordance = null;
    public List<RequiredItem> missingItems = new List<RequiredItem>();
} 
