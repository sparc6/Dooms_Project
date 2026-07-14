#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using MLA_SIM.Dooms.Registries;
using MLA_SIM.Dooms.Scenes;

namespace MLA_SIM.Dooms.Scenes.Editor
{
    [CustomEditor(typeof(SceneSmokeTestLauncher))]
    public class SceneSmokeTestLauncherEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var launcher = (SceneSmokeTestLauncher)target;

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Smoke Test Controls", EditorStyles.boldLabel);

            var registry = launcher.sceneRegistry != null ? launcher.sceneRegistry : SceneRegistrySO.Instance;
            var scenes = registry != null ? registry.scenes : null;

            if (scenes == null || scenes.Count == 0)
            {
                EditorGUILayout.HelpBox("No registered SceneSOs were found. Assign a SceneRegistrySO or populate the default registry first.", MessageType.Warning);
                return;
            }

            var validScenes = new System.Collections.Generic.List<SceneSO>();
            foreach (var scene in scenes)
            {
                if (scene != null && !string.IsNullOrWhiteSpace(scene.sceneId))
                {
                    validScenes.Add(scene);
                }
            }

            if (validScenes.Count == 0)
            {
                EditorGUILayout.HelpBox("The registry does not contain any valid SceneSO assets.", MessageType.Warning);
                return;
            }

            var labels = new string[validScenes.Count];
            for (int i = 0; i < validScenes.Count; i++)
            {
                var scene = validScenes[i];
                labels[i] = string.IsNullOrWhiteSpace(scene.displayName)
                    ? scene.sceneId
                    : $"{scene.displayName} ({scene.sceneId})";
            }

            int clampedIndex = Mathf.Clamp(launcher.selectedSceneIndex, 0, validScenes.Count - 1);
            int newIndex = EditorGUILayout.Popup("Scene", clampedIndex, labels);
            if (newIndex != launcher.selectedSceneIndex)
            {
                Undo.RecordObject(launcher, "Change Smoke Test Scene");
                launcher.selectedSceneIndex = newIndex;
                EditorUtility.SetDirty(launcher);
            }

            var selectedScene = validScenes[Mathf.Clamp(launcher.selectedSceneIndex, 0, validScenes.Count - 1)];
            EditorGUILayout.HelpBox($"Selected: {selectedScene.displayName} ({selectedScene.sceneId})", MessageType.Info);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Launch Selected Scene", GUILayout.Height(28)))
                {
                    launcher.LaunchSelectedScene();
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Previous", GUILayout.Height(24)))
                {
                    launcher.LaunchPreviousScene();
                }
                if (GUILayout.Button("Next", GUILayout.Height(24)))
                {
                    launcher.LaunchNextScene();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Start Auto-Cycle", GUILayout.Height(24)))
                {
                    launcher.StartAutoCycle();
                }
                if (GUILayout.Button("Stop All", GUILayout.Height(24)))
                {
                    launcher.StopAllLaunches();
                }
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("Deactivate Current Scene", GUILayout.Height(24)))
                {
                    if (launcher.sceneDirector != null)
                    {
                        launcher.sceneDirector.DeactivateScene("Smoke test launcher manual stop");
                    }
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to use the launch buttons.", MessageType.None);
            }
        }
    }
}
#endif
