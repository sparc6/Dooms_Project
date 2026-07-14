#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using ParadoxNotion.Design;
using UnityEditor;
using UnityEngine;
using MLA_SIM.EditorTools;

namespace MLA_SIM.Dooms.Editor
{
    // Draws registry dropdown popups inside the NodeCanvas canvas panel.
    // Discovered automatically by PropertyDrawerFactory via reflection — no registration needed.
    public class RegistryDropdownNCDrawer : AttributeDrawer<RegistryDropdownNCAttribute>
    {
        public override object OnGUI(GUIContent content, object instance)
        {
            if (fieldInfo.FieldType == typeof(string[]))
                return DrawStringArray(content, instance as string[] ?? Array.Empty<string>(), attribute.registryType);

            var options = GetOptions(attribute.registryType);

            if (options == null || options.Length == 0)
                return MoveNextDrawer(); // fall back to plain text field

            string current = instance as string ?? "";
            int idx = Array.IndexOf(options, current);
            if (idx < 0) idx = 0;

            int newIdx = EditorGUILayout.Popup(content, idx, options);
            return options[newIdx];
        }

        private static string[] GetOptions(RegistryType type)
        {
            return SogRegistryProvider.GetOptions(type);
        }

        private static string[] DrawStringArray(GUIContent content, string[] values, RegistryType type)
        {
            var options = SogRegistryProvider.GetOptions(type);
            if (options == null || options.Length == 0)
                return values;

            var edited = new List<string>(values ?? Array.Empty<string>());

            GUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(content);
            if (GUILayout.Button("+", GUILayout.Width(22)))
                edited.Add(options[0]);
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < edited.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                int index = Array.IndexOf(options, edited[i]);
                if (index < 0) index = 0;
                int newIndex = EditorGUILayout.Popup(GUIContent.none, index, options);
                if (newIndex >= 0 && newIndex < options.Length)
                    edited[i] = options[newIndex];
                if (GUILayout.Button("-", GUILayout.Width(22)))
                {
                    edited.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            return edited.ToArray();
        }
    }
}
#endif
