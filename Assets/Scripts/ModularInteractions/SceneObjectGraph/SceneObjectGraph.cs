using NodeCanvas.Framework;
using ParadoxNotion;
using ParadoxNotion.Design;
using UnityEngine;

namespace MLA_SIM.ModularInteractions
{
    /// <summary>
    /// A6.2 — NodeCanvas Graph asset that holds all SceneObjectNodes for a scene.
    /// One asset per scene. Double-click in the Project window to open the canvas.
    ///
    /// Each node owns one InteractableObject's full state machine. Cross-object
    /// dependency gates (formerly loose conditionObjectName strings) are expressed
    /// as ObjectDependencyConnection ports drawn on the canvas.
    ///
    /// At scene load, SceneObjectGraphRunner calls WireConditionNodes() to resolve
    /// connection ports into typed conditionNode references on InteractionEdges.
    /// </summary>
    [GraphInfo(
        packageName = "MLA SIM",
        docsURL = "",
        resourcesURL = "")]
    [CreateAssetMenu(
        fileName = "SceneObjectGraph",
        menuName = "MLA SIM/Scene Object Graph")]
    public class SceneObjectGraph : Graph
    {
        public override System.Type baseNodeType      => typeof(SceneObjectNodeBase);
        public override bool requiresAgent           => false;
        public override bool requiresPrimeNode       => false;
        public override bool isTree                  => false;
        public override bool allowBlackboardOverrides => false;
        public override bool canAcceptVariableDrops  => false;
        public override PlanarDirection flowDirection => PlanarDirection.Horizontal;
        public override string ToString()            => name;

        // ── Runtime helpers ────────────────────────────────────────────

        /// <summary>
        /// Find the SceneObjectNode whose objectId matches (case-insensitive).
        /// </summary>
        public SceneObjectNode FindObjectNode(string objectId)
        {
            if (string.IsNullOrEmpty(objectId)) return null;
            foreach (var node in allNodes)
            {
                if (node is SceneObjectNode n &&
                    string.Equals(n.objectId, objectId, System.StringComparison.OrdinalIgnoreCase))
                    return n;
            }
            return null;
        }

        /// <summary>
        /// Called by SceneObjectGraphRunner.OnEnable. Initialises each node's
        /// currentStateId and resolves ObjectDependencyConnections into typed
        /// conditionNode references on the relevant InteractionEdges.
        /// </summary>
        public void WireConditionNodes()
        {
            foreach (var node in allNodes)
            {
                if (node is not SceneObjectNode objNode) continue;

                // Initialise runtime state.
                if (string.IsNullOrEmpty(objNode.currentStateId))
                    objNode.currentStateId = objNode.initialStateId;

                // Wire dependency connections.
                foreach (var conn in node.inConnections)
                {
                    if (conn is not ObjectDependencyConnection depConn) continue;
                    var sourceNode = depConn.sourceNode as SceneObjectNode;
                    if (sourceNode == null) continue;

                    if (!string.IsNullOrWhiteSpace(depConn.gatedActionName) && !string.IsNullOrWhiteSpace(depConn.gatedFromStateId))
                    {
                        var edge = objNode.FindEdge(depConn.gatedFromStateId, depConn.gatedActionName);
                        if (edge != null)
                        {
                            edge.conditionNode = sourceNode;
                            edge.conditionStateId = depConn.requiredSourceStateId;
                            edge.conditionObjectName = "";
                            continue;
                        }
                    }

                    foreach (var state in objNode.states)
                    {
                        foreach (var edge in state.edges)
                        {
                            if (!string.IsNullOrEmpty(edge.conditionObjectName) &&
                                string.Equals(edge.conditionObjectName, sourceNode.objectId,
                                    System.StringComparison.OrdinalIgnoreCase))
                            {
                                edge.conditionNode = sourceNode;
                                edge.conditionStateId = depConn.requiredSourceStateId;
                                edge.conditionObjectName = "";
                            }
                        }
                    }
                }
            }
        }
    }
}
