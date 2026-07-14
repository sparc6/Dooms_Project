#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MLA_SIM.Dooms.Registries;

namespace MLA_SIM.Dooms.EditorTools
{
    /// <summary>
    /// Custom grid editor for FactionRelationsSO. Renders a square matrix with
    /// faction IDs (from FactionRegistrySO) on both axes and an enum popup at
    /// each intersection. Editing a cell writes into the underlying list-of-rows
    /// representation that is Unity-serializable.
    /// </summary>
    [CustomEditor(typeof(FactionRelationsSO))]
    public class FactionRelationsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var so = (FactionRelationsSO)target;
            var registry = FactionRegistrySO.Instance;

            if (registry == null || registry.factions == null || registry.factions.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "FactionRegistrySO is missing or empty. Create one at Assets/Resources/Dooms/FactionRegistry first.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Faction Relations Matrix", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Each row = source faction. Each column = target faction. Pick the relation source → target.",
                MessageType.None);

            var factionIds = new List<string>();
            foreach (var f in registry.factions)
            {
                if (f != null && !string.IsNullOrWhiteSpace(f.factionId)) factionIds.Add(f.factionId);
            }

            EnsureMatrixShape(so, factionIds);

            EditorGUI.BeginChangeCheck();

            // Header row
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(90));
            foreach (var col in factionIds)
            {
                GUILayout.Label(col, EditorStyles.miniBoldLabel, GUILayout.Width(90));
            }
            EditorGUILayout.EndHorizontal();

            // Body
            foreach (var rowFaction in factionIds)
            {
                var row = FindOrCreateRow(so, rowFaction);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(rowFaction, EditorStyles.miniBoldLabel, GUILayout.Width(90));
                foreach (var colFaction in factionIds)
                {
                    var cell = FindOrCreateCell(row, colFaction);
                    cell.relation = (Relation)EditorGUILayout.EnumPopup(cell.relation, GUILayout.Width(90));
                }
                EditorGUILayout.EndHorizontal();
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(so);
            }
        }

        private static void EnsureMatrixShape(FactionRelationsSO so, List<string> factionIds)
        {
            if (so.rows == null) so.rows = new List<RelationRow>();

            foreach (var f in factionIds)
            {
                var row = FindOrCreateRow(so, f);
                foreach (var t in factionIds) FindOrCreateCell(row, t);
            }
        }

        private static RelationRow FindOrCreateRow(FactionRelationsSO so, string factionId)
        {
            foreach (var r in so.rows)
            {
                if (r != null && r.fromFaction == factionId) return r;
            }
            var fresh = new RelationRow { fromFaction = factionId, cells = new List<RelationCell>() };
            so.rows.Add(fresh);
            return fresh;
        }

        private static RelationCell FindOrCreateCell(RelationRow row, string factionId)
        {
            foreach (var c in row.cells)
            {
                if (c != null && c.toFaction == factionId) return c;
            }
            var fresh = new RelationCell { toFaction = factionId, relation = Relation.Neutral };
            row.cells.Add(fresh);
            return fresh;
        }
    }
}
#endif
