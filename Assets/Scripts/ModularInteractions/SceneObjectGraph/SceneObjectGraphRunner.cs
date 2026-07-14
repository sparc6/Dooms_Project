using UnityEngine;

namespace MLA_SIM.ModularInteractions
{
    /// <summary>
    /// A6.3 — Drop one of these in the scene and assign a SceneObjectGraph asset.
    /// Replaces SceneInteractionRules + per-object SceneInteractionGraph components.
    ///
    /// On enable, calls WireConditionNodes to resolve ObjectDependencyConnections
    /// into typed conditionNode references and initialise per-node currentStateId.
    /// AgentActionSystem.InteractWith reads ActiveGraph to route interactions.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MLA SIM/Scene Object Graph Runner")]
    public class SceneObjectGraphRunner : MonoBehaviour
    {
        public SceneObjectGraph graph;

        private static SceneObjectGraphRunner _active;

        public static SceneObjectGraph ActiveGraph =>
            _active != null ? _active.graph : null;

        void OnEnable()
        {
            _active = this;
            graph?.WireConditionNodes();
        }

        void OnDisable()
        {
            if (_active == this) _active = null;
        }
    }
}
