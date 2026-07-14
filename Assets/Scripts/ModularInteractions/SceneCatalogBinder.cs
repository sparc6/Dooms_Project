using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace MLA_SIM.ModularInteractions
{
    /// <summary>
    /// Scene-level helper that binds a shared catalog to all relevant Unity-side IO components.
    /// This keeps item definitions, interactables, and dependency managers aligned during setup.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SceneCatalogBinder : MonoBehaviour
    {
        [Header("Shared Catalog")]
        [Tooltip("The catalog asset to apply across scene IO and item components.")]
        public InteractableCatalog sharedCatalog;

        [Header("Binding Options")]
        [Tooltip("Include inactive objects when applying the catalog to scene components.")]
        public bool includeInactiveObjects = true;

        [Tooltip("Automatically bind the catalog when the component starts in Play Mode.")]
        public bool applyOnStart = false;

        [Tooltip("Automatically bind the catalog when values change in the editor.")]
        public bool applyOnValidate = false;

        private void Start()
        {
            if (Application.isPlaying && applyOnStart)
            {
                ApplySharedCatalogToScene();
            }
        }

        private void OnValidate()
        {
            if (!Application.isPlaying && applyOnValidate)
            {
                ApplySharedCatalogToScene();
            }
        }

        [ContextMenu("Apply Shared Catalog To Scene")]
        public void ApplySharedCatalogToScene()
        {
            if (sharedCatalog == null)
            {
                Debug.LogWarning("[SceneCatalogBinder] No shared catalog assigned.");
                return;
            }

            var inactiveMode = includeInactiveObjects ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            int updatedCount = 0;

            foreach (var interactable in FindObjectsByType<InteractableObject>(inactiveMode, FindObjectsSortMode.None))
            {
                if (interactable == null) continue;
                if (interactable.GetSharedCatalog() != sharedCatalog)
                {
                    interactable.SetSharedCatalog(sharedCatalog);
                    updatedCount++;
                    MarkDirty(interactable);
                }
            }

            foreach (var inventory in FindObjectsByType<MLA_SIM.AgentInventory>(inactiveMode, FindObjectsSortMode.None))
            {
                if (inventory == null) continue;
                if (inventory.GetSharedCatalog() != sharedCatalog)
                {
                    inventory.SetSharedCatalog(sharedCatalog);
                    updatedCount++;
                    MarkDirty(inventory);
                }
            }

            Debug.Log($"[SceneCatalogBinder] Applied catalog '{sharedCatalog.name}' to {updatedCount} scene component(s).");
        }

#if UNITY_EDITOR
        private static void MarkDirty(Object target)
        {
            EditorUtility.SetDirty(target);
            if (target is Component component && component.gameObject != null)
            {
                EditorUtility.SetDirty(component.gameObject);
                if (component.gameObject.scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
                }
            }
        }
#else
        private static void MarkDirty(Object target)
        {
        }
#endif
    }
}
