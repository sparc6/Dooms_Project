using UnityEngine;
using UnityEngine.EventSystems;

namespace ParadoxNotion.Services
{

    ///<summary>Automaticaly added to a gameobject when needed. Handles forwarding Unity event messages to listeners that need them as well as Custom event forwarding. Notice: this is a partial class. Add your own methods/events if you like.</summary>
    public partial class EventRouter : MonoBehaviour
            , IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
            IDragHandler, IScrollHandler, IUpdateSelectedHandler, ISelectHandler, IDeselectHandler, IMoveHandler, ISubmitHandler, IDropHandler
    {

        //special router for OnAnimatorMove only
        private EventRouterAnimatorMove _routerAnimatorMove;

        ///----------------------------------------------------------------------------------------------

        public delegate void EventDelegate(EventData msg);
        public delegate void EventDelegate<T>(EventData<T> msg);

        ///----------------------------------------------------------------------------------------------
        public event EventDelegate<PointerEventData> onPointerEnter;
        public event EventDelegate<PointerEventData> onPointerExit;
        public event EventDelegate<PointerEventData> onPointerDown;
        public event EventDelegate<PointerEventData> onPointerUp;
        public event EventDelegate<PointerEventData> onPointerClick;
        public event EventDelegate<PointerEventData> onDrag;
        public event EventDelegate<PointerEventData> onDrop;
        public event EventDelegate<PointerEventData> onScroll;
        public event EventDelegate<BaseEventData> onUpdateSelected;
        public event EventDelegate<BaseEventData> onSelect;
        public event EventDelegate<BaseEventData> onDeselect;
        public event EventDelegate<AxisEventData> onMove;
        public event EventDelegate<BaseEventData> onSubmit;

        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData) { onPointerEnter?.Invoke(new EventData<PointerEventData>(eventData, gameObject, this)); }
        void IPointerExitHandler.OnPointerExit(PointerEventData eventData) { onPointerExit?.Invoke(new EventData<PointerEventData>(eventData, gameObject, this)); }
        void IPointerDownHandler.OnPointerDown(PointerEventData eventData) { onPointerDown?.Invoke(new EventData<PointerEventData>(eventData, gameObject, this)); }
        void IPointerUpHandler.OnPointerUp(PointerEventData eventData) { onPointerUp?.Invoke(new EventData<PointerEventData>(eventData, gameObject, this)); }
        void IPointerClickHandler.OnPointerClick(PointerEventData eventData) { onPointerClick?.Invoke(new EventData<PointerEventData>(eventData, gameObject, this)); }
        void IDragHandler.OnDrag(PointerEventData eventData) { onDrag?.Invoke(new EventData<PointerEventData>(eventData, gameObject, this)); }
        void IDropHandler.OnDrop(PointerEventData eventData) { onDrop?.Invoke(new EventData<PointerEventData>(eventData, gameObject, this)); }
        void IScrollHandler.OnScroll(PointerEventData eventData) { onScroll?.Invoke(new EventData<PointerEventData>(eventData, gameObject, this)); }
        void IUpdateSelectedHandler.OnUpdateSelected(BaseEventData eventData) { onUpdateSelected?.Invoke(new EventData<BaseEventData>(eventData, gameObject, this)); }
        void ISelectHandler.OnSelect(BaseEventData eventData) { onSelect?.Invoke(new EventData<BaseEventData>(eventData, gameObject, this)); }
        void IDeselectHandler.OnDeselect(BaseEventData eventData) { onDeselect?.Invoke(new EventData<BaseEventData>(eventData, gameObject, this)); }
        void IMoveHandler.OnMove(AxisEventData eventData) { onMove?.Invoke(new EventData<AxisEventData>(eventData, gameObject, this)); }
        void ISubmitHandler.OnSubmit(BaseEventData eventData) { onSubmit?.Invoke(new EventData<BaseEventData>(eventData, gameObject, this)); }

        //-------------------------------------------------
        public event EventDelegate onMouseDown;
        public event EventDelegate onMouseDrag;
        public event EventDelegate onMouseEnter;
        public event EventDelegate onMouseExit;
        public event EventDelegate onMouseOver;
        public event EventDelegate onMouseUp;

        void OnMouseDown() { onMouseDown?.Invoke(new EventData(gameObject, this)); }
        void OnMouseDrag() { onMouseDrag?.Invoke(new EventData(gameObject, this)); }
        void OnMouseEnter() { onMouseEnter?.Invoke(new EventData(gameObject, this)); }
        void OnMouseExit() { onMouseExit?.Invoke(new EventData(gameObject, this)); }
        void OnMouseOver() { onMouseOver?.Invoke(new EventData(gameObject, this)); }
        void OnMouseUp() { onMouseUp?.Invoke(new EventData(gameObject, this)); }

        //-------------------------------------------------
        public event EventDelegate onEnable;
        public event EventDelegate onDisable;
        public event EventDelegate onDestroy;

        void OnEnable() { onEnable?.Invoke(new EventData(gameObject, this)); }
        void OnDisable() { onDisable?.Invoke(new EventData(gameObject, this)); }
        void OnDestroy() { onDestroy?.Invoke(new EventData(gameObject, this)); }

        //-------------------------------------------------
        public event EventDelegate onTransformChildrenChanged;
        public event EventDelegate onTransformParentChanged;

        void OnTransformChildrenChanged() { onTransformChildrenChanged?.Invoke(new EventData(gameObject, this)); }
        void OnTransformParentChanged() { onTransformParentChanged?.Invoke(new EventData(gameObject, this)); }

        //-------------------------------------------------
        public event EventDelegate<int> onAnimatorIK;
        public event EventDelegate onAnimatorMove {
            add { if ( _routerAnimatorMove == null ) { _routerAnimatorMove = gameObject.GetAddComponent<EventRouterAnimatorMove>(); } _routerAnimatorMove.onAnimatorMove += value; }
            remove { if ( _routerAnimatorMove != null ) { _routerAnimatorMove.onAnimatorMove -= value; } }
        }

        void OnAnimatorIK(int layerIndex) { onAnimatorIK?.Invoke(new EventData<int>(layerIndex, gameObject, this)); }

        //-------------------------------------------------
        public event EventDelegate onBecameInvisible;
        public event EventDelegate onBecameVisible;

        void OnBecameInvisible() { onBecameInvisible?.Invoke(new EventData(gameObject, this)); }
        void OnBecameVisible() { onBecameVisible?.Invoke(new EventData(gameObject, this)); }

        //-------------------------------------------------
        public event EventDelegate<ControllerColliderHit> onControllerColliderHit;
        public event EventDelegate<GameObject> onParticleCollision;

        void OnControllerColliderHit(ControllerColliderHit hit) { onControllerColliderHit?.Invoke(new EventData<ControllerColliderHit>(hit, gameObject, this)); }
        void OnParticleCollision(GameObject other) { onParticleCollision?.Invoke(new EventData<GameObject>(other, gameObject, this)); }

        //-------------------------------------------------
        public event EventDelegate<Collision> onCollisionEnter;
        public event EventDelegate<Collision> onCollisionExit;
        public event EventDelegate<Collision> onCollisionStay;

        void OnCollisionEnter(Collision collisionInfo) { onCollisionEnter?.Invoke(new EventData<Collision>(collisionInfo, gameObject, this)); }
        void OnCollisionExit(Collision collisionInfo) { onCollisionExit?.Invoke(new EventData<Collision>(collisionInfo, gameObject, this)); }
        void OnCollisionStay(Collision collisionInfo) { onCollisionStay?.Invoke(new EventData<Collision>(collisionInfo, gameObject, this)); }

        public event EventDelegate<Collision2D> onCollisionEnter2D;
        public event EventDelegate<Collision2D> onCollisionExit2D;
        public event EventDelegate<Collision2D> onCollisionStay2D;

        void OnCollisionEnter2D(Collision2D collisionInfo) { onCollisionEnter2D?.Invoke(new EventData<Collision2D>(collisionInfo, gameObject, this)); }
        void OnCollisionExit2D(Collision2D collisionInfo) { onCollisionExit2D?.Invoke(new EventData<Collision2D>(collisionInfo, gameObject, this)); }
        void OnCollisionStay2D(Collision2D collisionInfo) { onCollisionStay2D?.Invoke(new EventData<Collision2D>(collisionInfo, gameObject, this)); }

        //-------------------------------------------------

        public event EventDelegate<Collider> onTriggerEnter;
        public event EventDelegate<Collider> onTriggerExit;
        public event EventDelegate<Collider> onTriggerStay;

        void OnTriggerEnter(Collider other) { onTriggerEnter?.Invoke(new EventData<Collider>(other, gameObject, this)); }
        void OnTriggerExit(Collider other) { onTriggerExit?.Invoke(new EventData<Collider>(other, gameObject, this)); }
        void OnTriggerStay(Collider other) { onTriggerStay?.Invoke(new EventData<Collider>(other, gameObject, this)); }

        public event EventDelegate<Collider2D> onTriggerEnter2D;
        public event EventDelegate<Collider2D> onTriggerExit2D;
        public event EventDelegate<Collider2D> onTriggerStay2D;

        void OnTriggerEnter2D(Collider2D other) { onTriggerEnter2D?.Invoke(new EventData<Collider2D>(other, gameObject, this)); }
        void OnTriggerExit2D(Collider2D other) { onTriggerExit2D?.Invoke(new EventData<Collider2D>(other, gameObject, this)); }
        void OnTriggerStay2D(Collider2D other) { onTriggerStay2D?.Invoke(new EventData<Collider2D>(other, gameObject, this)); }

        ///----------------------------------------------------------------------------------------------

        public event System.Action<RenderTexture, RenderTexture> onRenderImage;
        void OnRenderImage(RenderTexture source, RenderTexture destination) { onRenderImage?.Invoke(source, destination); }
        ///----------------------------------------------------------------------------------------------

        public event EventDelegate onDrawGizmos;
        void OnDrawGizmos() { onDrawGizmos?.Invoke(new EventData(gameObject, this)); }
        ///----------------------------------------------------------------------------------------------


        ///----------------------------------------------------------------------------------------------

        public delegate void CustomEventDelegate(string name, IEventData data);

        ///<summary>Sub/Unsub to a custom named events invoked through this router</summary>
        public event CustomEventDelegate onCustomEvent;

        ///<summary>Invokes a custom named event</summary>
        public void InvokeCustomEvent(string name, object value, object sender) {
            onCustomEvent?.Invoke(name, new EventData(value, gameObject, sender));
        }

        ///<summary>Invokes a custom named event</summary>
        public void InvokeCustomEvent<T>(string name, T value, object sender) {
            onCustomEvent?.Invoke(name, new EventData<T>(value, gameObject, sender));
        }
    }
}