using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;


namespace NodeCanvas.Tasks.Actions
{

    [Category("✫ Blackboard")]
    [Description("Set a blackboard boolean variable at random")]
    public class SetBooleanRandom : ActionTask
    {

        [BlackboardOnly]
        public BBParameter<bool> boolVariable;

        protected override string info => "Set " + boolVariable + " Random";

        protected override void OnExecute() {
            boolVariable.value = Random.Range(0, 2) != 0;
            EndAction();
        }
    }
}