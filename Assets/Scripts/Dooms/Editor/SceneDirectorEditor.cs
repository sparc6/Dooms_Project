#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using MLA_SIM.Dooms.Registries;

namespace MLA_SIM.Dooms.Scenes.Editor
{
    [CustomEditor(typeof(SceneDirector))]
    public class SceneDirectorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var director = (SceneDirector)target;

            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("DOOMS Scene Controller (Runtime)", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Play Mode required to manual-trigger scenes.", MessageType.Info);
                return;
            }

            var ctx = director.CurrentContext;
            if (ctx != null && ctx.scene != null)
            {
                EditorGUILayout.HelpBox($"Active Scene: {ctx.scene.displayName} ({ctx.scene.sceneId})\nIntensity: {ctx.intensity:F2}\nPhase Elapsed: {ctx.elapsedInPhase:F1}s", MessageType.Info);

                if (GUILayout.Button("Deactivate Scene", GUILayout.Height(30)))
                {
                    director.DeactivateScene("Manual click in Inspector");
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No active scene.", MessageType.None);
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Trigger Registered Scenes:", EditorStyles.boldLabel);

            var registry = SceneRegistrySO.Instance;
            if (registry == null || registry.scenes == null || registry.scenes.Count == 0)
            {
                EditorGUILayout.HelpBox("SceneRegistry is empty or missing. Go to Assets/Dooms/Registries/SceneRegistry.asset to register scenes.", MessageType.Warning);
                return;
            }

            foreach (var scene in registry.scenes)
            {
                if (scene == null || string.IsNullOrEmpty(scene.sceneId)) continue;

                if (GUILayout.Button($"Activate: {scene.displayName} ({scene.sceneId})", GUILayout.Height(25)))
                {
                    director.ActivateScene(scene.sceneId, 0.75f);
                }
            }
        }
    }
}
#endif
