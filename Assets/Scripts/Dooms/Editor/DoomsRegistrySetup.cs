#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using MLA_SIM.Dooms.Registries;

namespace MLA_SIM.Dooms.EditorTools
{
    // Run once after adding AnimationStateRegistrySO to create the registry assets.
    public static class DoomsRegistrySetup
    {
        private const string REG_RES  = "Assets/Resources/Dooms/DoomsAnimationStateRegistry.asset";
        private const string PROP_RES = "Assets/Resources/Dooms/PropRegistry.asset";

        [MenuItem("DOOMS/Setup Animation State Registry", priority = 110)]
        public static void SetupAnimationStateRegistry()
        {
            int created = 0;

            if (AssetDatabase.LoadAssetAtPath<AnimationStateRegistrySO>(REG_RES) == null)
            {
                EnsureFolder(System.IO.Path.GetDirectoryName(REG_RES).Replace('\\', '/'));
                var asset = ScriptableObject.CreateInstance<AnimationStateRegistrySO>();
                AssetDatabase.CreateAsset(asset, REG_RES);
                EditorUtility.SetDirty(asset);
                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("DOOMS Registry Setup",
                created > 0
                    ? $"Created {created} AnimationStateRegistry asset(s)."
                    : "AnimationStateRegistry assets already exist — nothing to do.",
                "OK");
        }

        [MenuItem("DOOMS/Setup Prop Registry", priority = 111)]
        public static void SetupPropRegistry()
        {
            int created = 0;

            if (AssetDatabase.LoadAssetAtPath<PropRegistrySO>(PROP_RES) == null)
            {
                EnsureFolder(System.IO.Path.GetDirectoryName(PROP_RES).Replace('\\', '/'));
                var asset = ScriptableObject.CreateInstance<PropRegistrySO>();
                AssetDatabase.CreateAsset(asset, PROP_RES);
                EditorUtility.SetDirty(asset);
                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("DOOMS Registry Setup",
                created > 0
                    ? $"Created {created} PropRegistry asset(s)."
                    : "PropRegistry asset already exists — nothing to do.",
                "OK");
        }

        static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;
            string parent = folderPath.Contains("/")
                ? folderPath.Substring(0, folderPath.LastIndexOf('/'))
                : "Assets";
            string name = folderPath.Contains("/")
                ? folderPath.Substring(folderPath.LastIndexOf('/') + 1)
                : folderPath;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
