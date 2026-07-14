using System.Collections.Generic;
using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace MLA_SIM.Dooms.Scenes.Nodes
{
    [Name("Assemble")]
    [Category("DOOMS")]
    [Description("Wait until a target percentage of agents are within tolerance of their interaction points.")]
    [Color("00aaff")]
    public class AssemblePhaseNode : ScenePhaseNode
    {
        [UnityEngine.Header("Assemble Options")]
        [Range(0f, 1f)]
        public float requiredPercentage = 0.8f;

        public override bool ShouldAdvance(SceneRuntimeContext ctx)
        {
            // Always advance if we hit the hard max timeout
            if (ctx.elapsedInPhase >= maxDurationSec)
            {
                Debug.Log($"[DOOMS][SceneDirector] Assemble phase '{phaseId}' timed out. Advancing anyway.");
                return true;
            }

            // Always enforce a minimum duration if specified
            if (ctx.elapsedInPhase < minDurationSec)
                return false;

            int totalAssigned = 0;
            int arrived = 0;

            foreach (var kv in ctx.roleAssignments)
            {
                var roleId = kv.Key;
                var agentIds = kv.Value;

                // Find the tolerance for this role in our configuration
                float tolerance = 2.0f;
                var rSlot = roles.Find(r => r.roleId == roleId);
                if (rSlot != null)
                {
                    tolerance = rSlot.arrivalTolerance;
                }

                foreach (var agentId in agentIds)
                {
                    if (string.IsNullOrEmpty(agentId)) continue;

                    totalAssigned++;

                    // Find actual Unity GameObject for this agent
                    var tag = FindAgentTag(agentId);
                    if (tag != null && ctx.reservedPoints.TryGetValue(agentId, out var ip) && ip != null)
                    {
                        float dist = Vector3.Distance(tag.transform.position, ip.GetAnchor().position);
                        if (dist <= tolerance)
                        {
                            arrived++;
                        }
                    }
                }
            }

            if (totalAssigned == 0) return true; // Empty scene or no roles, advance immediately

            float percent = (float)arrived / totalAssigned;
            return percent >= requiredPercentage;
        }

        private DoomsAgentTag FindAgentTag(string agentId)
        {
            var tags = Object.FindObjectsByType<DoomsAgentTag>(FindObjectsSortMode.None);
            foreach (var t in tags)
            {
                if (t != null && t.agentId == agentId)
                    return t;
            }
            return null;
        }
    }
}
