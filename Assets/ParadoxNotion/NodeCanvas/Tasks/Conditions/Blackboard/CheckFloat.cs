using NodeCanvas.Framework;
using ParadoxNotion;
using ParadoxNotion.Design;


namespace NodeCanvas.Tasks.Conditions
{

    [Category("✫ Blackboard")]
    public class CheckFloat : ConditionTask
    {

        [BlackboardOnly]
        public BBParameter<float> valueA;
        public CompareOp checkType = CompareOp.EqualTo;
        public BBParameter<float> valueB;

        [SliderField(0, 0.1f)]
        public float differenceThreshold = 0.05f;

        protected override string info => valueA + OperationTools.GetCompareString(checkType) + valueB;

        protected override bool OnCheck() {
            return OperationTools.Compare((float)valueA.value, (float)valueB.value, checkType, differenceThreshold);
        }
    }
}