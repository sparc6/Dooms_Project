using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;


namespace NodeCanvas.Tasks.Actions
{

    [Category("GameObject")]
    public class GetComponent<T> : ActionTask<Transform> where T : Component
    {

        [BlackboardOnly]
        public BBParameter<T> saveAs;

        protected override string info => string.Format("{0} Get {1}", agentInfo, typeof(T).Name);

        protected override void OnExecute() {
            var o = agent.GetComponent<T>();
            saveAs.value = o;
            EndAction(o != null);
        }
    }
}