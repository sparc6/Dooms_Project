using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace MLA_SIM.Dooms.Editor
{
    public static class EnsureInteractableCatalogDooms
    {
        private const string ResourcesPath = "Assets/Resources/Dooms";
        private const string CatalogPath   = ResourcesPath + "/InteractableCatalog.asset";

        [MenuItem("DOOMS/Setup/Ensure Interactable Catalog (Legacy Path)")]
        public static void CreateCatalogIfMissing()
        {
            var guids = AssetDatabase.FindAssets("t:InteractableCatalog");
            if (guids != null && guids.Length > 0)
            {
                Debug.Log("[EnsureInteractableCatalog] Already exists — skipping.");
                return;
            }
            if (!AssetDatabase.IsValidFolder(ResourcesPath))
            {
                System.IO.Directory.CreateDirectory(ResourcesPath);
                AssetDatabase.Refresh();
            }
            var catalog = ScriptableObject.CreateInstance<MLA_SIM.InteractableCatalog>();
            catalog.actionVocabulary = new List<string>
            {
                "InteractWith", "Pickup", "Fix", "Destroy",
                "SleepAt", "HideAt", "TalkTo", "Use", "Talk", "Notify"
            };
            catalog.contextTags = new List<string>
            {
                "container", "machine", "power", "furniture",
                "weapon", "food", "tool", "infrastructure"
            };
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[EnsureInteractableCatalog] Created at {CatalogPath}");
        }
    }
}
