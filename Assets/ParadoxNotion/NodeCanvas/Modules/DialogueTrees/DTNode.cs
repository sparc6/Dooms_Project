using NodeCanvas.Framework;
using ParadoxNotion;
using ParadoxNotion.Design;
using UnityEngine;


namespace NodeCanvas.DialogueTrees
{

    ///<summary> Base class for DialogueTree nodes that can live within a DialogueTree Graph.</summary>
    abstract public class DTNode : Node
    {

        [SerializeField] private string _actorName = DialogueTree.INSTIGATOR_NAME;
        [SerializeField] private string _actorParameterID;

        public override string name {
            get
            {
                if ( requireActorSelection ) {
                    if ( DLGTree.definedActorParameterNames.Contains(actorName) ) {
                        return string.Format("{0}", actorName);
                    }
                    return string.Format("<color=#d63e3e>* {0} *</color>", _actorName);
                }
                return base.name;
            }
        }

        virtual public bool requireActorSelection => true;
        public override int maxInConnections => -1;
        public override int maxOutConnections => 1;
        sealed public override System.Type outConnectionType => typeof(DTConnection);
        sealed public override bool allowAsPrime => true;
        sealed public override bool canSelfConnect => false;
        sealed public override Alignment2x2 commentsAlignment => Alignment2x2.Bottom;
        sealed public override Alignment2x2 iconAlignment => Alignment2x2.Bottom;

        protected DialogueTree DLGTree => (DialogueTree)graph;

        ///<summary>The key name actor parameter to be used for this node</summary>
        public string actorName {
            get
            {
                var result = DLGTree.GetParameterByID(_actorParameterID);
                return result != null ? result.name : _actorName;
            }
            private set
            {
                if ( _actorName != value && !string.IsNullOrEmpty(value) ) {
                    _actorName = value;
                    var param = DLGTree.GetParameterByName(value);
                    _actorParameterID = param?.ID;
                }
            }
        }

        ///<summary>The DialogueActor that will execute the node</summary>
        public IDialogueActor finalActor {
            get
            {
                var result = DLGTree.GetActorReferenceByID(_actorParameterID);
                return result != null ? result : DLGTree.GetActorReferenceByName(_actorName);
            }
        }


        ///----------------------------------------------------------------------------------------------
        ///---------------------------------------UNITY EDITOR-------------------------------------------
#if UNITY_EDITOR

        protected override void OnNodeInspectorGUI() {
            if ( requireActorSelection ) {
                GUI.backgroundColor = Colors.lightBlue;
                actorName = EditorUtils.Popup<string>(actorName, DLGTree.definedActorParameterNames);
                GUI.backgroundColor = Color.white;
            }
            base.OnNodeInspectorGUI();
        }

        protected override UnityEditor.GenericMenu OnContextMenu(UnityEditor.GenericMenu menu) {
            menu.AddItem(new GUIContent("Breakpoint"), isBreakpoint, () => { isBreakpoint = !isBreakpoint; });
            return menu;
        }

#endif
    }
}