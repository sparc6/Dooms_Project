using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms
{
    public enum Relation
    {
        Neutral,
        Ally,
        Hostile,
        Subordinate,
        Superior
    }

    [Serializable]
    public class RelationCell
    {
        public string toFaction;
        public Relation relation = Relation.Neutral;
    }

    [Serializable]
    public class RelationRow
    {
        public string fromFaction;
        public List<RelationCell> cells = new List<RelationCell>();
    }

    /// <summary>
    /// 2D matrix of pairwise faction relations. Hand-authored in the inspector
    /// via the custom FactionRelationsEditor grid. Queried at runtime by
    /// RelationGatedTransitionNode and by any narrative logic that wants to
    /// know how two factions feel about each other.
    /// </summary>
    [CreateAssetMenu(fileName = "FactionRelations", menuName = "DOOMS/Faction Relations")]
    public class FactionRelationsSO : ScriptableObject
    {
        private static FactionRelationsSO _instance;
        public static FactionRelationsSO Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<FactionRelationsSO>("Dooms/FactionRelations");
                    if (_instance == null)
                    {
                        var assets = Resources.FindObjectsOfTypeAll<FactionRelationsSO>();
                        if (assets.Length > 0) _instance = assets[0];
                    }
                }
                return _instance;
            }
        }

        [Tooltip("Row per source faction; each row contains a cell per target faction.")]
        public List<RelationRow> rows = new List<RelationRow>();

        public Relation GetRelation(string fromFaction, string toFaction)
        {
            if (string.IsNullOrEmpty(fromFaction) || string.IsNullOrEmpty(toFaction))
                return Relation.Neutral;

            foreach (var row in rows)
            {
                if (row == null || !string.Equals(row.fromFaction, fromFaction, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var cell in row.cells)
                {
                    if (cell == null) continue;
                    if (string.Equals(cell.toFaction, toFaction, StringComparison.OrdinalIgnoreCase))
                        return cell.relation;
                }
            }
            return Relation.Neutral;
        }
    }
}
