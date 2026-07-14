using NodeCanvas.Framework;
using UnityEngine;

namespace MLA_SIM.ModularInteractions
{
    /// <summary>
    /// A6.2 — A visual NodeCanvas connection from ObjectA to ObjectB meaning:
    /// "ObjectB has an edge that is gated on ObjectA being in requiredSourceStateId."
    ///
    /// Replaces the loose conditionObjectName string on InteractionEdge with a
    /// typed, visually authored port connection on the SceneObjectGraph canvas.
    /// SceneObjectGraph.WireConditionNodes() resolves these into conditionNode
    /// references on the relevant InteractionEdges at scene load.
    /// </summary>
    public class ObjectDependencyConnection : Connection
    {
        [Tooltip("The source node's state that must be active for the target edge to unlock.")]
        [RegistryDropdown(RegistryType.ObjectState)]
        [MLA_SIM.Dooms.RegistryDropdownNC((MLA_SIM.Dooms.RegistryType)RegistryType.ObjectState)]
        public string requiredSourceStateId = "";

        [Tooltip("The actionName of the target edge this gate applies to.")]
        [MLA_SIM.Dooms.RegistryDropdownNC((MLA_SIM.Dooms.RegistryType)RegistryType.Action)]
        public string gatedActionName = "";

        [Tooltip("The fromState of the target edge this gate applies to.")]
        [MLA_SIM.Dooms.RegistryDropdownNC((MLA_SIM.Dooms.RegistryType)RegistryType.ObjectState)]
        public string gatedFromStateId = "";
    }
}
