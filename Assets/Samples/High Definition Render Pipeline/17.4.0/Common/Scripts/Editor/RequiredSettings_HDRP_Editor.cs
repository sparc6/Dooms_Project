using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEditor;
using System;
using UnityEditor.Inspector.GraphicsSettingsInspectors;

namespace UnityEditor.Rendering.HighDefinition
{
    [CustomPropertyDrawer(typeof(HDRPSettingsSectionAttribute))]
    public class HDRPSettingsSectionDrawer : PropertyDrawer
    {
        // Draw the property inside the given rect
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            HDRPSettingsSectionAttribute settingsSection = attribute as HDRPSettingsSectionAttribute;

            if (property.propertyType == SerializedPropertyType.Integer)
            {
                if (settingsSection.rootSection != null)
                {
                    var so = property.serializedObject;
                    var fullPath = new List<string>( property.propertyPath.Split("."[0]) );
                    fullPath[fullPath.Count - 1] = settingsSection.rootSection;
                    SerializedProperty rootSectionProp = null;

                    rootSectionProp = so.FindProperty(fullPath[0]);
                    if (rootSectionProp == null)
                        return;

                    fullPath.RemoveAt(0);

                    while (fullPath.Count > 0)
                    {
                        rootSectionProp = rootSectionProp.FindPropertyRelative(fullPath[0]);
                        if (rootSectionProp == null)
                            return;
                        fullPath.RemoveAt(0);
                    }

                    string rootSectionName = HDRenderPipelineUICompat.ExpandableGroupType.GetEnumName(rootSectionProp.intValue);
                    Type enumType;
                    switch (rootSectionName)
                    {
                        case "Lighting":
                            enumType = HDRenderPipelineUICompat.ExpandableLightingType;
                            break;
                        case "LightingTiers":
                            enumType = HDRenderPipelineUICompat.ExpandableLightingQualityType;
                            break;
                        case "PostProcess":
                            enumType = HDRenderPipelineUICompat.ExpandablePostProcessType;
                            break;
                        case "PostProcessTiers":
                            enumType = HDRenderPipelineUICompat.ExpandablePostProcessQualityType;
                            break;
                        default:
                            enumType = HDRenderPipelineUICompat.ExpandableRenderingType;
                            break;
                    }

                    Enum currentValue = (Enum)Enum.ToObject(enumType, property.intValue);
                    property.intValue = Convert.ToInt32(EditorGUI.EnumPopup(position, label, currentValue));
                }
                else
                {
                    Enum currentValue = (Enum)Enum.ToObject(HDRenderPipelineUICompat.ExpandableGroupType, property.intValue);
                    property.intValue = Convert.ToInt32(EditorGUI.EnumPopup(position, label, currentValue));
                }
            }
            else
                EditorGUI.LabelField(position, label.text, "Use HDRPSettingsSection with int.");
        }
    }

    // HDRenderPipelineUI is internal in this version of the HDRP package, so it is accessed
    // via reflection instead of a direct reference.
    internal static class HDRenderPipelineUICompat
    {
        private static readonly Type s_HDRenderPipelineUIType =
            Type.GetType("UnityEditor.Rendering.HighDefinition.HDRenderPipelineUI, Unity.RenderPipelines.HighDefinition.Editor");

        private static readonly System.Reflection.FieldInfo s_SubInspectorsField =
            s_HDRenderPipelineUIType?.GetField("SubInspectors", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        public static readonly Type ExpandableGroupType = GetNestedEnum("ExpandableGroup");
        public static readonly Type ExpandableLightingType = GetNestedEnum("ExpandableLighting");
        public static readonly Type ExpandableLightingQualityType = GetNestedEnum("ExpandableLightingQuality");
        public static readonly Type ExpandablePostProcessType = GetNestedEnum("ExpandablePostProcess");
        public static readonly Type ExpandablePostProcessQualityType = GetNestedEnum("ExpandablePostProcessQuality");
        public static readonly Type ExpandableRenderingType = GetNestedEnum("ExpandableRendering");

        private static Type GetNestedEnum(string name)
        {
            return s_HDRenderPipelineUIType?.GetNestedType(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        }

        public static void ExpandSubInspector(int uiSectionInt, int uiSubSectionInt)
        {
            if (s_SubInspectorsField == null)
                return;

            object subInspectors = s_SubInspectorsField.GetValue(null);
            object subInspector = ((System.Collections.IDictionary)subInspectors)[Enum.ToObject(ExpandableGroupType, uiSectionInt)];
            var expandMethod = subInspector?.GetType().GetMethod("Expand");
            if (expandMethod == null)
                return;

            Type expandParamType = expandMethod.GetParameters()[0].ParameterType;
            object expandArg = expandParamType.IsEnum ? Enum.ToObject(expandParamType, uiSubSectionInt) : Convert.ChangeType(uiSubSectionInt, expandParamType);
            expandMethod.Invoke(subInspector, new object[] { expandArg });
        }
    }

    public class HDRPRequiredSettings_Editor
    {
        [InitializeOnLoadMethod]
        static void Initialize()
        {
            UnityEngine.Rendering.RequiredSettingBase.showSettingCallback = ShowSetting;
        }
        
        static void ShowSetting(UnityEngine.Rendering.RequiredSettingBase settingBase)
        {
            var setting = settingBase as RequiredSettingHDRP;
            
            if (!string.IsNullOrEmpty(setting.globalSettingsType))
			{
                var type = Type.GetType(setting.globalSettingsType);
                GraphicsSettingsInspectorUtility.OpenAndScrollTo(type);
			}
            else
			{
                SettingsService.OpenProjectSettings(setting.projectSettingsPath);
                HDRenderPipelineUICompat.ExpandSubInspector(setting.uiSectionInt, setting.uiSubSectionInt);
                CoreEditorUtils.Highlight("Project Settings", setting.propertyPath, HighlightSearchMode.Identifier);
			}
        }
    }
}
