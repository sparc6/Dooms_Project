#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using MLA_SIM.Dooms.Scenes;
using MLA_SIM.Dooms.Scenes.Nodes;

namespace MLA_SIM.Dooms.EditorTools
{
    /// <summary>
    /// Custom inspector for SceneSO that lets the author create an embedded
    /// SceneGraph asset in one click, then opens NodeCanvas's graph editor on it.
    /// </summary>
    [CustomEditor(typeof(SceneSO))]
    public class SceneSOEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var so = (SceneSO)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Scene Graph Authoring", EditorStyles.boldLabel);

            if (so.graph == null)
            {
                EditorGUILayout.HelpBox(
                    "No SceneGraph assigned. Click below to create an embedded graph asset with a default Assemble → Hold → Disperse skeleton.\n\n" +
                    "After it is created, the NodeCanvas Graph Editor will open automatically. Right-click on the canvas to add more phase nodes (DOOMS/Assemble, Hold, Confront, Disperse, RelationGatedTransition).",
                    MessageType.Info);

                if (GUILayout.Button("Create & Open Embedded Scene Graph", GUILayout.Height(32)))
                {
                    CreateEmbeddedGraph(so);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "A SceneGraph is assigned. Click below to open it in the NodeCanvas Graph Editor. " +
                    "Inside the editor, right-click to add Phase nodes and connect them in order.",
                    MessageType.None);

                if (GUILayout.Button("Open Scene Graph in Editor", GUILayout.Height(28)))
                {
                    OpenGraphInEditor(so.graph);
                }
            }
        }

        private static void CreateEmbeddedGraph(SceneSO so)
        {
            string soPath = AssetDatabase.GetAssetPath(so);
            if (string.IsNullOrEmpty(soPath))
            {
                EditorUtility.DisplayDialog("DOOMS", "Save the SceneSO asset first, then try again.", "OK");
                return;
            }

            var graph = ScriptableObject.CreateInstance<SceneGraph>();
            graph.name = so.name + "_Graph";

            // Build a default skeleton: Assemble -> Hold -> Disperse
            var assemble = graph.AddNode<AssemblePhaseNode>();
            assemble.phaseId = "Assemble";
            assemble.position = new Vector2(0, 0);

            var hold = graph.AddNode<HoldPhaseNode>();
            hold.phaseId = "Hold";
            hold.position = new Vector2(260, 0);

            var disperse = graph.AddNode<DispersePhaseNode>();
            disperse.phaseId = "Disperse";
            disperse.position = new Vector2(520, 0);

            graph.ConnectNodes(assemble, hold);
            graph.ConnectNodes(hold, disperse);
            graph.primeNode = assemble;

            // Persist as sub-asset of the SceneSO so the user doesn't manage two files.
            AssetDatabase.AddObjectToAsset(graph, so);
            so.graph = graph;
            EditorUtility.SetDirty(so);
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(soPath);

            OpenGraphInEditor(graph);
        }

        private static void OpenGraphInEditor(SceneGraph graph)
        {
            if (graph == null) return;

            // NodeCanvas opens its Graph Editor automatically when a graph asset is
            // double-clicked. Selecting + opening through AssetDatabase achieves the
            // same effect without a hard reference to NodeCanvas editor assemblies.
            Selection.activeObject = graph;
            AssetDatabase.OpenAsset(graph);
        }
    }
}
#endif
