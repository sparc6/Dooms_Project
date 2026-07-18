using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(NavMeshAgent))]
public class AgentInteractionController : MonoBehaviour
{
    [Header("Click Input")]
    [SerializeField] private Camera inputCamera;
    [SerializeField] private LayerMask interactionLayers = ~0;
    [SerializeField] private float clickRayDistance = 500f;
    [SerializeField] private bool ignoreClicksOverUI = true;

    [Header("References")]
    [SerializeField] private ClickToMoveAnimatorAgent movementAgent;
    [SerializeField] private Animator animator;
    [SerializeField] private NavMeshAgent navMeshAgent;
    [SerializeField] private Transform boneSearchRoot;

    [Header("Arrival")]
    [SerializeField] private float arriveDistance = 0.15f;
    [SerializeField] private float poseRotationSpeedDegrees = 540f;
    [SerializeField] private float poseRotationTolerance = 2f;
    [SerializeField] private bool useNavMeshWarpForPoseSnap = true;

    private Coroutine interactionRoutine;
    private ActivePropState activeProp;
    private AgentInteractionZone activeZone;

    private class ActivePropState
    {
        public Transform prop;
        public Transform originalParent;
        public Vector3 originalLocalPosition;
        public Quaternion originalLocalRotation;
        public Vector3 originalLocalScale;
    }

    private void Awake()
    {
        if (inputCamera == null)
        {
            inputCamera = Camera.main;
        }

        if (movementAgent == null)
        {
            movementAgent = GetComponent<ClickToMoveAnimatorAgent>();
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (navMeshAgent == null)
        {
            navMeshAgent = GetComponent<NavMeshAgent>();
        }

        if (boneSearchRoot == null)
        {
            boneSearchRoot = transform;
        }
    }

    private void OnValidate()
    {
        clickRayDistance = Mathf.Max(0f, clickRayDistance);
        arriveDistance = Mathf.Max(0f, arriveDistance);
        poseRotationSpeedDegrees = Mathf.Max(0f, poseRotationSpeedDegrees);
        poseRotationTolerance = Mathf.Max(0f, poseRotationTolerance);
    }

    private void Update()
    {
        HandleInteractionClick();
    }

    public void StartInteraction(AgentInteractionZone zone)
    {
        if (zone == null || zone.PoseReference == null)
        {
            return;
        }

        if (interactionRoutine != null)
        {
            StopCoroutine(interactionRoutine);
        }

        if (activeZone != null)
        {
            CompleteCurrentInteraction();
        }

        interactionRoutine = StartCoroutine(RunInteraction(zone));
    }

    public void CompleteCurrentInteraction()
    {
        if (activeZone != null && activeZone.ResetBoolWhenDone)
        {
            SetAnimatorBool(activeZone.AnimatorBoolName, false);
        }

        if (activeZone != null && activeZone.ReturnPropWhenDone)
        {
            ReturnActiveProp(activeZone);
        }

        activeZone = null;
        activeProp = null;
        interactionRoutine = null;
    }

    private void HandleInteractionClick()
    {
        if (!Input.GetMouseButtonDown(0) || inputCamera == null)
        {
            return;
        }

        if (ignoreClicksOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        Ray ray = inputCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, clickRayDistance, interactionLayers, QueryTriggerInteraction.Collide))
        {
            CompletePersistentInteractionOnNextClick();
            return;
        }

        AgentInteractionZone zone = hit.collider.GetComponentInParent<AgentInteractionZone>();
        if (zone != null)
        {
            StartInteraction(zone);
            return;
        }

        CompletePersistentInteractionOnNextClick();
    }

    private IEnumerator RunInteraction(AgentInteractionZone zone)
    {
        activeZone = zone;

        if (movementAgent != null)
        {
            movementAgent.MoveTo(zone.PoseReference.position);
        }
        else if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.isStopped = false;
            navMeshAgent.SetDestination(zone.PoseReference.position);
        }

        yield return WaitForArrival(zone.PoseReference.position);

        if (movementAgent != null)
        {
            movementAgent.StopMoving();
        }
        else if (navMeshAgent != null && navMeshAgent.enabled)
        {
            navMeshAgent.ResetPath();
            navMeshAgent.isStopped = true;
        }

        if (zone.SnapAgentToPose)
        {
            SnapToPosePosition(zone.PoseReference.position);
        }

        yield return RotateToPose(zone.PoseReference.rotation);

        AttachProp(zone);
        SetAnimatorBool(zone.AnimatorBoolName, true);

        if (zone.CompletionMode == InteractionCompletionMode.Timed && zone.ActionDuration > 0f)
        {
            yield return new WaitForSeconds(zone.ActionDuration);
            CompleteCurrentInteraction();
            yield break;
        }

        interactionRoutine = null;
    }

    private IEnumerator WaitForArrival(Vector3 destination)
    {
        while (true)
        {
            if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
            {
                if (Vector3.Distance(transform.position, destination) <= arriveDistance)
                {
                    yield break;
                }
            }
            else if (!navMeshAgent.pathPending)
            {
                float remaining = navMeshAgent.hasPath ? navMeshAgent.remainingDistance : Vector3.Distance(transform.position, destination);
                if (remaining <= Mathf.Max(arriveDistance, navMeshAgent.stoppingDistance))
                {
                    yield break;
                }
            }

            yield return null;
        }
    }

    private IEnumerator RotateToPose(Quaternion targetRotation)
    {
        while (Quaternion.Angle(transform.rotation, targetRotation) > poseRotationTolerance)
        {
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                poseRotationSpeedDegrees * Time.deltaTime);

            yield return null;
        }

        transform.rotation = targetRotation;
    }

    private void SnapToPosePosition(Vector3 position)
    {
        if (navMeshAgent != null && navMeshAgent.enabled && navMeshAgent.isOnNavMesh && useNavMeshWarpForPoseSnap)
        {
            navMeshAgent.Warp(position);
            return;
        }

        transform.position = position;
    }

    private void AttachProp(AgentInteractionZone zone)
    {
        Transform prop = zone.Prop;
        if (prop == null)
        {
            return;
        }

        Transform attachmentTarget = zone.AttachmentTargetOverride != null
            ? zone.AttachmentTargetOverride
            : FindDeepChild(boneSearchRoot, zone.AttachmentBoneName);

        if (attachmentTarget == null)
        {
            Debug.LogWarning($"Interaction '{zone.name}' could not find attachment bone '{zone.AttachmentBoneName}'.", this);
            return;
        }

        activeProp = new ActivePropState
        {
            prop = prop,
            originalParent = prop.parent,
            originalLocalPosition = prop.localPosition,
            originalLocalRotation = prop.localRotation,
            originalLocalScale = prop.localScale
        };

        prop.SetParent(attachmentTarget, false);
        prop.localPosition = zone.AttachedLocalPosition;
        prop.localRotation = Quaternion.Euler(zone.AttachedLocalEulerAngles);
        prop.localScale = zone.AttachedLocalScale;
    }

    private void ReturnActiveProp(AgentInteractionZone zone)
    {
        if (activeProp == null || activeProp.prop == null)
        {
            return;
        }

        if (zone.PropHome != null)
        {
            activeProp.prop.SetParent(zone.PropHome, false);
            activeProp.prop.localPosition = zone.ReturnedLocalPosition;
            activeProp.prop.localRotation = Quaternion.Euler(zone.ReturnedLocalEulerAngles);
            activeProp.prop.localScale = Vector3.one;
            return;
        }

        activeProp.prop.SetParent(activeProp.originalParent, false);
        activeProp.prop.localPosition = activeProp.originalLocalPosition;
        activeProp.prop.localRotation = activeProp.originalLocalRotation;
        activeProp.prop.localScale = activeProp.originalLocalScale;
    }

    private void SetAnimatorBool(string boolName, bool value)
    {
        if (animator == null || string.IsNullOrWhiteSpace(boolName))
        {
            return;
        }

        int hash = Animator.StringToHash(boolName);
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.nameHash == hash)
            {
                animator.SetBool(hash, value);
                return;
            }
        }

        Debug.LogWarning($"Animator on '{name}' does not have a bool parameter named '{boolName}'.", this);
    }

    private void CompletePersistentInteractionOnNextClick()
    {
        if (activeZone != null && activeZone.CompletionMode == InteractionCompletionMode.UntilNextClickOrInteraction)
        {
            CompleteCurrentInteraction();
        }
    }

    private static Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null || string.IsNullOrWhiteSpace(childName))
        {
            return null;
        }

        if (parent.name == childName)
        {
            return parent;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform result = FindDeepChild(parent.GetChild(i), childName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}
