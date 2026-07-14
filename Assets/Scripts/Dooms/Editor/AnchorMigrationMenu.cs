#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using MLA_SIM.Interactions;
using UnityEditor.SceneManagement;

namespace MLA_SIM.Dooms.Editor
{
    public static class AnchorMigrationMenu
    {
        [MenuItem("DOOMS/Migrate Anchors -> InteractionPoints")]
        public static void Migrate()
        {
            var oldAnchors = Object.FindObjectsByType<TargetTransformAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (oldAnchors.Length == 0)
            {
                EditorUtility.DisplayDialog("Migration Complete", "No TargetTransformAnchor components found in the active scene.", "OK");
                return;
            }

            int migratedCount = 0;
            foreach (var old in oldAnchors)
            {
                if (old == null) continue;

                var go = old.gameObject;
                
                // If it already has an InteractionPoint, skip or update it
                var ip = go.GetComponent<InteractionPoint>();
                if (ip == null)
                {
                    ip = Undo.AddComponent<InteractionPoint>(go);
                }

                // Copy properties
                Undo.RecordObject(ip, "Migrate TargetTransformAnchor to InteractionPoint");
                ip.anchor = old.anchor;
                ip.pointTag = old.targetClass;
                ip.animatorStateName = old.animatorStateName;
                ip.holdSeconds = old.holdSeconds;
                ip.capacity = old.capacity;
                ip.infectious = old.infectious;
                
                ip.allowedFactions = new List<string>();
                if (old.allowedFactions != null)
                {
                    ip.allowedFactions.AddRange(old.allowedFactions);
                }

                // Disable old component
                Undo.RecordObject(old, "Disable legacy TargetTransformAnchor");
                old.enabled = false;

                migratedCount++;
            }

            // Mark scenes as dirty
            EditorSceneManager.MarkAllScenesDirty();

            Debug.Log($"[DOOMS] Successfully migrated {migratedCount} TargetTransformAnchor components to InteractionPoints.");
            EditorUtility.DisplayDialog("Migration Complete", $"Successfully migrated {migratedCount} anchors to InteractionPoints. The legacy components have been disabled.", "OK");
        }
    }
}
#endif
