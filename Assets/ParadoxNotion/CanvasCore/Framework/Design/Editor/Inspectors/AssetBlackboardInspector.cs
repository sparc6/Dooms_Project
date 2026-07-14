#if UNITY_EDITOR

using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEditor;
using UnityEngine;

namespace NodeCanvas.Editor
{
    [CustomEditor(typeof(AssetBlackboard))]
    public class AssetBlackboardInspector : UnityEditor.Editor
    {

        private AssetBlackboard bb => (AssetBlackboard)target;

        public override void OnInspectorGUI() {
            BlackboardEditor.ShowVariables(bb);
            EditorUtils.EndOfInspector();
            if ( Event.current.isMouse ) {
                Repaint();
            }
        }
    }
}

#endif