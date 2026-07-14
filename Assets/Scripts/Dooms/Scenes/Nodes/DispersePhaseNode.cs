using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace MLA_SIM.Dooms.Scenes.Nodes
{
    [Name("Disperse")]
    [Category("DOOMS")]
    [Description("Release all agent reservations and clear active scene role directives, allowing agents to disperse.")]
    [Color("ff0055")]
    public class DispersePhaseNode : ScenePhaseNode
    {
        public override int maxOutConnections => 0; // Terminating node

        public override void OnEnter(SceneRuntimeContext ctx)
        {
            base.OnEnter(ctx);
            Debug.Log($"[DOOMS][SceneDirector] Dispersal sequence triggered. Releasing all roles.");
            
            // Clear reserved status and points early so agents can navigate away immediately
            ctx.roleAssignments.Clear();

            // Notify each agent tag that they are no longer reserved by this scene
            var tags = Object.FindObjectsByType<DoomsAgentTag>(FindObjectsSortMode.None);
            foreach (var t in tags)
            {
                if (t != null && t.reservedBySceneId == ctx.scene.sceneId)
                {
                    t.reservedBySceneId = "";
                }
            }

            // Release all reserved InteractionPoints
            foreach (var ip in ctx.reservedPoints.Values)
            {
                if (ip != null)
                {
                    // Clean up occupancy for any agent who had this point reserved
                    foreach (var agentId in ctx.reservedPoints.Keys)
                    {
                        ip.Release(agentId);
                    }
                }
            }
            ctx.reservedPoints.Clear();
        }

        public override bool ShouldAdvance(SceneRuntimeContext ctx)
        {
            // Give agents a few seconds of free movement before terminating the scene completely
            return ctx.elapsedInPhase >= minDurationSec;
        }
    }
}
