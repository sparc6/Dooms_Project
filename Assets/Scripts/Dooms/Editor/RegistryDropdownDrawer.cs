#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using MLA_SIM.EditorTools;

namespace MLA_SIM.Dooms.Editor
{
    [CustomPropertyDrawer(typeof(MLA_SIM.Dooms.RegistryDropdownAttribute))]
    public class RegistryDropdownDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property != null && property.isArray && property.arrayElementType == "string")
            {
                int lines = Mathf.Max(2, property.arraySize + 2);
                return EditorGUIUtility.singleLineHeight * lines + EditorGUIUtility.standardVerticalSpacing * (lines - 1);
            }

            return base.GetPropertyHeight(property, label);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var attrib = (MLA_SIM.Dooms.RegistryDropdownAttribute)attribute;

            if (property != null && property.isArray && property.arrayElementType == "string")
            {
                DrawStringArray(position, property, label, attrib.registryType);
                return;
            }

            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "Use RegistryDropdown only on strings.");
                return;
            }

            List<string> options = SogRegistryProvider.GetOptions((MLA_SIM.RegistryType)attrib.registryType).ToList();

            if (options.Count == 0)
            {
                // Fallback to text box if registry not found or empty
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            // Index 0 is always the explicit "empty / unset" sentinel.
            // This prevents the drawer from silently writing options[0] to
            // fields that are intentionally empty on every repaint.
            options.Insert(0, "(none)");

            string currentValue = property.stringValue;
            int currentIndex;
            if (string.IsNullOrEmpty(currentValue))
            {
                currentIndex = 0; // (none)
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
                bool pickedSentinel = newIndex == options.Count - 1
                    && options[newIndex].EndsWith("(unregistered)");
                if (!pickedSentinel)
                    property.stringValue = newIndex == 0 ? "" : options[newIndex];
            }
        }

        private static void DrawStringArray(Rect position, SerializedProperty property, GUIContent label, MLA_SIM.RegistryType registryType)
        {
            var options = SogRegistryProvider.GetOptions(registryType).ToList();
            if (options.Count == 0)
            {
                DrawPlainStringArray(position, property, label);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            var line = new Rect(position.x, position.y, position.width, lineHeight);

            EditorGUI.LabelField(line, label);
            line.y += lineHeight + spacing;

            int removeIndex = -1;
            for (int i = 0; i < property.arraySize; i++)
            {
                var element = property.GetArrayElementAtIndex(i);
                var row = new Rect(line.x, line.y, line.width, lineHeight);
                var popupRect = new Rect(row.x, row.y, row.width - 26f, row.height);
                var buttonRect = new Rect(row.xMax - 24f, row.y, 24f, row.height);

                int currentIndex = Mathf.Max(0, options.IndexOf(element.stringValue));
                int newIndex = EditorGUI.Popup(popupRect, currentIndex, options.ToArray());
                if (newIndex >= 0 && newIndex < options.Count)
                    element.stringValue = options[newIndex];

                if (GUI.Button(buttonRect, "-"))
                    removeIndex = i;

                line.y += lineHeight + spacing;
            }

            var addRect = new Rect(line.x, line.y, 24f, lineHeight);
            if (GUI.Button(addRect, "+"))
            {
                property.arraySize++;
                property.GetArrayElementAtIndex(property.arraySize - 1).stringValue = options[0];
            }

            if (removeIndex >= 0 && removeIndex < property.arraySize)
                property.DeleteArrayElementAtIndex(removeIndex);

            EditorGUI.EndProperty();
        }

        private static void DrawPlainStringArray(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            var line = new Rect(position.x, position.y, position.width, lineHeight);

            EditorGUI.LabelField(line, label);
            line.y += lineHeight + spacing;

            int removeIndex = -1;
            for (int i = 0; i < property.arraySize; i++)
            {
                var element = property.GetArrayElementAtIndex(i);
                var row = new Rect(line.x, line.y, line.width, lineHeight);
                var fieldRect = new Rect(row.x, row.y, row.width - 26f, row.height);
                var buttonRect = new Rect(row.xMax - 24f, row.y, 24f, row.height);

                element.stringValue = EditorGUI.TextField(fieldRect, element.stringValue);
                if (GUI.Button(buttonRect, "-"))
                    removeIndex = i;

                line.y += lineHeight + spacing;
            }

            if (GUI.Button(new Rect(line.x, line.y, 24f, lineHeight), "+"))
                property.arraySize++;

            if (removeIndex >= 0 && removeIndex < property.arraySize)
                property.DeleteArrayElementAtIndex(removeIndex);

            EditorGUI.EndProperty();
        }
    }
}
#endif
