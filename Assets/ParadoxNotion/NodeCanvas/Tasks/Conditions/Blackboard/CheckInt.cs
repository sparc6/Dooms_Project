using NodeCanvas.Framework;
using ParadoxNotion;
using ParadoxNotion.Design;


namespace NodeCanvas.Tasks.Conditions
{

    [Category("✫ Blackboard")]
    public class CheckInt : ConditionTask
    {

        [BlackboardOnly]
        public BBParameter<int> valueA;
        public CompareOp checkType = CompareOp.EqualTo;
        public BBParameter<int> valueB;

        protected override string info => valueA + OperationTools.GetCompareString(checkType) + valueB;

        protected override bool OnCheck() {
            return OperationTools.Compare((int)valueA.value, (int)valueB.value, checkType);
        }
    }
}