using System;
using UnityEngine;

namespace MLA_SIM.Interactions
{
    /// <summary>
    /// Set the subject InteractableObject to a given state.
    /// </summary>
    [Serializable]
    public class SetSubjectStateEffect : InteractionEffect
    {
        public InteractableObject.ObjectState newState = InteractableObject.ObjectState.Usable;

        public override void Apply(InteractionContext ctx)
        {
            if (ctx.subjectInteractable == null) return;
            ctx.subjectInteractable.ChangeState(newState);
        }

        public override string Describe() => $"Set subject state -> {newState}";
    }

    /// <summary>
    /// Remove a quantity of an item from the actor's inventory.
    /// </summary>
    [Serializable]
    public class ConsumeItemEffect : InteractionEffect
    {
        [CatalogItemId]
        public string itemId = "toolbox";
        public int quantity = 1;

        public override void Apply(InteractionContext ctx)
        {
            if (ctx.actorInventory == null) return;
            ctx.actorInventory.RemoveItem(itemId, quantity);
        }

        public override string Describe() => $"Consume {itemId} x{quantity}";
    }

    /// <summary>
    /// Toggle SetActive on every scene object that matches the descriptor.
    /// Targets are stored as strings because ScriptableObject assets cannot
    /// reliably hold scene GameObject references.
    /// </summary>
    [Serializable]
    public class SetTargetActiveEffect : InteractionEffect
    {
        [Tooltip("Match all instances of this archetype. Leave empty to use targetObjectName instead.")]
        [CatalogArchetypeId]
        public string targetArchetypeId = "";

        [Tooltip("Match a single specific scene object by name. Takes precedence over archetype.")]
        public string targetObjectName = "";

        public bool active = true;

        public override void Apply(InteractionContext ctx)
        {
            foreach (var go in InteractionTargetResolver.ResolveGameObjects(targetArchetypeId, targetObjectName))
            {
                if (go != null) go.SetActive(active);
            }
        }

        public override string Describe()
        {
            string n = !string.IsNullOrEmpty(targetObjectName) ? targetObjectName : targetArchetypeId;
            return $"SetActive({n}, {active})";
        }
    }

    /// <summary>
    /// Force-change the state of every scene InteractableObject matching the descriptor.
    /// </summary>
    [Serializable]
    public class SetTargetStateEffect : InteractionEffect
    {
        [Tooltip("Match all instances of this archetype. Leave empty to use targetObjectName instead.")]
        [CatalogArchetypeId]
        public string targetArchetypeId = "";

        [Tooltip("Match a single specific scene object by name. Takes precedence over archetype.")]
        public string targetObjectName = "";

        public InteractableObject.ObjectState newState = InteractableObject.ObjectState.Usable;

        public override void Apply(InteractionContext ctx)
        {
            foreach (var io in InteractionTargetResolver.Resolve(targetArchetypeId, targetObjectName))
            {
                if (io != null) io.ChangeState(newState);
            }
        }

        public override string Describe()
        {
            string n = !string.IsNullOrEmpty(targetObjectName) ? targetObjectName : targetArchetypeId;
            return $"Set {n} -> {newState}";
        }
    }

    /// <summary>
    /// Emit a named signal that downstream systems (audio, VFX, analytics,
    /// agent perception) can subscribe to. Decouples gameplay reactions
    /// from the interaction graph itself.
    /// </summary>
    [Serializable]
    public class EmitSignalEffect : InteractionEffect
    {
        public string signal = "rule.fired";

        public override void Apply(InteractionContext ctx)
        {
            ctx.emittedSignals.Add(signal);
        }

        public override string Describe() => $"Emit '{signal}'";
    }

    /// <summary>
    /// Yield an item as a result of the interaction. Either grants the item
    /// directly to the actor's inventory, or instantiates the item's
    /// drop prefab at the subject's position.
    /// </summary>
    [Serializable]
    public class YieldItemEffect : InteractionEffect
    {
        public enum YieldMode
        {
            ToInventory,
            DropAtSubject,
        }

        [CatalogItemId]
        public string itemId = "";

        public int quantity = 1;

        [Tooltip("ToInventory: adds to actor inventory. DropAtSubject: instantiates the item's dropPrefab at the subject's position (if the catalog provides one).")]
        public YieldMode mode = YieldMode.ToInventory;

        public override void Apply(InteractionContext ctx)
        {
            if (string.IsNullOrEmpty(itemId) || quantity <= 0) return;

            if (mode == YieldMode.ToInventory)
            {
                if (ctx.actorInventory != null)
                {
                    ctx.actorInventory.AddItem(itemId, quantity);
                }
                return;
            }

            // DropAtSubject: try to find the catalog dropPrefab via the
            // shared catalog on the subject's InteractableObject (if any).
            if (ctx.subjectInteractable == null) return;
            var catalog = ctx.subjectInteractable.GetSharedCatalog();
            if (catalog == null) return;
            var def = catalog.GetItemDefinition(itemId);
            if (def == null || def.dropPrefab == null) return;

            var pos = ctx.subjectInteractable.transform.position + UnityEngine.Vector3.up * 0.2f;
            for (int i = 0; i < quantity; i++)
            {
                UnityEngine.Object.Instantiate(def.dropPrefab, pos, UnityEngine.Quaternion.identity);
            }
        }

        public override string Describe()
        {
            string m = mode == YieldMode.ToInventory ? "to inventory" : "drop at subject";
            return $"Yield {itemId} x{quantity} ({m})";
        }
    }

    /// <summary>
    /// Trigger an EventSource component on a named scene GameObject with a
    /// custom description, optionally repeating N times at a fixed interval.
    /// Resolves the EventSource via its static registry at fire time, so the
    /// rule asset only needs to store the GameObject's name.
    /// </summary>
    [Serializable]
    public class TriggerEventSourceEffect : InteractionEffect
    {
        [SceneEventSourceName]
        [Tooltip("Name of the scene GameObject that carries the EventSource.")]
        public string targetObjectName = "";

        [TextArea(2, 4)]
        [Tooltip("Custom description broadcast on the WorldEvent. Empty falls back to the EventSource default.")]
        public string customDescription = "";

        [Tooltip("How many times the event fires when the rule resolves. <= 1 fires once.")]
        public int repeatCount = 1;

        [Tooltip("Seconds between repeats. 0 fires them all on the same frame.")]
        public float repeatIntervalSeconds = 0f;

        public override void Apply(InteractionContext ctx)
        {
            if (string.IsNullOrEmpty(targetObjectName)) return;

            var eventSourceType = ResolveEventSourceType();
            if (eventSourceType == null)
            {
                UnityEngine.Debug.LogWarning(
                    $"[TriggerEventSourceEffect] EventSource type is unavailable in this project.");
                return;
            }

            var findByName = eventSourceType.GetMethod(
                "FindByName",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var triggerRepeating = eventSourceType.GetMethod(
                "TriggerCustomRepeating",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (findByName == null || triggerRepeating == null)
            {
                return;
            }

            var src = findByName.Invoke(null, new object[] { targetObjectName });
            if (src == null)
            {
                UnityEngine.Debug.LogWarning(
                    $"[TriggerEventSourceEffect] No active EventSource named '{targetObjectName}'.");
                return;
            }

            int count = UnityEngine.Mathf.Max(1, repeatCount);
            triggerRepeating.Invoke(src, new object[] { customDescription, count, repeatIntervalSeconds });
        }

        public override string Describe()
        {
            string preview = string.IsNullOrEmpty(customDescription)
                ? "(default desc)"
                : (customDescription.Length > 28 ? customDescription.Substring(0, 28) + "..." : customDescription);
            string repeat = repeatCount > 1
                ? $" x{repeatCount}@{repeatIntervalSeconds}s"
                : "";
            return $"Trigger '{targetObjectName}' \"{preview}\"{repeat}";
        }

        private static System.Type ResolveEventSourceType()
        {
            var type = System.Type.GetType("EventSource, Assembly-CSharp") ?? System.Type.GetType("EventSource");
            if (type != null)
            {
                return type;
            }

            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType("EventSource");
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
