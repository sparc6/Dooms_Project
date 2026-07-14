using NodeCanvas.Framework;
using ParadoxNotion;
using ParadoxNotion.Design;
using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.ModularInteractions
{
    public abstract class SceneObjectNodeBase : Node
    {
    }

    /// <summary>
    /// A6.2 — A NodeCanvas node that owns the full state machine for one
    /// InteractableObject in the scene. All state transitions and edges for that
    /// object live inside this node; cross-object dependency wiring is expressed
    /// as ObjectDependencyConnection ports on the canvas.
    /// </summary>
    [Name("Interactable Object")]
    [Description("Holds the state machine for one InteractableObject in the scene.")]
    [Color("3e8fc4")]
    public partial class SceneObjectNode : SceneObjectNodeBase
    {
        // ── Identity ──────────────────────────────────────────────────
        [Tooltip("Must match a registered SceneObjectGraph object id exactly.")]
        [RegistryDropdown(RegistryType.ObjectId)]
        [MLA_SIM.Dooms.RegistryDropdownNC((MLA_SIM.Dooms.RegistryType)RegistryType.ObjectId)]
        public string objectId = "";

        [UnityEngine.Header("Scene / Catalog Metadata")]
        public string displayName = "";
        public string sourceObjectName = "";
        public string archetypeId = "";
        [TextArea(2, 4)]
        public string description = "";
        [TextArea(2, 4)]
        public string environmentHint = "";
        public List<string> contextTags = new List<string>();
        public Vector3 sceneWorldPosition;
        public string sourceSceneName = "";

        // ── State machine ─────────────────────────────────────────────
        [RegistryDropdown(RegistryType.ObjectState)]
        [MLA_SIM.Dooms.RegistryDropdownNC((MLA_SIM.Dooms.RegistryType)RegistryType.ObjectState)]
        public string initialStateId = "Usable";
        public List<InteractionStateNode> states = new List<InteractionStateNode>();

        // ── Runtime state (not serialized) ───────────────────────────
        [System.NonSerialized]
        public string currentStateId = "";

        // ── NodeCanvas abstract members ───────────────────────────────
        public override bool allowAsPrime     => false;
        public override bool canSelfConnect   => false;
        public override Alignment2x2 commentsAlignment => Alignment2x2.Bottom;
        public override Alignment2x2 iconAlignment     => Alignment2x2.Bottom;
        public override int maxInConnections  => -1;
        public override int maxOutConnections => -1;
        public override System.Type outConnectionType => typeof(ObjectDependencyConnection);

        // ─────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        protected override void OnNodeInspectorGUI()
        {
            UnityEditor.EditorGUILayout.LabelField("Identity", UnityEditor.EditorStyles.boldLabel);
            objectId = DrawRegistryPopup("Object Id", objectId, RegistryType.ObjectId);
            displayName = UnityEditor.EditorGUILayout.TextField("Display Name", displayName);
            sourceObjectName = UnityEditor.EditorGUILayout.TextField("Source Object Name", sourceObjectName);
            archetypeId = UnityEditor.EditorGUILayout.TextField("Archetype Id", archetypeId);
            initialStateId = DrawRegistryPopup("Initial State", initialStateId, RegistryType.ObjectState);
            UnityEditor.EditorGUILayout.LabelField("Current Runtime State", string.IsNullOrWhiteSpace(currentStateId) ? "<not initialized>" : currentStateId);
            description = UnityEditor.EditorGUILayout.TextArea(description, UnityEngine.GUILayout.MinHeight(48f));
            environmentHint = UnityEditor.EditorGUILayout.TextArea(environmentHint, UnityEngine.GUILayout.MinHeight(48f));

            UnityEditor.EditorGUILayout.Space(6f);
            UnityEditor.EditorGUILayout.LabelField("Context Tags", UnityEditor.EditorStyles.boldLabel);
            contextTags = new List<string>(DrawRegistryArray("Tags", contextTags != null ? contextTags.ToArray() : new string[0], RegistryType.ContextTag));

            UnityEditor.EditorGUILayout.Space(6f);
            UnityEditor.EditorGUILayout.LabelField("State Machine", UnityEditor.EditorStyles.boldLabel);
            if (states == null) states = new List<InteractionStateNode>();

            for (int i = 0; i < states.Count; i++)
            {
                var state = states[i];
                if (state == null)
                {
                    states[i] = new InteractionStateNode();
                    state = states[i];
                }

                UnityEditor.EditorGUILayout.BeginVertical(UnityEditor.EditorStyles.helpBox);
                UnityEditor.EditorGUILayout.BeginHorizontal();
                state.stateId = DrawRegistryPopup("State Id", state.stateId, RegistryType.ObjectState);
                if (UnityEngine.GUILayout.Button("Remove", UnityEngine.GUILayout.Width(68f)))
                {
                    states.RemoveAt(i);
                    UnityEditor.EditorGUILayout.EndHorizontal();
                    UnityEditor.EditorGUILayout.EndVertical();
                    break;
                }
                UnityEditor.EditorGUILayout.EndHorizontal();

                state.position = UnityEditor.EditorGUILayout.Vector2Field("Canvas Position", state.position);
                DrawEdges(state);

                UnityEditor.EditorGUILayout.EndVertical();
            }

            if (UnityEngine.GUILayout.Button("+ Add State"))
            {
                states.Add(new InteractionStateNode { stateId = string.IsNullOrWhiteSpace(initialStateId) ? "Usable" : initialStateId });
            }
        }

        private static string DrawRegistryPopup(string label, string currentValue, RegistryType type)
        {
            var options = GetRegistryOptions(type);
            if (options == null || options.Length == 0)
                return UnityEditor.EditorGUILayout.TextField(label, currentValue);

            var list = new List<string>(options);
            int index = list.IndexOf(currentValue);
            if (index < 0)
            {
                if (!string.IsNullOrWhiteSpace(currentValue))
                {
                    list.Add(currentValue + "  (unregistered)");
                    index = list.Count - 1;
                }
                else
                {
                    index = 0;
                }
            }

            int newIndex = UnityEditor.EditorGUILayout.Popup(label, index, list.ToArray());
            if (newIndex >= 0 && newIndex < list.Count)
            {
                bool pickedSentinel = !string.IsNullOrWhiteSpace(currentValue)
                    && newIndex == list.Count - 1
                    && list[newIndex].EndsWith("(unregistered)");
                if (!pickedSentinel)
                    return list[newIndex];
            }

            return currentValue;
        }

        private static string[] DrawRegistryArray(string label, string[] values, RegistryType type)
        {
            var options = GetRegistryOptions(type);
            UnityEditor.EditorGUILayout.BeginVertical(UnityEditor.EditorStyles.helpBox);
            UnityEditor.EditorGUILayout.LabelField(label, UnityEditor.EditorStyles.boldLabel);

            var list = new List<string>(values ?? new string[0]);
            if (options != null && options.Length > 0)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    UnityEditor.EditorGUILayout.BeginHorizontal();
                    int index = System.Array.IndexOf(options, list[i]);
                    if (index < 0) index = 0;
                    int newIndex = UnityEditor.EditorGUILayout.Popup(UnityEngine.GUIContent.none, index, options);
                    if (newIndex >= 0 && newIndex < options.Length)
                        list[i] = options[newIndex];
                    if (UnityEngine.GUILayout.Button("-", UnityEngine.GUILayout.Width(22f)))
                    {
                        list.RemoveAt(i);
                        UnityEditor.EditorGUILayout.EndHorizontal();
                        break;
                    }
                    UnityEditor.EditorGUILayout.EndHorizontal();
                }

                if (UnityEngine.GUILayout.Button("+"))
                    list.Add(options[0]);
            }
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    UnityEditor.EditorGUILayout.BeginHorizontal();
                    list[i] = UnityEditor.EditorGUILayout.TextField(UnityEngine.GUIContent.none, list[i]);
                    if (UnityEngine.GUILayout.Button("-", UnityEngine.GUILayout.Width(22f)))
                    {
                        list.RemoveAt(i);
                        UnityEditor.EditorGUILayout.EndHorizontal();
                        break;
                    }
                    UnityEditor.EditorGUILayout.EndHorizontal();
                }

                if (UnityEngine.GUILayout.Button("+"))
                    list.Add("");
            }

            UnityEditor.EditorGUILayout.EndVertical();
            return list.ToArray();
        }

        private void DrawEdges(InteractionStateNode state)
        {
            if (state.edges == null) state.edges = new List<InteractionEdge>();
            UnityEditor.EditorGUILayout.Space(4f);
            UnityEditor.EditorGUILayout.LabelField("Edges", UnityEditor.EditorStyles.boldLabel);

            for (int i = 0; i < state.edges.Count; i++)
            {
                var edge = state.edges[i];
                if (edge == null)
                {
                    state.edges[i] = new InteractionEdge();
                    edge = state.edges[i];
                }

                UnityEditor.EditorGUILayout.BeginVertical(UnityEditor.EditorStyles.helpBox);
                UnityEditor.EditorGUILayout.BeginHorizontal();
                edge.actionName = DrawRegistryPopup("Action", edge.actionName, RegistryType.Action);
                if (UnityEngine.GUILayout.Button("Remove", UnityEngine.GUILayout.Width(68f)))
                {
                    state.edges.RemoveAt(i);
                    UnityEditor.EditorGUILayout.EndHorizontal();
                    UnityEditor.EditorGUILayout.EndVertical();
                    break;
                }
                UnityEditor.EditorGUILayout.EndHorizontal();

                edge.resultingStateId = DrawRegistryPopup("Resulting State", edge.resultingStateId, RegistryType.ObjectState);
                edge.requiredItemIds = DrawRegistryArray("Required Items", edge.requiredItemIds, RegistryType.Item);
                edge.yieldItemIds = DrawRegistryArray("Yield Items", edge.yieldItemIds, RegistryType.Item);
                edge.sequenceId = DrawRegistryPopup("Sequence", edge.sequenceId, RegistryType.AnimationSequence);
                edge.animatorStateName = DrawRegistryPopup("Animator State", edge.animatorStateName, RegistryType.AnimationState);
                edge.holdSeconds = UnityEditor.EditorGUILayout.FloatField("Hold (sec)", edge.holdSeconds);
                edge.conditionStateId = DrawRegistryPopup("Dependency State", edge.conditionStateId, RegistryType.ObjectState);
                edge.successMessage = UnityEditor.EditorGUILayout.TextField("Success Message", edge.successMessage);
                edge.failureMessage = UnityEditor.EditorGUILayout.TextField("Failure Message", edge.failureMessage);

                if (edge.conditionNode != null)
                    UnityEditor.EditorGUILayout.LabelField("Gated By", edge.conditionNode.objectId + " / " + edge.conditionStateId);

                UnityEditor.EditorGUILayout.EndVertical();
            }

            if (UnityEngine.GUILayout.Button("+ Add Edge"))
            {
                state.edges.Add(new InteractionEdge { actionName = "InteractWith", resultingStateId = state.stateId });
            }
        }

        private static string[] GetRegistryOptions(RegistryType type)
        {
            switch (type)
            {
                case RegistryType.Faction:
                {
                    var reg = MLA_SIM.Dooms.Registries.FactionRegistrySO.Instance;
                    if (reg?.factions == null) return System.Array.Empty<string>();
                    var list = new List<string>();
                    foreach (var faction in reg.factions)
                    {
                        if (faction != null && !string.IsNullOrWhiteSpace(faction.factionId))
                            list.Add(faction.factionId.Trim());
                    }
                    return list.ToArray();
                }
                case RegistryType.InteractionPoint:
                {
                    var reg = MLA_SIM.Dooms.Registries.InteractionPointRegistrySO.Instance;
                    if (reg?.pointTags == null) return System.Array.Empty<string>();
                    return reg.pointTags.ToArray();
                }
                case RegistryType.Scene:
                {
                    var reg = MLA_SIM.Dooms.Registries.SceneRegistrySO.Instance;
                    if (reg?.scenes == null) return System.Array.Empty<string>();
                    var list = new List<string>();
                    foreach (var scene in reg.scenes)
                    {
                        if (scene != null && !string.IsNullOrWhiteSpace(scene.sceneId))
                            list.Add(scene.sceneId.Trim());
                    }
                    return list.ToArray();
                }
                case RegistryType.AnimationState:
                {
                    var reg = MLA_SIM.Dooms.Registries.AnimationStateRegistrySO.Instance;
                    if (reg?.states == null) return System.Array.Empty<string>();
                    var list = new List<string>();
                    foreach (var state in reg.states)
                    {
                        if (!string.IsNullOrWhiteSpace(state))
                            list.Add(state.Trim());
                    }
                    return list.ToArray();
                }
                case RegistryType.AnimationSequence:
                {
                    var reg = MLA_SIM.AnimationSequenceRegistry.Instance;
                    if (reg?.sequences == null) return System.Array.Empty<string>();
                    var list = new List<string>();
                    foreach (var seq in reg.sequences)
                    {
                        if (seq != null && !string.IsNullOrWhiteSpace(seq.sequenceId))
                            list.Add(seq.sequenceId.Trim());
                    }
                    return list.ToArray();
                }
                case RegistryType.Prop:
                {
                    var reg = MLA_SIM.Dooms.Registries.PropRegistrySO.Instance;
                    if (reg?.props == null) return System.Array.Empty<string>();
                    var list = new List<string>();
                    foreach (var prop in reg.props)
                    {
                        if (prop != null && !string.IsNullOrWhiteSpace(prop.propId))
                            list.Add(prop.propId.Trim());
                    }
                    return list.ToArray();
                }
                case RegistryType.ContextTag:
                    return MLA_SIM.InteractableCatalog.Instance != null ? MLA_SIM.InteractableCatalog.Instance.GetContextTags() : System.Array.Empty<string>();
                case RegistryType.Action:
                    return MLA_SIM.InteractableCatalog.Instance != null ? MLA_SIM.InteractableCatalog.Instance.GetActionVocabulary() : System.Array.Empty<string>();
                case RegistryType.Item:
                    return MLA_SIM.InteractableCatalog.Instance != null ? MLA_SIM.InteractableCatalog.Instance.GetItemIds() : System.Array.Empty<string>();
                case RegistryType.ObjectArchetype:
                    return MLA_SIM.InteractableCatalog.Instance != null ? MLA_SIM.InteractableCatalog.Instance.GetArchetypeIds() : System.Array.Empty<string>();
                case RegistryType.ObjectId:
                    return MLA_SIM.InteractableCatalog.Instance != null ? MLA_SIM.InteractableCatalog.Instance.GetRegisteredObjectIds() : System.Array.Empty<string>();
                case RegistryType.ObjectState:
                    return MLA_SIM.InteractableCatalog.Instance != null ? MLA_SIM.InteractableCatalog.Instance.GetObjectStateIds() : System.Enum.GetNames(typeof(InteractableObject.ObjectState));
                default:
                    return System.Array.Empty<string>();
            }
        }
#endif

        public InteractionStateNode FindState(string stateId)
        {
            if (string.IsNullOrEmpty(stateId) || states == null) return null;
            return states.Find(n => n != null &&
                string.Equals(n.stateId, stateId, System.StringComparison.OrdinalIgnoreCase));
        }

        public InteractionEdge FindEdge(string fromStateId, string actionName)
        {
            var stateNode = FindState(fromStateId);
            if (stateNode == null || stateNode.edges == null) return null;
            return stateNode.edges.Find(e => e != null &&
                string.Equals(e.actionName, actionName, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
