using NodeCanvas.Framework;
using ParadoxNotion;
using ParadoxNotion.Design;
using UnityEngine;


namespace NodeCanvas.Tasks.Conditions
{

    [Category("System Events")]
    public class CheckMouse : ConditionTask<Collider>
    {

        public MouseInteractionType checkType = MouseInteractionType.MouseEnter;

        protected override string info => checkType.ToString();

        protected override bool OnCheck() {
            return false;
        }

        protected override void OnEnable() {
            router.onMouseEnter += OnMouseEnter;
            router.onMouseExit += OnMouseExit;
            router.onMouseOver += OnMouseOver;
        }

        protected override void OnDisable() {
            router.onMouseEnter -= OnMouseEnter;
            router.onMouseExit -= OnMouseExit;
            router.onMouseOver -= OnMouseOver;
        }

        void OnMouseEnter(EventData msg) {
            if ( checkType == MouseInteractionType.MouseEnter ) {
                YieldReturn(true);
            }
        }

        void OnMouseExit(EventData msg) {
            if ( checkType == MouseInteractionType.MouseExit ) {
                YieldReturn(true);
            }
        }

        void OnMouseOver(EventData msg) {
            if ( checkType == MouseInteractionType.MouseOver ) {
                YieldReturn(true);
            }
        }
    }
}