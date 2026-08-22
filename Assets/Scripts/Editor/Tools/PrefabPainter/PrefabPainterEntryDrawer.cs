using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CORE.Editor.Tools
{
    [CustomPropertyDrawer(typeof(PrefabPainterEntry))]
    internal sealed class PrefabPainterEntryDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            SerializedProperty prefabProperty = property.FindPropertyRelative("_prefab");
            SerializedProperty wallOnlyProperty = property.FindPropertyRelative("_wallOnly");
            SerializedProperty attachmentSideProperty = property.FindPropertyRelative("_attachmentSide");
            SerializedProperty localOffsetProperty = property.FindPropertyRelative("_localOffset");
            SerializedProperty startRotationProperty = property.FindPropertyRelative("_startRotationEuler");
            SerializedProperty sectionProperty = property.FindPropertyRelative("_section");

            VisualElement container = new VisualElement();
            container.style.marginTop = 3f;
            container.style.marginBottom = 3f;
            container.style.paddingLeft = 7f;
            container.style.paddingRight = 7f;
            container.style.paddingTop = 5f;
            container.style.paddingBottom = 5f;
            container.style.backgroundColor = new Color(0.12f, 0.135f, 0.16f, 1f);
            container.style.borderLeftWidth = 2f;
            container.style.borderLeftColor = new Color(0.16f, 0.67f, 0.78f, 1f);
            container.style.borderTopLeftRadius = 4f;
            container.style.borderTopRightRadius = 4f;
            container.style.borderBottomLeftRadius = 4f;
            container.style.borderBottomRightRadius = 4f;

            Foldout foldout = new Foldout
            {
                text = ResolveTitle(prefabProperty),
                value = true
            };
            foldout.style.unityFontStyleAndWeight = FontStyle.Bold;
            container.Add(foldout);

            PropertyField prefabField = new PropertyField(prefabProperty, "Prefab");
            VisualElement sectionField = CreateSectionField(property, sectionProperty);
            PropertyField wallOnlyField = new PropertyField(wallOnlyProperty, "Только для стен");
            PropertyField attachmentSideField = new PropertyField(attachmentSideProperty, "Сторона к стене");
            PropertyField localOffsetField = new PropertyField(localOffsetProperty, "Локальное смещение");
            PropertyField startRotationField = new PropertyField(startRotationProperty, "Стартовый поворот");

            prefabField.RegisterValueChangeCallback(_ => foldout.text = ResolveTitle(prefabProperty));
            foldout.Add(prefabField);
            foldout.Add(sectionField);
            foldout.Add(wallOnlyField);
            foldout.Add(attachmentSideField);
            foldout.Add(localOffsetField);
            foldout.Add(startRotationField);
            return container;
        }

        private static VisualElement CreateSectionField(
            SerializedProperty entryProperty,
            SerializedProperty sectionProperty)
        {
            PrefabPainterConfig config = entryProperty.serializedObject.targetObject as PrefabPainterConfig;
            if (config == null || config.Sections.Count == 0)
            {
                return new PropertyField(sectionProperty, "Раздел");
            }

            List<string> choices = new List<string>(config.Sections);
            int selectedIndex = choices.IndexOf(sectionProperty.stringValue);
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }

            PopupField<string> popup = new PopupField<string>("Раздел", choices, selectedIndex);
            popup.tooltip = "Раздел библиотеки, в котором отображается этот prefab.";
            popup.RegisterValueChangedCallback(evt =>
            {
                sectionProperty.serializedObject.Update();
                sectionProperty.stringValue = evt.newValue;
                sectionProperty.serializedObject.ApplyModifiedProperties();
            });
            return popup;
        }

        private static string ResolveTitle(SerializedProperty prefabProperty)
        {
            GameObject prefab = prefabProperty.objectReferenceValue as GameObject;
            return prefab != null ? prefab.name : "Новая запись";
        }
    }
}
