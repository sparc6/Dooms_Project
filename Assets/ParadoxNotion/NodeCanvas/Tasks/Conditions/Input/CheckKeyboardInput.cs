using NodeCanvas.Framework;
using ParadoxNotion;
using ParadoxNotion.Design;
using UnityEngine;


namespace NodeCanvas.Tasks.Conditions
{

    [Category("Input (Legacy System)")]
    public class CheckKeyboardInput : ConditionTask
    {

        public PressType pressType = PressType.Down;
        public KeyCode key = KeyCode.Space;

        protected override string info => pressType.ToString() + " " + key.ToString();

        protected override bool OnCheck() {

            if ( pressType == PressType.Down )
                return Input.GetKeyDown(key);

            if ( pressType == PressType.Up )
                return Input.GetKeyUp(key);

            if ( pressType == PressType.Pressed )
                return Input.GetKey(key);

            return false;
        }


        ///----------------------------------------------------------------------------------------------
        ///---------------------------------------UNITY EDITOR-------------------------------------------
#if UNITY_EDITOR

        protected override void OnTaskInspectorGUI() {

            UnityEditor.EditorGUILayout.BeginHorizontal();
            pressType = (PressType)UnityEditor.EditorGUILayout.EnumPopup(pressType);
            key = (KeyCode)UnityEditor.EditorGUILayout.EnumPopup(key);
            UnityEditor.EditorGUILayout.EndHorizontal();
        }

#endif
    }
}