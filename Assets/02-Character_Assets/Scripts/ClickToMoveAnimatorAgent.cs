using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class ClickToMoveAnimatorAgent : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private Camera inputCamera;
    [SerializeField] private LayerMask clickableLayers = ~0;
    [SerializeField] private float clickRayDistance = 500f;
    [SerializeField] private float navMeshSampleDistance = 2f;
    [SerializeField] private bool ignoreClicksOverUI = true;

    [Header("Movement")]
    [SerializeField] private float agentSpeed = 3.5f;
    [SerializeField] private float stoppingDistance = 0.05f;
    [SerializeField] private bool updateRotationFromNavMeshAgent = true;

    [Header("Turn Before Move")]
    [SerializeField] private bool turnBeforeMoving = true;
    [SerializeField] private float turnSpeedDegrees = 540f;
    [SerializeField] private float moveStartAngle = 5f;

    [Header("Animator")]
    [SerializeField] private string speedParameter = "speed";
    [SerializeField] private string turnDirectionParameter = "turnDirection";
    [SerializeField] private bool normalizeAnimatorSpeed = true;
    [SerializeField] private float animatorSpeedForFullBlend = 3.5f;
    [SerializeField] private float animatorDampTime = 0.1f;

    private NavMeshAgent navMeshAgent;
    private Animator animator;
    private int speedParameterHash;
    private int turnDirectionParameterHash;
    private bool hasSpeedParameter;
    private bool hasTurnDirectionParameter;
    private bool hasPendingDestination;
    private Vector3 pendingDestination;
    private float turnDirection;

    public float AgentSpeed
    {
        get => agentSpeed;
        set
        {
            agentSpeed = Mathf.Max(0f, value);
            ApplyMovementSettings();
        }
    }

    public float TurnDirection => turnDirection;

    private void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        if (inputCamera == null)
        {
            inputCamera = Camera.main;
        }

        CacheAnimatorParameters();
        ApplyMovementSettings();
    }

    private void OnValidate()
    {
        agentSpeed = Mathf.Max(0f, agentSpeed);
        stoppingDistance = Mathf.Max(0f, stoppingDistance);
        clickRayDistance = Mathf.Max(0f, clickRayDistance);
        navMeshSampleDistance = Mathf.Max(0f, navMeshSampleDistance);
        turnSpeedDegrees = Mathf.Max(0f, turnSpeedDegrees);
        moveStartAngle = Mathf.Max(0f, moveStartAngle);
        animatorSpeedForFullBlend = Mathf.Max(0.01f, animatorSpeedForFullBlend);
        animatorDampTime = Mathf.Max(0f, animatorDampTime);

        if (navMeshAgent == null)
        {
            navMeshAgent = GetComponent<NavMeshAgent>();
        }

        ApplyMovementSettings();
    }

    private void Update()
    {
        HandleClickInput();
        UpdatePendingTurn();
        UpdateAnimatorSpeed();
    }

    public bool MoveTo(Vector3 worldPosition)
    {
        if (navMeshAgent == null || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
        {
            return false;
        }

        if (!NavMesh.SamplePosition(worldPosition, out NavMeshHit hit, navMeshSampleDistance, navMeshAgent.areaMask))
        {
            return false;
        }

        if (turnBeforeMoving && NeedsTurnBeforeMove(hit.position))
        {
            pendingDestination = hit.position;
            hasPendingDestination = true;
            navMeshAgent.ResetPath();
            navMeshAgent.isStopped = true;
            SetAnimatorSpeed(0f);
            return true;
        }

        hasPendingDestination = false;
        return StartMoveTo(hit.position);
    }

    public void StopMoving()
    {
        if (navMeshAgent == null || !navMeshAgent.enabled)
        {
            return;
        }

        hasPendingDestination = false;
        navMeshAgent.ResetPath();
        navMeshAgent.isStopped = true;
        SetAnimatorSpeed(0f);
    }

    public void SetAgentSpeed(float speed)
    {
        AgentSpeed = speed;
    }

    private void HandleClickInput()
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
        if (Physics.Raycast(ray, out RaycastHit hit, clickRayDistance, clickableLayers, QueryTriggerInteraction.Collide))
        {
            if (HasComponentInParent(hit.collider.transform, "AgentInteractionZone"))
            {
                return;
            }

            MoveTo(hit.point);
        }
    }

    private void UpdatePendingTurn()
    {
        if (!hasPendingDestination)
        {
            SetAnimatorTurnDirection(0f);
            return;
        }

        SetAnimatorTurnDirection(GetTurnDirection(pendingDestination));

        if (RotateTowards(pendingDestination))
        {
            hasPendingDestination = false;
            SetAnimatorTurnDirection(0f);
            StartMoveTo(pendingDestination);
        }
    }

    private bool StartMoveTo(Vector3 destination)
    {
        navMeshAgent.isStopped = false;
        return navMeshAgent.SetDestination(destination);
    }

    private bool NeedsTurnBeforeMove(Vector3 destination)
    {
        Vector3 toDestination = destination - transform.position;
        toDestination.y = 0f;

        if (toDestination.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        float angle = Vector3.Angle(transform.forward, toDestination);
        return angle > moveStartAngle;
    }

    private bool RotateTowards(Vector3 destination)
    {
        Vector3 toDestination = destination - transform.position;
        toDestination.y = 0f;

        if (toDestination.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        Quaternion targetRotation = Quaternion.LookRotation(toDestination.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            turnSpeedDegrees * Time.deltaTime);

        float remainingAngle = Quaternion.Angle(transform.rotation, targetRotation);
        return remainingAngle <= moveStartAngle;
    }

    private float GetTurnDirection(Vector3 destination)
    {
        Vector3 toDestination = destination - transform.position;
        toDestination.y = 0f;

        if (toDestination.sqrMagnitude <= 0.0001f)
        {
            return 0f;
        }

        float signedAngle = Vector3.SignedAngle(transform.forward, toDestination.normalized, Vector3.up);
        return Mathf.Abs(signedAngle) <= moveStartAngle ? 0f : Mathf.Sign(signedAngle);
    }

    private void UpdateAnimatorSpeed()
    {
        if (navMeshAgent == null || animator == null || !hasSpeedParameter)
        {
            return;
        }

        float currentSpeed = navMeshAgent.velocity.magnitude;

        if (!navMeshAgent.pathPending && navMeshAgent.hasPath && navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance)
        {
            currentSpeed = 0f;
        }

        float animatorSpeed = normalizeAnimatorSpeed
            ? Mathf.Clamp01(currentSpeed / animatorSpeedForFullBlend)
            : currentSpeed;

        SetAnimatorSpeed(animatorSpeed);
    }

    private void SetAnimatorSpeed(float value)
    {
        if (animator == null || !hasSpeedParameter)
        {
            return;
        }

        animator.SetFloat(speedParameterHash, value, animatorDampTime, Time.deltaTime);
    }

    private void SetAnimatorTurnDirection(float value)
    {
        turnDirection = value;

        if (animator == null || !hasTurnDirectionParameter)
        {
            return;
        }

        animator.SetFloat(turnDirectionParameterHash, value, animatorDampTime, Time.deltaTime);
    }

    private void CacheAnimatorParameters()
    {
        hasSpeedParameter = false;
        hasTurnDirectionParameter = false;

        if (animator == null)
        {
            return;
        }

        speedParameterHash = string.IsNullOrWhiteSpace(speedParameter) ? 0 : Animator.StringToHash(speedParameter);
        turnDirectionParameterHash = string.IsNullOrWhiteSpace(turnDirectionParameter) ? 0 : Animator.StringToHash(turnDirectionParameter);

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type != AnimatorControllerParameterType.Float)
            {
                continue;
            }

            if (parameter.nameHash == speedParameterHash)
            {
                hasSpeedParameter = true;
            }

            if (parameter.nameHash == turnDirectionParameterHash)
            {
                hasTurnDirectionParameter = true;
            }
        }
    }

    private void ApplyMovementSettings()
    {
        if (navMeshAgent == null)
        {
            return;
        }

        navMeshAgent.speed = agentSpeed;
        navMeshAgent.stoppingDistance = stoppingDistance;
        navMeshAgent.updateRotation = updateRotationFromNavMeshAgent;
    }

    private static bool HasComponentInParent(Transform start, string componentTypeName)
    {
        Transform current = start;
        while (current != null)
        {
            Component[] components = current.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && component.GetType().Name == componentTypeName)
                {
                    return true;
                }
            }

            current = current.parent;
        }

        return false;
    }
}
