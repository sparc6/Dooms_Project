using System.Collections.Generic;
using NodeCanvas.Framework;
using ParadoxNotion;
using UnityEngine;

namespace MLA_SIM.Dooms.Scenes.Nodes
{
    public abstract class ScenePhaseNode : Node
    {
        [UnityEngine.Header("Phase Configuration")]
        public string phaseId = "NewPhase";
        public float minDurationSec = 5f;
        public float maxDurationSec = 60f;

        [UnityEngine.Header("Roles and Choreography")]
        public List<RoleSlot> roles = new List<RoleSlot>();

        public override bool allowAsPrime => true;
        public override bool canSelfConnect => false;
        public override int maxInConnections => -1;
        public override int maxOutConnections => 1;
        public override System.Type outConnectionType => typeof(SceneConnection);
        public override Alignment2x2 commentsAlignment => Alignment2x2.Bottom;
        public override Alignment2x2 iconAlignment => Alignment2x2.Bottom;

        public virtual void OnEnter(SceneRuntimeContext ctx)
        {
            Debug.Log($"[DOOMS][SceneDirector] Entering phase node '{phaseId}' ({GetType().Name})");
        }

        public virtual void OnTick(SceneRuntimeContext ctx, float dt)
        {
        }

        public virtual void OnExit(SceneRuntimeContext ctx)
        {
            Debug.Log($"[DOOMS][SceneDirector] Exiting phase node '{phaseId}'");
        }

        public virtual bool ShouldAdvance(SceneRuntimeContext ctx)
        {
            return ctx.elapsedInPhase >= maxDurationSec;
        }
    }
}
