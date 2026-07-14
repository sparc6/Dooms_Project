using NodeCanvas.Framework;
using ParadoxNotion;
using ParadoxNotion.Design;


namespace NodeCanvas.Tasks.Actions
{

    [Name("Set Integer")]
    [Category("✫ Blackboard")]
    [Description("Set a blackboard integer variable")]
    public class SetInt : ActionTask
    {

        [BlackboardOnly]
        public BBParameter<int> valueA;
        public AssignOp Operation = AssignOp.Set;
        public BBParameter<int> valueB;

        protected override string info => valueA + OperationTools.GetOperationString(Operation) + valueB;

        protected override void OnExecute() {
            valueA.value = OperationTools.Operate(valueA.value, valueB.value, Operation);
            EndAction();
        }
    }
}