#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MLA_SIM.Dooms.Registries;

namespace MLA_SIM.Dooms.Editor
{
    [CustomEditor(typeof(InteractionPoint))]
    public class InteractionPointEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Draw all standard fields except allowedFactions
            DrawPropertiesExcluding(serializedObject, "allowedFactions");

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Allowed Factions", EditorStyles.boldLabel);

            var factionReg = FactionRegistrySO.Instance;
            var factionIds = factionReg != null && factionReg.factions != null
                ? factionReg.factions.Select(f => f.factionId).ToArray()
                : null;

            var prop = serializedObject.FindProperty("allowedFactions");

            if (factionIds == null || factionIds.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "FactionRegistry not found. Add entries manually as text.",
                    MessageType.Warning);
                EditorGUILayout.PropertyField(prop, new GUIContent("Allowed Factions"), true);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Empty = any faction allowed. Add entries to restrict.",
                    MessageType.None);

                for (int i = 0; i < prop.arraySize; i++)
                {
                    var elem = prop.GetArrayElementAtIndex(i);
                    string current = elem.stringValue;
                    int idx = System.Array.IndexOf(factionIds, current);
                    if (idx < 0) idx = 0;

                    EditorGUILayout.BeginHorizontal();

                    // Color swatch from registry
                    if (factionReg != null && idx < factionReg.factions.Count)
                    {
                        var col = factionReg.factions[idx].displayColor;
                        var swatchRect = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14));
                        EditorGUI.DrawRect(swatchRect, col);
                    }

                    int newIdx = EditorGUILayout.Popup($"[{i}]", idx, factionIds);
                    elem.stringValue = factionIds[newIdx];

                    if (GUILayout.Button("−", GUILayout.Width(24)))
                    {
                        prop.DeleteArrayElementAtIndex(i);
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+ Add Faction", GUILayout.Width(120)))
                {
                    prop.InsertArrayElementAtIndex(prop.arraySize);
                    var newElem = prop.GetArrayElementAtIndex(prop.arraySize - 1);
                    newElem.stringValue = factionIds[0];
                }
                EditorGUILayout.EndHorizontal();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
