#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MLA_SIM.Dooms.Editor
{
    [CustomPropertyDrawer(typeof(MLA_SIM.Dooms.ActivityNameDropdownAttribute))]
    public class ActivityNameDropdownDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "Use ActivityNameDropdown only on strings.");
                return;
            }

            var options = new List<string>();

            // Runtime singleton only resolves assets already loaded (or under Resources).
            // In editor inspectors (e.g. AmbientMoodProfile), that can be null/empty even
            // when catalog assets exist in project. Collect from both singleton and project.
            AddNamesFromCatalog(ActivityCatalogSO.Instance, options);

            if (options.Count == 0)
            {
                var guids = AssetDatabase.FindAssets("t:ActivityCatalogSO");
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var catalog = AssetDatabase.LoadAssetAtPath<ActivityCatalogSO>(path);
                    AddNamesFromCatalog(catalog, options);
                }
            }

            options = options
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            EditorGUI.BeginProperty(position, label, property);

            Rect contentRect = EditorGUI.PrefixLabel(position, label);
            const float pickerWidth = 120f;
            float actualPickerWidth = Mathf.Min(pickerWidth, Mathf.Max(70f, contentRect.width * 0.45f));
            Rect textRect = new Rect(contentRect.x, contentRect.y, Mathf.Max(40f, contentRect.width - actualPickerWidth - 4f), contentRect.height);
            Rect popupRect = new Rect(textRect.xMax + 4f, contentRect.y, contentRect.width - textRect.width - 4f, contentRect.height);

            EditorGUI.BeginChangeCheck();
            string typed = EditorGUI.TextField(textRect, property.stringValue ?? "");
            if (EditorGUI.EndChangeCheck())
            {
                property.stringValue = typed;
            }

            if (options.Count == 0)
            {
                EditorGUI.EndProperty();
                return;
            }

            options.Insert(0, "(none)");

            string currentValue = property.stringValue;
            int currentIndex = string.IsNullOrEmpty(currentValue) ? 0 : Mathf.Max(0, options.IndexOf(currentValue));

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUI.Popup(popupRect, currentIndex, options.ToArray());
            if (EditorGUI.EndChangeCheck() && newIndex >= 0 && newIndex < options.Count)
            {
                property.stringValue = newIndex == 0 ? "" : options[newIndex];
            }

            EditorGUI.EndProperty();
        }

        private static void AddNamesFromCatalog(ActivityCatalogSO catalog, List<string> sink)
        {
            if (catalog == null || sink == null) return;

            AddNames(catalog.shared, sink);
            if (catalog.factionOverrides == null) return;

            for (int i = 0; i < catalog.factionOverrides.Count; i++)
            {
                var entry = catalog.factionOverrides[i];
                if (entry == null) continue;
                AddNames(entry.activities, sink);
            }
        }

        private static void AddNames(List<DoomsAgentT4Brain.Activity> activities, List<string> sink)
        {
            if (activities == null || sink == null) return;
            for (int i = 0; i < activities.Count; i++)
            {
                var a = activities[i];
                if (a == null || string.IsNullOrWhiteSpace(a.activityName)) continue;
                sink.Add(a.activityName.Trim());
            }
        }
    }
}
#endif
