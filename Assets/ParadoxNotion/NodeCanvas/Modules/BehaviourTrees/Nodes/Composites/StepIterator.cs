using NodeCanvas.Framework;
using ParadoxNotion.Design;
using UnityEngine;


namespace NodeCanvas.BehaviourTrees
{

    [Name("Step Sequencer")]
    [Category("Composites")]
    [Description("In comparison to a normal Sequencer which executes all its children until one fails, the Step Sequencer executes their children in order (like a sequencer), BUT after every child execution, it returns the child status. Then the next time the Step Sequencer executes, the next child in order will execute.")]
    [ParadoxNotion.Design.Icon("StepSequencer")]
    [Color("bf7fff")]
    public class StepIterator : BTComposite
    {

        private int current;

        public override void OnGraphStarted() {
            current = 0;
        }

        protected override Status OnExecute(Component agent, IBlackboard blackboard) {
            current = current % outConnections.Count;
            return outConnections[current].Execute(agent, blackboard);
        }

        protected override void OnReset() {
            current++;
        }
    }
}