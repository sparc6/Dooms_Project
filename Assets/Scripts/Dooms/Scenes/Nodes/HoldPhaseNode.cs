using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace MLA_SIM.Dooms.Scenes.Nodes
{
    [Name("Hold")]
    [Category("DOOMS")]
    [Description("Wait for the specified duration while agents remain at their interaction points.")]
    [Color("88ff00")]
    public class HoldPhaseNode : ScenePhaseNode
    {
        public override bool ShouldAdvance(SceneRuntimeContext ctx)
        {
            // Simply advance when we exceed the duration
            return ctx.elapsedInPhase >= maxDurationSec;
        }
    }
}
