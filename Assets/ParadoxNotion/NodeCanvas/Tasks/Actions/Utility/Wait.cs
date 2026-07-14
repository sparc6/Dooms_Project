using NodeCanvas.Framework;
using ParadoxNotion;
using ParadoxNotion.Design;


namespace NodeCanvas.Tasks.Actions
{

    [Category("✫ Utility")]
    public class Wait : ActionTask
    {
        public BBParameter<float> waitTime = 1f;
        public BooleanStatus finishStatus = BooleanStatus.Success;

        public override float length => waitTime.useBlackboard ? 0 : waitTime.value;

        protected override string info => string.Format("Wait {0} sec.", waitTime);

        protected override void OnUpdate() {
            if ( elapsedTime >= waitTime.value ) {
                EndAction(finishStatus == BooleanStatus.Success);
            }
        }
    }
}