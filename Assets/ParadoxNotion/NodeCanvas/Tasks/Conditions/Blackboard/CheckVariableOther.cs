using NodeCanvas.Framework;
using ParadoxNotion;
using ParadoxNotion.Design;

namespace NodeCanvas.Tasks.Conditions
{

    [Category("✫ Blackboard")]
    [Description("Check the variable value of another Blackboard by name")]
    public class CheckVariableOther<T> : ConditionTask<IBlackboard>
    {

        [RequiredField]
        public string variableName;
        public BBParameter<T> checkValue;

        protected override string info => variableName + " == " + checkValue;

        protected override bool OnCheck() {
            var variable = agent.GetVariable<T>(variableName);
            if ( variable == null ) {
                return Error(string.Format("Variable with name '{0}' does not exist on blackboard {1}", variableName, agent));
            }
            return ObjectUtils.AnyEquals(variable.value, checkValue.value);
        }
    }
}