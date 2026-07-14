using UnityEngine;
using NodeCanvas.Framework;
using ParadoxNotion.Design;


namespace NodeCanvas.Tasks.Conditions
{

    [Category("✫ Utility")]
    [Description("Simply use to debug return true or false by inverting the condition if needed")]
    public class DebugCondition : ConditionTask
    {
        protected override bool OnCheck() {
            return false;
        }

        protected override void OnEnable() {
            Debug.Log("enable");
        }

        protected override void OnDisable() {
            Debug.Log("disable");
        }


        ///----------------------------------------------------------------------------------------------
        ///---------------------------------------UNITY EDITOR-------------------------------------------
#if UNITY_EDITOR

        protected override void OnTaskInspectorGUI() {
            if ( UnityEngine.Application.isPlaying && UnityEngine.GUILayout.Button("Tick True") ) {
                YieldReturn(true);
            }
        }

#endif

    }
}