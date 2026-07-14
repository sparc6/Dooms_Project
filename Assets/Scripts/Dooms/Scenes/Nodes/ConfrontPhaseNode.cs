using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace MLA_SIM.Dooms.Scenes.Nodes
{
    [Name("Confront")]
    [Category("DOOMS")]
    [Description("Like Hold, but represents active confrontation or high-tension face-offs.")]
    [Color("ff5500")]
    public class ConfrontPhaseNode : ScenePhaseNode
    {
        [UnityEngine.Header("Confrontation Tension")]
        [Tooltip("Multiplies global scene intensity for confrontation effect.")]
        public float tensionMultiplier = 1.2f;

        public override void OnEnter(SceneRuntimeContext ctx)
        {
            base.OnEnter(ctx);
            // Temporarily amplify active scene intensity for execution logic
            ctx.intensity = Mathf.Clamp01(ctx.intensity * tensionMultiplier);
            Debug.Log($"[DOOMS][SceneDirector] Confrontation initiated! Scene tension raised to {ctx.intensity:F2}");
        }

        public override bool ShouldAdvance(SceneRuntimeContext ctx)
        {
            return ctx.elapsedInPhase >= maxDurationSec;
        }
    }
}
