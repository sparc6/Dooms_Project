using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MLA_SIM
{
    // Minimal, non-intrusive animator driver for 2D locomotion BlendTree
    // Drives MoveX (strafe), MoveZ (forward), optional Turn (yaw rate), and IsMoving
    // from NavMeshAgent signals. Works with existing actions (MoveTo, Find, etc.).
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class AnimatorLocomotionDriver : MonoBehaviour
    {
        [Header("Animator Parameter Names")]
        public string moveXParam = "MoveX";
        public string moveZParam = "MoveZ";
        public string turnParam = "Turn";
        public string isMovingParam = "IsMoving";

        [Header("Locomotion Configuration")]
        [Tooltip("The state name of the locomotion blend tree state in the Animator.")]
        public string locomotionStateName = "Locomotion";

        [System.Serializable]
        public class LocoStyleEntry
        {
            [Tooltip("Style name used in code/inspector (e.g. BTGun, Drunk). Case-insensitive.")]
            public string name = "";
            [Tooltip("The LocoStyle integer the Animator switches on for this style.")]
            public int index = 0;
        }

        [Tooltip("Named locomotion styles -> LocoStyle integer. Author arbitrary styles (BTGun, Drunk, ...) here so they can be referenced by name (defaultLocoStyle, routine loco style) instead of relying on hardcoded names.")]
        public List<LocoStyleEntry> locoStyles = new List<LocoStyleEntry>
        {
            new LocoStyleEntry { name = "March", index = 1 },
            new LocoStyleEntry { name = "Angry", index = 2 },
            new LocoStyleEntry { name = "Sneak", index = 3 },
        };

        /// <summary>The loco style name currently applied (last passed to SetLocoStyle(string)).</summary>
        public string CurrentLocoStyleName { get; private set; } = "";
        /// <summary>Fires when the named loco style changes. Consumed by AnimatorPropDriver to carry style-bound props.</summary>
        public event System.Action<string> OnLocoStyleChanged;

        [Header("Normalization Speeds (m/s)")]
        [Tooltip("Max forward speed used to normalize MoveZ (e.g., run speed)")]
        public float maxForwardSpeed = 5.5f;
        [Tooltip("Max strafe speed used to normalize MoveX")]
        public float maxStrafeSpeed = 3.0f;

        [Header("Turning")]
        [Tooltip("Enable writing to Turn parameter (normalized yaw rate)")]
        public bool enableTurnParam = true;
        [Tooltip("Assumed max yaw rate in deg/s for normalization of Turn")]
        public float maxTurnDegPerSec = 360f;

        [Header("Smoothing")]
        public float damping = 0.10f;          // seconds
        public float turnDamping = 0.10f;      // seconds

        [Header("Agent Variance")]
        [Tooltip("Animator float parameter seeded once per agent from the discrete steps below.")]
        public string agentVarianceParam = "AgentVariance";
        [Tooltip("Discrete blend values to pick from. Each value corresponds to one clip threshold in the blend tree.")]
        public float[] agentVarianceSteps = { 0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f };
        [Tooltip("Animator state names that use AgentVariance. When the clip in any of these states finishes (loops), a new random step is picked. Leave empty to re-roll on every state's loop.")]
        public string[] agentVarianceStateNames = { };

        [Header("Action Blending")]
        [Tooltip("Default crossfade (s) when entering an action state from locomotion.")]
        public float actionEnterCrossfade = 0.22f;
        [Tooltip("Crossfade (s) when returning to locomotion after an action. Slightly longer reads smoother.")]
        public float actionReturnCrossfade = 0.30f;

        [Header("Movement Flags")]
        public float movingSpeedThreshold = 0.10f; // m/s to set IsMoving

        private Animator _animator;
        private NavMeshAgent _agent;
        private CharacterController _characterController;

        // Cache if parameters exist to avoid warnings and overhead
        private readonly Dictionary<string, bool> _hasFloatParam = new();
        private readonly Dictionary<string, bool> _hasBoolParam = new();

        private Vector3 _lastForward;
        private Vector3 _lastPosition; // For manual velocity calculation

        // ---- Plan M4: cached set of state name hashes on the base layer for O(1) Has() checks. ----
        private HashSet<int> _baseLayerStateHashes;
        // Active timed action state (when callers passed holdSeconds > 0). Cleared by Update.
        private Coroutine _holdRoutine;

        // Agent variance re-roll tracking
        private HashSet<int> _agentVarianceStateHashes = new HashSet<int>();
        private float _lastVarianceNormalizedTime = 0f;
        private int _lastVarianceStateHash = 0;
        private float _currentVarianceValue = -1f;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _agent = GetComponent<NavMeshAgent>();
            _characterController = GetComponent<CharacterController>();
            _lastForward = transform.forward;
            _lastPosition = transform.position;
            CacheAnimatorParams();
            SeedAgentVariance();
        }

        private void SeedAgentVariance()
        {
            if (string.IsNullOrEmpty(agentVarianceParam)) return;
            if (agentVarianceSteps == null || agentVarianceSteps.Length == 0) return;
            if (!_hasFloatParam.TryGetValue(agentVarianceParam, out bool exists) || !exists) return;

            _agentVarianceStateHashes.Clear();
            if (agentVarianceStateNames != null)
                foreach (var s in agentVarianceStateNames)
                    if (!string.IsNullOrEmpty(s))
                        _agentVarianceStateHashes.Add(Animator.StringToHash(s));

            _currentVarianceValue = agentVarianceSteps[Random.Range(0, agentVarianceSteps.Length)];
            _animator.SetFloat(agentVarianceParam, _currentVarianceValue);
        }

        private void RerollAgentVariance()
        {
            if (agentVarianceSteps == null || agentVarianceSteps.Length < 2) return;
            if (!_hasFloatParam.TryGetValue(agentVarianceParam, out bool exists) || !exists) return;
            float next = _currentVarianceValue;
            for (int i = 0; i < 8 && Mathf.Approximately(next, _currentVarianceValue); i++)
                next = agentVarianceSteps[Random.Range(0, agentVarianceSteps.Length)];
            _currentVarianceValue = next;
            _animator.SetFloat(agentVarianceParam, _currentVarianceValue);
        }

        private void UpdateAgentVarianceReroll()
        {
            if (agentVarianceSteps == null || agentVarianceSteps.Length < 2) return;
            if (string.IsNullOrEmpty(agentVarianceParam)) return;
            if (!_hasFloatParam.TryGetValue(agentVarianceParam, out bool exists) || !exists) return;

            var info = _animator.GetCurrentAnimatorStateInfo(0);

            // State changed — reset tracking without rerolling
            if (info.shortNameHash != _lastVarianceStateHash)
            {
                _lastVarianceStateHash = info.shortNameHash;
                _lastVarianceNormalizedTime = info.normalizedTime;
                return;
            }

            // Optional state filter: only reroll while in a listed state
            if (_agentVarianceStateHashes.Count > 0 && !_agentVarianceStateHashes.Contains(info.shortNameHash))
            {
                _lastVarianceNormalizedTime = info.normalizedTime;
                return;
            }

            // Detect loop: normalizedTime wrapped back (clip restarted)
            if (info.normalizedTime < _lastVarianceNormalizedTime && _lastVarianceNormalizedTime > 0.8f)
                RerollAgentVariance();

            _lastVarianceNormalizedTime = info.normalizedTime;
        }

        private void CacheAnimatorParams()
        {
            _hasFloatParam.Clear();
            _hasBoolParam.Clear();
            if (_animator == null) return;
            foreach (var p in _animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Float)
                {
                    _hasFloatParam[p.name] = true;
                }
                else if (p.type == AnimatorControllerParameterType.Bool)
                {
                    _hasBoolParam[p.name] = true;
                }
            }

            // Ensure keys exist even if false
            if (!_hasFloatParam.ContainsKey(moveXParam)) _hasFloatParam[moveXParam] = false;
            if (!_hasFloatParam.ContainsKey(moveZParam)) _hasFloatParam[moveZParam] = false;
            if (!_hasFloatParam.ContainsKey(turnParam)) _hasFloatParam[turnParam] = false;
            if (!_hasBoolParam.ContainsKey(isMovingParam)) _hasBoolParam[isMovingParam] = false;
        }

        private void Update()
        {
            if (_animator == null || _agent == null) return;
            float dt = Time.deltaTime > 0f ? Time.deltaTime : 0.02f;

            // Compute local velocity components
            Vector3 currentPos = transform.position;
            Vector3 velW = Vector3.zero; // Initialize velocity

            if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh && _agent.hasPath)
            {
                velW = _agent.velocity;
            }
            else
            {
                // Calculate effective velocity from position change for non-NavMesh movement
                if (dt > 0.0001f)
                {
                    velW = (currentPos - _lastPosition) / dt;
                }
                
                if (velW.sqrMagnitude < 0.0001f && _characterController != null && _characterController.enabled)
                {
                    velW = _characterController.velocity;
                }
            }
            _lastPosition = currentPos;

            Vector3 velL = transform.InverseTransformDirection(velW);
            float forward = velL.z; // + forward, - backward
            float strafe = velL.x;  // + right, - left

            // Normalize into [-1, 1] for 2D BlendTree
            float moveZ = 0f;
            float moveX = 0f;
            if (maxForwardSpeed > 0.01f)
                moveZ = Mathf.Clamp(forward / maxForwardSpeed, -1f, 1f);
            if (maxStrafeSpeed > 0.01f)
                moveX = Mathf.Clamp(strafe / maxStrafeSpeed, -1f, 1f);

            // Set MoveX/MoveZ with damping
            if (_hasFloatParam.TryGetValue(moveZParam, out bool hasZ) && hasZ)
            {
                _animator.SetFloat(moveZParam, moveZ, damping, dt);
            }
            if (_hasFloatParam.TryGetValue(moveXParam, out bool hasX) && hasX)
            {
                _animator.SetFloat(moveXParam, moveX, damping, dt);
            }

            // Compute actual yaw rate from last frame (deg/s)
            float turnNorm = 0f;
            if (enableTurnParam && _hasFloatParam.TryGetValue(turnParam, out bool hasTurn) && hasTurn)
            {
                Vector3 fwdNow = transform.forward;
                float angleDelta = Vector3.SignedAngle(_lastForward, fwdNow, Vector3.up);
                float yawRateDegPerSec = angleDelta / Mathf.Max(dt, 0.0001f);

                // Normalize by assumed max yaw rate
                if (maxTurnDegPerSec > 0.01f)
                {
                    turnNorm = Mathf.Clamp(yawRateDegPerSec / maxTurnDegPerSec, -1f, 1f);
                    _animator.SetFloat(turnParam, turnNorm, turnDamping, dt);
                }
                _lastForward = fwdNow;
            }

            // Set IsMoving
            if (_hasBoolParam.TryGetValue(isMovingParam, out bool hasMoving) && hasMoving)
            {
                bool isMoving = velW.magnitude >= movingSpeedThreshold;
                
                // If using NavMeshAgent, also check path status
                if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh && _agent.hasPath)
                {
                    isMoving = isMoving && _agent.hasPath && !_agent.pathPending;
                }
                
                _animator.SetBool(isMovingParam, isMoving);
            }

            UpdateAgentVarianceReroll();
        }

        // Event fired when an action state transitions
        public event System.Action<string> OnStateChanged;

        // Last base-layer state the driver played/crossfaded to. Exposed for telemetry
        // so a logger can record what was visually playing without polling the Animator.
        public string CurrentStateName { get; private set; } = "";

        private void RaiseStateChanged(string stateName)
        {
            CurrentStateName = stateName;
            OnStateChanged?.Invoke(stateName);
        }

        // ---------------------------------------------------------------
        // Plan M4: Unified action state entry point.
        //
        // Replaces the per-action bool-parameter explosion with a single call
        // that crossfades the Animator base layer to a state named exactly
        // after the action verb (e.g. "Fix", "Pickup", "Talk", "Build").
        //
        // Callers (interaction points, DOOMS brains, NPC pickers) pass:
        //   - stateName     : Animator state on base layer
        //   - crossfade     : seconds; default 0.15s. 0 = instant.
        //   - holdSeconds   : if > 0, the driver returns to locomotionStateName
        //                     after that many seconds. <= 0 = caller must
        //                     trigger the next state explicitly.
        //
        // Returns true if the state existed and CrossFade was issued, false
        // if the state name was missing on the controller (no warning spam).
        // ---------------------------------------------------------------
        public bool PlayActionState(string stateName, float crossfade = 0.15f, float holdSeconds = -1f)
        {
            if (_animator == null || string.IsNullOrEmpty(stateName)) return false;
            EnsureBaseLayerStateCache();
            int hash = Animator.StringToHash(stateName);
            // _baseLayerStateHashes == null means "we couldn't enumerate" (build / runtime)
            // and we trust the caller. Non-null means we know the full set.
            if (_baseLayerStateHashes != null && !_baseLayerStateHashes.Contains(hash))
            {
                return false;
            }
            float fade = Mathf.Max(0f, crossfade);
            if (fade <= 0.001f)
                _animator.Play(hash, 0, 0f);
            else
                _animator.CrossFadeInFixedTime(hash, fade, 0, 0f);

            // Fire state change event
            RaiseStateChanged(stateName);

            if (_holdRoutine != null)
            {
                StopCoroutine(_holdRoutine);
                _holdRoutine = null;
            }
            if (holdSeconds > 0f)
            {
                _holdRoutine = StartCoroutine(ReturnToLocomotionAfter(holdSeconds, fade));
            }
            return true;
        }

        public bool PlayActionSequence(string sequenceId, float holdSeconds = -1f)
        {
            if (_animator == null || string.IsNullOrEmpty(sequenceId)) return false;

            var registry = AnimationSequenceRegistry.Instance;
            var seq = registry != null ? registry.FindSequence(sequenceId) : null;

            if (seq == null)
            {
                return PlayActionState(sequenceId, actionEnterCrossfade, holdSeconds);
            }

            if (_holdRoutine != null)
            {
                StopCoroutine(_holdRoutine);
                _holdRoutine = null;
            }

            _holdRoutine = seq.useBlendTree
                ? StartCoroutine(ExecuteBlendTreeCo(seq, holdSeconds))
                : StartCoroutine(ExecuteSequenceCo(seq, holdSeconds));
            return true;
        }

        public void StopActionPlayback(bool returnToLocomotion = true)
        {
            if (_holdRoutine != null)
            {
                StopCoroutine(_holdRoutine);
                _holdRoutine = null;
            }

            if (_animator == null || !_animator.gameObject.activeInHierarchy) return;

            if (returnToLocomotion)
            {
                int locoHash = Animator.StringToHash(locomotionStateName);
                _animator.CrossFadeInFixedTime(locoHash, Mathf.Max(0.05f, actionReturnCrossfade), 0, 0f);
                RaiseStateChanged(locomotionStateName);
            }
        }

        private System.Collections.IEnumerator ExecuteSequenceCo(ActionAnimSequence seq, float holdSeconds)
        {
            float startFade = Mathf.Max(0f, seq.startCrossfade);

            if (!string.IsNullOrEmpty(seq.startState))
            {
                yield return PlayStateAndWait(seq.startState, startFade);
            }

            var holdSteps = seq.GetHoldSteps();
            if (holdSteps != null && holdSteps.Count > 0)
            {
                bool hasBudget = holdSeconds > 0f;
                float budget = Mathf.Max(0f, holdSeconds);
                float elapsed = 0f;
                int stepIndex = 0;
                int guard = 0;

                do
                {
                    var step = holdSteps[stepIndex];
                    if (step != null && !string.IsNullOrEmpty(step.stateName))
                    {
                        int stepHash = Animator.StringToHash(step.stateName);
                        if (startFade <= 0.001f)
                            _animator.Play(stepHash, 0, 0f);
                        else
                            _animator.CrossFadeInFixedTime(stepHash, startFade, 0, 0f);

                        RaiseStateChanged(step.stateName);

                        yield return new WaitForSeconds(startFade);

                        var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
                        float stepWait = stateInfo.length > 0f
                            ? Mathf.Max(0f, stateInfo.length - startFade)
                            : 1.5f;

                        if (hasBudget)
                        {
                            float remaining = Mathf.Max(0f, budget - elapsed);
                            if (remaining <= 0f)
                            {
                                break;
                            }
                            stepWait = Mathf.Min(stepWait, remaining);
                        }

                        if (stepWait > 0f)
                        {
                            yield return new WaitForSeconds(stepWait);
                            elapsed += stepWait;
                        }
                    }

                    stepIndex++;
                    if (stepIndex >= holdSteps.Count)
                    {
                        if (!hasBudget)
                        {
                            break;
                        }
                        stepIndex = 0;
                    }

                    guard++;
                    if (guard > 1000)
                    {
                        Debug.LogWarning("[DOOMS][AnimatorLocomotionDriver] Hold-step loop guard tripped; aborting sequence playback.");
                        break;
                    }
                }
                while (!hasBudget || elapsed < budget);
            }
            else if (holdSeconds > 0f)
            {
                yield return new WaitForSeconds(holdSeconds);
            }

            float endFade = Mathf.Max(0f, seq.endCrossfade);
            if (!string.IsNullOrEmpty(seq.endState))
            {
                yield return PlayStateAndWait(seq.endState, endFade);
            }

            // 4. Return to Locomotion
            int locoHash = Animator.StringToHash(locomotionStateName);
            float returnFade = Mathf.Max(Mathf.Max(startFade, endFade), actionReturnCrossfade);
            if (returnFade <= 0.001f) _animator.Play(locoHash, 0, 0f);
            else _animator.CrossFadeInFixedTime(locoHash, returnFade, 0, 0f);

            RaiseStateChanged(locomotionStateName);
            _holdRoutine = null;
        }

        // Blendtree action: enter a single blendtree state and drive its float
        // parameter through blendKeys over the hold duration (e.g. aim->shoot->settle).
        // holdSeconds <= 0 holds the final key indefinitely (used by Death).
        private System.Collections.IEnumerator ExecuteBlendTreeCo(ActionAnimSequence seq, float holdSeconds)
        {
            if (_animator == null || string.IsNullOrEmpty(seq.blendTreeState))
            {
                _holdRoutine = null;
                yield break;
            }

            float enterFade = Mathf.Max(0f, actionEnterCrossfade);
            int stateHash = Animator.StringToHash(seq.blendTreeState);
            if (enterFade <= 0.001f) _animator.Play(stateHash, 0, 0f);
            else _animator.CrossFadeInFixedTime(stateHash, enterFade, 0, 0f);

            // Notify so the prop driver spawns the blend prop for this state.
            RaiseStateChanged(seq.blendTreeState);

            bool hasParam = !string.IsNullOrEmpty(seq.blendParam)
                && _hasFloatParam.TryGetValue(seq.blendParam, out bool hp) && hp;
            var keys = seq.blendKeys;
            bool hold = holdSeconds <= 0f;
            float duration = hold ? 0f : holdSeconds;

            if (hasParam && keys != null && keys.Count > 0)
            {
                _animator.SetFloat(seq.blendParam, keys[0].value);

                if (!hold && duration > 0f)
                {
                    float t = 0f;
                    while (t < duration)
                    {
                        float n = Mathf.Clamp01(t / duration);
                        _animator.SetFloat(seq.blendParam, EvaluateBlend(keys, n));
                        yield return null;
                        t += Time.deltaTime;
                    }
                }
                _animator.SetFloat(seq.blendParam, keys[keys.Count - 1].value);
            }
            else if (!hold && duration > 0f)
            {
                yield return new WaitForSeconds(duration);
            }

            if (hold)
            {
                // Indefinite (e.g. death): hold the final pose; caller controls exit.
                _holdRoutine = null;
                yield break;
            }

            int locoHash = Animator.StringToHash(locomotionStateName);
            float returnFade = Mathf.Max(actionReturnCrossfade, enterFade);
            if (returnFade <= 0.001f) _animator.Play(locoHash, 0, 0f);
            else _animator.CrossFadeInFixedTime(locoHash, returnFade, 0, 0f);
            RaiseStateChanged(locomotionStateName);
            _holdRoutine = null;
        }

        private static float EvaluateBlend(List<BlendKey> keys, float t01)
        {
            if (keys == null || keys.Count == 0) return 0f;
            if (keys.Count == 1) return keys[0].value;
            if (t01 <= keys[0].time01) return keys[0].value;
            for (int i = 1; i < keys.Count; i++)
            {
                if (t01 <= keys[i].time01)
                {
                    var a = keys[i - 1];
                    var b = keys[i];
                    float span = Mathf.Max(0.0001f, b.time01 - a.time01);
                    float f = Mathf.Clamp01((t01 - a.time01) / span);
                    return Mathf.Lerp(a.value, b.value, f);
                }
            }
            return keys[keys.Count - 1].value;
        }

        private System.Collections.IEnumerator PlayStateAndWait(string stateName, float fade)
        {
            if (_animator == null || string.IsNullOrEmpty(stateName)) yield break;

            int hash = Animator.StringToHash(stateName);
            if (fade <= 0.001f) _animator.Play(hash, 0, 0f);
            else _animator.CrossFadeInFixedTime(hash, fade, 0, 0f);

            RaiseStateChanged(stateName);

            if (fade > 0f)
            {
                yield return new WaitForSeconds(fade);
            }

            var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
            float duration = stateInfo.length > 0f ? Mathf.Max(0f, stateInfo.length - fade) : 1.5f;
            if (duration > 0f)
            {
                yield return new WaitForSeconds(duration);
            }
        }

        // ---------------------------------------------------------------
        // Bump reaction: pick a random Animator state that actually exists
        // from the supplied candidate names and play it as a brief one-shot
        // that auto-returns to locomotion after holdSeconds. Used by the
        // DOOMS T4 brain when an agent makes contact with another agent.
        // Returns false (no-op) if none of the candidate states exist, so
        // callers can still pause + reroute without an authored clip.
        // ---------------------------------------------------------------
        public bool PlayBumpReaction(string[] candidates, float holdSeconds)
        {
            if (_animator == null || candidates == null || candidates.Length == 0) return false;

            // Collect only the candidates that resolve to a real base-layer state.
            int presentCount = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!string.IsNullOrEmpty(candidates[i]) && HasActionState(candidates[i]))
                    presentCount++;
            }
            if (presentCount == 0) return false;

            int pick = Random.Range(0, presentCount);
            string chosen = null;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (string.IsNullOrEmpty(candidates[i]) || !HasActionState(candidates[i])) continue;
                if (pick == 0) { chosen = candidates[i]; break; }
                pick--;
            }

            if (string.IsNullOrEmpty(chosen)) return false;
            return PlayActionState(chosen, 0.08f, holdSeconds);
        }

        public void SetLocoStyle(int style)
        {
            if (_animator == null) return;
            _animator.SetInteger("LocoStyle", style);
        }

        public void SetLocoStyle(string styleName)
        {
            SetLocoStyle(ResolveLocoStyleIndex(styleName));

            string norm = styleName ?? "";
            if (!string.Equals(norm, CurrentLocoStyleName, System.StringComparison.OrdinalIgnoreCase))
            {
                CurrentLocoStyleName = norm;
                OnLocoStyleChanged?.Invoke(norm);
            }
        }

        // Map a style name to its LocoStyle integer: authored locoStyles list first,
        // then the legacy hardcoded names, then a raw integer, else 0 (default).
        private int ResolveLocoStyleIndex(string styleName)
        {
            if (string.IsNullOrEmpty(styleName)) return 0;

            if (locoStyles != null)
            {
                for (int i = 0; i < locoStyles.Count; i++)
                {
                    var e = locoStyles[i];
                    if (e != null && string.Equals(e.name, styleName, System.StringComparison.OrdinalIgnoreCase))
                        return e.index;
                }
            }

            // Legacy fallbacks so existing data keeps working even if the list is cleared.
            if (string.Equals(styleName, "March", System.StringComparison.OrdinalIgnoreCase) || string.Equals(styleName, "BT_March", System.StringComparison.OrdinalIgnoreCase))
                return 1;
            if (string.Equals(styleName, "Angry", System.StringComparison.OrdinalIgnoreCase) || string.Equals(styleName, "BT_Angry", System.StringComparison.OrdinalIgnoreCase))
                return 2;
            if (string.Equals(styleName, "Sneak", System.StringComparison.OrdinalIgnoreCase) || string.Equals(styleName, "BT_Sneak", System.StringComparison.OrdinalIgnoreCase))
                return 3;
            if (int.TryParse(styleName, out int styleVal))
                return styleVal;
            return 0;
        }

        public bool HasActionState(string stateName)
        {
            if (_animator == null || string.IsNullOrEmpty(stateName)) return false;
            EnsureBaseLayerStateCache();
            // Unknown set (build): assume yes; the Animator will silently no-op if missing.
            if (_baseLayerStateHashes == null) return true;
            return _baseLayerStateHashes.Contains(Animator.StringToHash(stateName));
        }

        private void EnsureBaseLayerStateCache()
        {
            // Re-enumerate every Awake / on-demand. We only know the full set
            // in the editor; runtime builds leave the cache as null and trust
            // the caller, which is what PlayActionState is designed for.
            if (_baseLayerStateHashes != null) return;
#if UNITY_EDITOR
            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            var ac = _animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
            if (ac == null || ac.layers == null || ac.layers.Length == 0) return;
            var sm = ac.layers[0].stateMachine;
            if (sm == null || sm.states == null) return;
            var set = new HashSet<int>();
            foreach (var s in sm.states)
            {
                if (s.state != null && !string.IsNullOrEmpty(s.state.name))
                    set.Add(Animator.StringToHash(s.state.name));
            }
            if (set.Count > 0) _baseLayerStateHashes = set;
#endif
        }

        private System.Collections.IEnumerator ReturnToLocomotionAfter(float seconds, float fade)
        {
            yield return new WaitForSeconds(seconds);
            if (_animator == null) yield break;
            // Convention: the BlendTree locomotion state is named locomotionStateName.
            // Use the (typically longer) return crossfade so the action eases back
            // into locomotion instead of snapping.
            int locoHash = Animator.StringToHash(locomotionStateName);
            float returnFade = Mathf.Max(fade, actionReturnCrossfade);
            if (returnFade <= 0.001f) _animator.Play(locoHash, 0, 0f);
            else _animator.CrossFadeInFixedTime(locoHash, returnFade, 0, 0f);
            _holdRoutine = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                // keep caches in sync in Editor when parameter names change
                if (_animator == null) _animator = GetComponent<Animator>();
                if (_animator != null)
                {
                    CacheAnimatorParams();
                }
            }
        }
#endif
    }
}
