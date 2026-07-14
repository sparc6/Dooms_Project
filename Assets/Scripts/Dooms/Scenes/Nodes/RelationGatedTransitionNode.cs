using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;

namespace MLA_SIM.Dooms.Scenes.Nodes
{
    /// <summary>
    /// Branches the scene graph based on the live relation between two
    /// factions. Output connection 0 is taken when the relation matches
    /// `requiredRelation`; output 1 otherwise. Authors are expected to set
    /// up exactly two outgoing connections in the NodeCanvas editor.
    /// </summary>
    [Name("Relation Gate")]
    [Category("DOOMS")]
    [Description("Branches based on the current pairwise faction relation. Out 0 = match, Out 1 = no match.")]
    [Color("ffcc00")]
    public class RelationGatedTransitionNode : ScenePhaseNode
    {
        [UnityEngine.Header("Relation Gate")]
        [RegistryDropdown(RegistryType.Faction)]
        public string factionA = "";

        [RegistryDropdown(RegistryType.Faction)]
        public string factionB = "";

        public Relation requiredRelation = Relation.Hostile;

        public override int maxOutConnections => 2;

        public bool EvaluateRelation()
        {
            var rel = FactionRelationsSO.Instance;
            if (rel == null)
            {
                Debug.LogWarning("[DOOMS][RelationGate] FactionRelationsSO.Instance is null; treating as Neutral.");
                return Relation.Neutral == requiredRelation;
            }
            var actual = rel.GetRelation(factionA, factionB);
            return actual == requiredRelation;
        }

        public override bool ShouldAdvance(SceneRuntimeContext ctx)
        {
            return ctx.elapsedInPhase >= Mathf.Max(0.1f, minDurationSec);
        }
    }
}
