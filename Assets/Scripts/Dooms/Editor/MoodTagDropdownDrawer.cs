#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MLA_SIM.Dooms.Editor
{
    [CustomPropertyDrawer(typeof(MLA_SIM.Dooms.MoodTagDropdownAttribute))]
    public class MoodTagDropdownDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "Use MoodTagDropdown only on strings.");
                return;
            }

            var profile = AmbientMoodProfileSO.Instance;
            if (profile == null)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var options = new List<string>();
            AddTags(profile.hostileTags, options);
            AddTags(profile.tenseTags, options);

            options = options
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (options.Count == 0)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            options.Insert(0, "(none)");

            string currentValue = property.stringValue;
            int currentIndex;
            if (string.IsNullOrEmpty(currentValue))
            {
                currentIndex = 0;
            }
            else
            {
                currentIndex = options.IndexOf(currentValue);
                if (currentIndex < 0)
                {
                    options.Add(currentValue + "  (unregistered)");
                    currentIndex = options.Count - 1;
                }
            }

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUI.Popup(position, label.text, currentIndex, options.ToArray());
            if (EditorGUI.EndChangeCheck() && newIndex >= 0 && newIndex < options.Count)
            {
                bool pickedSentinel = newIndex == options.Count - 1 && options[newIndex].EndsWith("(unregistered)");
                if (!pickedSentinel)
                    property.stringValue = newIndex == 0 ? "" : options[newIndex];
            }
        }

        private static void AddTags(List<string> source, List<string> sink)
        {
            if (source == null || sink == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                var v = source[i];
                if (string.IsNullOrWhiteSpace(v)) continue;
                sink.Add(v.Trim());
            }
        }
    }
}
#endif
