#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MLA_SIM.ModularInteractions;

namespace MLA_SIM.EditorTools
{
    /// <summary>
    /// A6.5 — Editor utility that reads all SceneObjectNode.objectId values from
    /// every SceneObjectGraph asset in the project and writes them into
    /// InteractableCatalog.registeredObjectIds.
    ///
    /// Run via: MLA SIM / Setup / Sync Catalog from Scene Object Graph
    ///
    /// This gives AI authoring tools a validated object list to reference,
    /// eliminating hallucinated object names in dooms_interactables.json and
    /// LLM-authored scene briefs. Also populates RegistryType.ObjectId dropdowns.
    /// </summary>
    public static class SceneObjectGraphCatalogSync
    {
        [MenuItem("MLA SIM/Setup/Sync Catalog from Scene Object Graph")]
        private static void SyncCatalog()
        {
            // Find all SceneObjectGraph assets.
            var graphGuids = AssetDatabase.FindAssets("t:SceneObjectGraph");
            if (graphGuids == null || graphGuids.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Sync Catalog",
                    "No SceneObjectGraph assets found in the project.",
                    "OK");
                return;
            }

            var objectIds = new List<string>();
            foreach (var guid in graphGuids)
            {
                var path  = AssetDatabase.GUIDToAssetPath(guid);
                var graph = AssetDatabase.LoadAssetAtPath<SceneObjectGraph>(path);
                if (graph == null) continue;

                foreach (var node in graph.allNodes)
                {
                    if (node is SceneObjectNode objNode &&
                        !string.IsNullOrWhiteSpace(objNode.objectId) &&
                        !objectIds.Contains(objNode.objectId))
                    {
                        objectIds.Add(objNode.objectId);
                    }
                }
            }

            // Write into the catalog.
            var catalogGuids = AssetDatabase.FindAssets("t:InteractableCatalog");
            if (catalogGuids == null || catalogGuids.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Sync Catalog",
                    "No InteractableCatalog asset found. Create one first.",
                    "OK");
                return;
            }

            var catalogPath    = AssetDatabase.GUIDToAssetPath(catalogGuids[0]);
            var catalog        = AssetDatabase.LoadAssetAtPath<InteractableCatalog>(catalogPath);
            if (catalog == null) return;

            Undo.RecordObject(catalog, "Sync Catalog from SceneObjectGraph");
            catalog.registeredObjectIds = objectIds;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog(
                "Sync Catalog",
                $"Synced {objectIds.Count} object id(s) into {catalog.name}:\n\n" +
                string.Join("\n", objectIds),
                "OK");

            Debug.Log($"[SceneObjectGraphCatalogSync] Wrote {objectIds.Count} ids into {catalogPath}.");
        }
    }
}
#endif
