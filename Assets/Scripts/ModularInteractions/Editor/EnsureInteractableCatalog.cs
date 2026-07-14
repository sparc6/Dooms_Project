#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace MLA_SIM.EditorTools
{
    /// <summary>
    /// Core bootstrap — ensures an InteractableCatalog asset exists at
    /// Assets/Resources/MLA_SIM/InteractableCatalog.asset on editor load.
    /// This script lives outside the DOOMS add-on folder so catalog creation
    /// survives DOOMS removal.
    /// </summary>
    [InitializeOnLoad]
    public static class EnsureInteractableCatalog
    {
        private const string ResourcesPath = "Assets/Resources/MLA_SIM";
        private const string CatalogPath   = ResourcesPath + "/InteractableCatalog.asset";

        static EnsureInteractableCatalog()
        {
            EditorApplication.delayCall += CreateCatalogIfMissing;
        }

        [MenuItem("MLA SIM/Setup/Ensure Interactable Catalog")]
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
                System.IO.Directory.CreateDirectory(
                    System.IO.Path.Combine(Application.dataPath,
                        ResourcesPath.Replace("Assets/", "")));
                AssetDatabase.Refresh();
            }
            var catalog = ScriptableObject.CreateInstance<InteractableCatalog>();
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
#endif
