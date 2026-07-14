using NodeCanvas.Framework;
using ParadoxNotion;
using ParadoxNotion.Design;
using UnityEngine;

namespace MLA_SIM.Dooms.Scenes
{
    [GraphInfo(packageName = "DOOMS", docsURL = "", resourcesURL = "")]
    [CreateAssetMenu(fileName = "NewSceneGraph", menuName = "DOOMS/Scene Graph")]
    public class SceneGraph : Graph
    {
        public override System.Type baseNodeType => typeof(Nodes.ScenePhaseNode);
        public override bool requiresAgent => false;
        public override bool requiresPrimeNode => true;
        public override bool isTree => false;
        public override bool allowBlackboardOverrides => false;
        public override bool canAcceptVariableDrops => false;
        public override PlanarDirection flowDirection => PlanarDirection.Horizontal;
    }
}
