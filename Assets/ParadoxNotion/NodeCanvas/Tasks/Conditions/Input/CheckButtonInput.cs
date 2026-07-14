using NodeCanvas.Framework;
using ParadoxNotion;
using ParadoxNotion.Design;
using UnityEngine;


namespace NodeCanvas.Tasks.Conditions
{

    [Category("Input (Legacy System)")]
    public class CheckButtonInput : ConditionTask
    {

        public PressType pressType = PressType.Down;
        [RequiredField] public BBParameter<string> buttonName = "Fire1";

        protected override string info => pressType.ToString() + " " + buttonName.ToString();

        protected override bool OnCheck() {

            if ( pressType == PressType.Down )
                return Input.GetButtonDown(buttonName.value);

            if ( pressType == PressType.Up )
                return Input.GetButtonUp(buttonName.value);

            if ( pressType == PressType.Pressed )
                return Input.GetButton(buttonName.value);

            return false;
        }
    }
}