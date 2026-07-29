using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using MLA_SIM;
using MLA_SIM.Interactions;
using MLA_SIM.Dooms.Scenes;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Phase 2: T4 AnimationOnly local brain, Option A - Need-Driven Weighted Choice.
    ///
    /// No backend contact. Each tick it scores every configured activity:
    ///     score = need_deficit * need_weight
    ///           + directive_bonus (if faction directive targets/allows this)
    ///           + time_of_day_bonus
    /// Picks the highest, resolves a free TargetTransformAnchor of the
    /// configured targetClass via TargetClassRegistry, navigates there,
    /// snaps, plays the animator state, waits holdSeconds, restores the
    /// satisfied need, releases the anchor.
    ///
    /// Required components on the agent: DoomsAgentTag, DoomsAgentNeeds,
    /// NavMeshAgent, Animator + AnimatorLocomotionDriver.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DoomsAgentTag))]
    [RequireComponent(typeof(DoomsAgentNeeds))]
    [RequireComponent(typeof(AnimatorLocomotionDriver))]
    [AddComponentMenu("MLA_SIM/DOOMS/Dooms Agent T4 Brain")]
    public class DoomsAgentT4Brain : MonoBehaviour
    {
        public enum ActivityParticipantMode
        {
            Solo,
            Pair
        }

        [System.Serializable]
        public class Activity
        {
            [ActivityNameDropdown]
            public string activityName = "Idle";

            [RegistryDropdown(RegistryType.InteractionPoint)]
            public string targetClass = "";

            [RegistryDropdown(RegistryType.AnimationState)]
            public string animatorStateName = "";

            [RegistryDropdown(RegistryType.AnimationSequence)]
            public string sequenceId = "";

            [RegistryDropdown(RegistryType.Prop)]
            public string propId = "";

            [RegistryDropdown(RegistryType.Faction)]
            public string factionId = "";

            [MoodTagDropdown]
            public string hostilityTag = "";

            public ActivityParticipantMode participantMode = ActivityParticipantMode.Solo;
            public bool infectious = false;

            public float holdSeconds = 4f;
            public DoomsAgentNeeds.NeedType restoresNeed = DoomsAgentNeeds.NeedType.Duty;
            public float restoreAmount = 0.5f;
            [Tooltip("How strongly this activity's need-deficit drives its score (0..2).")]
            public float needWeight = 1.0f;
            [Tooltip("Hours 0..24 when this activity gets a daytime bonus. Leave empty for no time bias.")]
            public float timeStartHour = 0f;
            public float timeEndHour = 24f;
            [Tooltip("Score bonus applied during time window.")]
            public float timeBonus = 0f;
            [Tooltip("Action name to match against faction directive allowed_actions.")]
            public string directiveMatchAction = "";
        }

        [Header("Activities (weighted choice)")]
        public List<Activity> activities = new List<Activity>
        {
            new Activity { activityName = "Sleep",  targetClass = "Bed",        animatorStateName = "Sleep",      holdSeconds = 8f, restoresNeed = DoomsAgentNeeds.NeedType.Energy, restoreAmount = 0.8f, needWeight = 1.2f, timeStartHour = 22f, timeEndHour = 6f, timeBonus = 0.3f },
            new Activity { activityName = "Eat",    targetClass = "FoodSpot",   animatorStateName = "Eat",        holdSeconds = 4f, restoresNeed = DoomsAgentNeeds.NeedType.Hunger, restoreAmount = 0.7f, needWeight = 1.0f, timeStartHour = 7f,  timeEndHour = 19f, timeBonus = 0.15f },
            new Activity { activityName = "Talk",   targetClass = "SocialSpot", animatorStateName = "Talk",       holdSeconds = 6f, restoresNeed = DoomsAgentNeeds.NeedType.Social, restoreAmount = 0.6f, needWeight = 0.9f, timeStartHour = 16f, timeEndHour = 22f, timeBonus = 0.1f },
            new Activity { activityName = "Work",   targetClass = "WorkBench",  animatorStateName = "Work",       holdSeconds = 6f, restoresNeed = DoomsAgentNeeds.NeedType.Duty,   restoreAmount = 0.5f, needWeight = 0.8f, timeStartHour = 8f,  timeEndHour = 18f, timeBonus = 0.3f },
        };

        [Header("Activity Source")]
        [Tooltip("Optional shared activity catalog. Used when local 'activities' is empty.")]
        public ActivityCatalogSO sharedActivityCatalog;
        [Tooltip("If true and local activities are empty, auto-load activities from sharedActivityCatalog (or ActivityCatalogSO.Instance).")]
        public bool useSharedCatalogWhenLocalEmpty = true;
        [Tooltip("If true and a sharedActivityCatalog is explicitly assigned, it overrides any local default activities.")]
        public bool catalogOverridesLocal = true;

        [Header("Daily Routine")]
        [Tooltip("If true, the agent follows an authored daily schedule (InteractionPoint-anchored) as its backbone, doing procedural activities only in free time.")]
        public bool enableRoutine = true;
        [Tooltip("Optional shared routine catalog. Used when local 'routineBlocks' is empty (falls back to DoomsRoutineCatalogSO.Instance).")]
        public DoomsRoutineCatalogSO sharedRoutineCatalog;
        [Tooltip("Optional per-agent routine override. If empty, loaded from the catalog by faction at Awake.")]
        public List<RoutineBlock> routineBlocks = new List<RoutineBlock>();
        [Tooltip("Optional: specific InteractionPoints this agent owns. Matched by pointTag to a block's targetClass and preferred over nearest-free.")]
        public List<InteractionPoint> assignedRoutinePoints = new List<InteractionPoint>();

        [Header("Directive Matching")]
        [Tooltip("Bonus score added when a faction directive's target_classes or allowed_actions match an activity.")]
        public float directiveBonusBase = 0.8f;
        [Tooltip("Extra bonus multiplied by directive intensity (0..1).")]
        public float directiveBonusIntensityMul = 0.8f;

        [Header("Loop Timing")]
        [Tooltip("Min seconds between decisions (after a hold completes).")]
        public float minDecisionInterval = 2f;
        public float maxDecisionInterval = 4f;

        [Header("Time of Day")]
        [Tooltip("Sim hour source: uses Time.time scaled. 1 real-second = simHourPerSecond sim-hours.")]
        public float simHourPerSecond = 0.01f; // 1 hour per 100 real-seconds
        [Range(0f, 24f)] public float currentHour = 8f;

        [Tooltip("If enabled, this agent reads in-game time from TimeOfDayManager when one exists in the scene. If none exists, it falls back to the local clock below.")]
        public bool useSharedTimeOfDay = true;

        [Header("Bump Reaction")]
        [Tooltip("Play a brief 'bumped' animator state and reroute when the agent makes contact with another agent while moving.")]
        public bool enableBumpReaction = true;
        [Tooltip("Candidate animator state names for the bump reaction. One is chosen at random (only states that exist are used).")]
        public string[] bumpStateNames = { "Bump", "BumpLeft", "BumpRight" };
        [Tooltip("Distance (m) ahead within which another agent triggers a bump.")]
        public float bumpDetectDistance = 0.7f;
        [Tooltip("How directly the other agent must be ahead of us (dot of forward vs direction-to-other). 1=dead ahead, 0=to the side.")]
        public float bumpForwardDot = 0.35f;
        [Tooltip("Seconds the bump reaction holds before rerouting.")]
        public float bumpReactionSeconds = 0.6f;
        [Tooltip("Minimum seconds between bump reactions for this agent.")]
        public float bumpCooldownSeconds = 2.5f;
        [Tooltip("NavMeshAgent avoidancePriority used while standing/acting. LOWER = higher importance, so moving agents steer around it instead of pushing it. Must be lower than moving agents' priority.")]
        [Range(0, 99)] public int actingAvoidancePriority = 15;

        [Header("Courtesy Yield (step aside / 'after you')")]
        [Tooltip("When this agent is standing idle and another agent is walking toward it, step aside and (optionally) play a short 'after you' gesture so it doesn't block the path. One-sided: only the idle agent yields.")]
        public bool enableCourtesyYield = true;
        [Tooltip("Candidate animator state names for the yield gesture (one chosen at random; only states that exist are used). Leave empty to step aside with no gesture.")]
        public string[] yieldStateNames = { "StepAside", "AfterYou" };
        [Tooltip("Distance (m) within which an approaching mover triggers a yield.")]
        public float yieldDetectDistance = 1.8f;
        [Tooltip("How directly the other agent must be moving AT us (dot of their heading vs direction from them to us). 1=straight at us, 0=glancing.")]
        public float yieldApproachDot = 0.5f;
        [Tooltip("How far (m) to step to the side when yielding.")]
        public float yieldStepMeters = 0.9f;
        [Tooltip("Seconds to hold the yield gesture after stepping aside.")]
        public float yieldHoldSeconds = 0.8f;
        [Tooltip("Minimum seconds between courtesy yields for this agent.")]
        public float yieldCooldownSeconds = 3f;

        [Header("Social Facing")]
        [Tooltip("When a social/Talk activity runs, approach to this distance (m) from the nearest agent and face them.")]
        public float socialStandoffMeters = 1.5f;
        [Tooltip("Search radius (m) for a partner to face during social/Talk activities.")]
        public float socialSeekRadius = 6f;

        [Header("Ambient Reaction")]
        [Tooltip("Minimum seconds between ambient cinematic reactions, so agents don't re-watch every decision tick (which reads as jitter).")]
        public float ambientReactionCooldown = 4f;

        [Header("Locomotion Loadout")]
        [Tooltip("Default locomotion blendtree style applied at spawn and restored after scene/routine style overrides. E.g. a guard uses 'BTGun'. Empty = engine default (0). Must match a name in AnimatorLocomotionDriver.locoStyles (or the legacy March/Angry/Sneak). Bind a carried prop to the style via AnimatorPropDriver.locoStyleProps (e.g. BTGun -> MachineGun).")]
        public string defaultLocoStyle = "";

        [Header("Logging")]
        [Tooltip("Show detailed DOOMS activity logging")]
        public bool verboseActivityLogging = false;

        // Current high-level activity label, exposed for telemetry/debug.
        public string CurrentActivityName { get; private set; } = "Idle";

        // True while performing a scheduled routine task — encounter staging skips
        // these agents so they don't abandon work/sleep for random chatter.
        public bool IsBusyWithRoutine { get; private set; }

        private DoomsAgentTag _tag;
        private DoomsAgentNeeds _needs;
        private DoomsAgentPersona _persona;
        private NavMeshAgent _nav;
        private AnimatorLocomotionDriver _anim;
        private AnimatorPropDriver _propDriver;
        private TargetTransformAnchor _heldAnchor;
        private InteractionPoint _heldPoint;
        private AreaAnchor.PointOfInterest _heldAreaPoint;

        private void Awake()
        {
            _tag = GetComponent<DoomsAgentTag>();
            _needs = GetComponent<DoomsAgentNeeds>();
            _persona = GetComponent<DoomsAgentPersona>();
            _nav = GetComponent<NavMeshAgent>();
            _anim = GetComponent<AnimatorLocomotionDriver>();

            // Crowd avoidance: equal avoidance priorities are the #1 cause of
            // agents shoving / deadlocking into each other. Use high-quality
            // avoidance and randomize priority per agent so they yield to one
            // another instead of bumping head-on.
            if (_nav != null)
            {
                _nav.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                // Movers sit in a mid band; randomized so equal priorities don't deadlock.
                // Standing/acting agents drop to actingAvoidancePriority (higher importance)
                // so movers steer around them instead of shoving them along.
                _moveAvoidancePriority = Random.Range(45, 61);
                _nav.avoidancePriority = _moveAvoidancePriority;
            }
            _propDriver = GetComponent<AnimatorPropDriver>();
            if (_propDriver == null)
                _propDriver = GetComponentInChildren<AnimatorPropDriver>(true);
            if (_propDriver == null)
                _propDriver = GetComponentInParent<AnimatorPropDriver>();

            if (_propDriver == null && _anim != null)
            {
                _propDriver = gameObject.AddComponent<AnimatorPropDriver>();
                Debug.LogWarning($"[DOOMS][T4Brain] '{gameObject.name}' did not have an AnimatorPropDriver. Added one at runtime so interaction props can spawn.");
            }

            BootstrapActivitiesFromCatalog();
            BootstrapRoutineFromCatalog();

            // Apply the per-agent loadout locomotion style (e.g. armed guard gait).
            if (_anim != null && !string.IsNullOrEmpty(defaultLocoStyle))
            {
                _anim.SetLocoStyle(defaultLocoStyle);
            }

            Debug.Log($"[DOOMS][T4Brain] Awake on '{gameObject.name}'. AnimatorDriver={_anim != null}, PropDriver={_propDriver != null}");
        }

        private void LogActivity(string message)
        {
            if (verboseActivityLogging)
            {
                Debug.Log(message);
            }
        }

        private void EnsureLocomotionPlayback()
        {
            if (_anim != null)
            {
                _anim.StopActionPlayback(true);
            }
        }

        private float _lastNavWarnTime = -999f;

        private bool TryBeginNavMove()
        {
            if (_nav == null || !_nav.enabled)
            {
                WarnNav("no enabled NavMeshAgent");
                return false;
            }

            if (!_nav.isOnNavMesh)
            {
                if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    _nav.Warp(hit.position);
                }

                if (!_nav.isOnNavMesh)
                {
                    WarnNav("agent is off NavMesh and recovery failed");
                    return false;
                }
            }

            EnsureLocomotionPlayback();
            // Back to a "mover" priority so we yield normally to standing agents.
            _nav.avoidancePriority = _moveAvoidancePriority;
            _nav.isStopped = false;
            return true;
        }

        private void WarnNav(string reason)
        {
            if (Time.time - _lastNavWarnTime < 10f) return;
            _lastNavWarnTime = Time.time;
            Debug.LogWarning($"[DOOMS][T4Brain] '{gameObject.name}' cannot move: {reason}.");
        }

        // ─────────────────── Navigation / arrival / bump ───────────────────

        private enum NavOutcome { Arrived, TimedOut, Aborted }
        private NavOutcome _lastNavOutcome = NavOutcome.TimedOut;
        private float _nextBumpAllowed = -999f;
        private float _nextAmbientReactionAllowed = -999f;
        private float _nextYieldAllowed = -999f;
        private Coroutine _courtesyCo;
        private int _moveAvoidancePriority = 50;

        /// <summary>
        /// Robustly drives the NavMeshAgent to <paramref name="dest"/> and watches for
        /// bumps along the way. Sets <see cref="_lastNavOutcome"/> so the caller can
        /// react. The agent is NOT stopped here — callers call <see cref="StopNavForAction"/>
        /// before playing an action so the body never slides while a static clip plays.
        /// </summary>
        private IEnumerator NavigateAndWatch(Vector3 dest, float timeout, float arriveDist, string agentId, System.Func<bool> abort)
        {
            _lastNavOutcome = NavOutcome.TimedOut;

            if (!TryBeginNavMove())
            {
                yield break;
            }
            _nav.SetDestination(dest);

            float t = 0f;
            while (t < timeout)
            {
                if (abort != null && abort())
                {
                    _lastNavOutcome = NavOutcome.Aborted;
                    yield break;
                }

                if (HasArrived(arriveDist))
                {
                    _lastNavOutcome = NavOutcome.Arrived;
                    yield break;
                }

                var other = DetectImminentBump();
                if (other != null)
                {
                    yield return StartCoroutine(BumpReactAndReroute(other, dest, agentId));
                }

                yield return new WaitForSeconds(0.2f);
                t += 0.2f;
            }
        }

        // Hardened arrival test. The old check trusted remainingDistance even before
        // a path existed (it reads a stale 0 then), causing instant false arrivals.
        private bool HasArrived(float arriveDist)
        {
            if (_nav == null || !_nav.isOnNavMesh) return false;
            if (_nav.pathPending) return false;
            if (!_nav.hasPath) return false;
            if (_nav.pathStatus == NavMeshPathStatus.PathInvalid) return true; // give up where we are
            return _nav.remainingDistance <= arriveDist;
        }

        // Clean up after an activity finishes normally. The animator hold already
        // auto-returns to locomotion, so this only needs to detach the interaction
        // prop — otherwise a held prop stays bound to the bone after completion.
        private void FinishActivityCleanup(string occId)
        {
            if (_propDriver != null)
                _propDriver.ClearInteractionPropOverride();
        }

        // Restore the agent's loadout locomotion style after a transient override
        // (scene blendtree directive, flee gait). Falls back to engine default (0).
        private void RestoreLocoStyle()
        {
            if (_anim == null) return;
            if (!string.IsNullOrEmpty(defaultLocoStyle))
                _anim.SetLocoStyle(defaultLocoStyle);
            else
                _anim.SetLocoStyle(0);
        }

        // Stop and settle the agent so a static action clip never plays while sliding.
        // TryBeginNavMove() flips isStopped=false again on the next move.
        private void StopNavForAction()
        {
            if (_nav != null && _nav.enabled && _nav.isOnNavMesh)
            {
                _nav.isStopped = true;
                _nav.velocity = Vector3.zero;
                _nav.ResetPath();
                // Become a high-importance obstacle so moving agents route around us
                // instead of shoving us along while we stand and play an action.
                _nav.avoidancePriority = Mathf.Clamp(actingAvoidancePriority, 0, 99);
            }
        }

        private DoomsAgentTag DetectImminentBump()
        {
            if (!enableBumpReaction) return null;
            if (Time.time < _nextBumpAllowed) return null;
            if (_nav == null || _nav.velocity.sqrMagnitude < 0.04f) return null; // only while actually moving

            Vector3 fwd = _nav.velocity; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return null;
            fwd.Normalize();

            // Scale detection by both agents' radii so it fires at (not past) contact.
            float radius = Mathf.Max(0.3f, bumpDetectDistance) + _nav.radius;
            var hits = Physics.OverlapSphere(transform.position, radius);

            DoomsAgentTag best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i];
                if (c == null) continue;
                // Collider is typically on a child mesh; the tag lives on the root.
                var ot = c.GetComponentInParent<DoomsAgentTag>();
                if (ot == null || ot == _tag || ot.gameObject == gameObject) continue;

                Vector3 to = ot.transform.position - transform.position; to.y = 0f;
                float d = to.magnitude;
                if (d < 0.01f) continue;
                if (Vector3.Dot(fwd, to / d) < bumpForwardDot) continue; // must be ahead of us

                if (d < bestDist) { bestDist = d; best = ot; }
            }
            return best;
        }

        private IEnumerator BumpReactAndReroute(DoomsAgentTag other, Vector3 dest, string agentId)
        {
            _nextBumpAllowed = Time.time + Mathf.Max(0.5f, bumpCooldownSeconds);

            if (other != null) FaceTowards(other.transform.position);
            StopNavForAction();

            if (_anim != null)
            {
                _anim.PlayBumpReaction(bumpStateNames, Mathf.Max(0.1f, bumpReactionSeconds));
            }

            float t = 0f;
            float wait = Mathf.Max(0.1f, bumpReactionSeconds);
            while (t < wait)
            {
                yield return new WaitForSeconds(0.1f);
                t += 0.1f;
            }

            // Re-path: the other agent has had time to pass, and avoidance will
            // now route us around the cleared space.
            if (TryBeginNavMove())
            {
                _nav.SetDestination(dest);
            }
        }

        private static bool IsSocialActivity(Activity a)
        {
            if (a == null) return false;
            if (a.restoresNeed == DoomsAgentNeeds.NeedType.Social) return true;
            string n = a.activityName;
            return !string.IsNullOrEmpty(n)
                && (n.IndexOf("talk", System.StringComparison.OrdinalIgnoreCase) >= 0
                 || n.IndexOf("social", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // Solo social/Talk: walk to ~socialStandoffMeters from the nearest agent and
        // face them, instead of playing Talk at a fixed anchor with nobody there.
        private IEnumerator ApproachAndFacePartner(string agentId, System.Func<bool> abort)
        {
            DoomsAgentTag partner = null;
            float bestDist = float.MaxValue;
            var hits = Physics.OverlapSphere(transform.position, Mathf.Max(0.5f, socialSeekRadius));
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i];
                if (c == null) continue;
                // Collider is typically on a child mesh; the tag lives on the root.
                var ot = c.GetComponentInParent<DoomsAgentTag>();
                if (ot == null || ot == _tag || ot.gameObject == gameObject) continue;
                float d = Vector3.Distance(transform.position, ot.transform.position);
                if (d < bestDist) { bestDist = d; partner = ot; }
            }

            if (partner == null) yield break;

            Vector3 toMe = transform.position - partner.transform.position; toMe.y = 0f;
            if (toMe.sqrMagnitude < 0.0001f) toMe = transform.forward;
            toMe = toMe.sqrMagnitude > 0.0001f ? toMe.normalized : Vector3.forward;

            Vector3 standoff = partner.transform.position + toMe * Mathf.Max(0.5f, socialStandoffMeters);
            if (UnityEngine.AI.NavMesh.SamplePosition(standoff, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
                standoff = hit.position;

            yield return StartCoroutine(NavigateAndWatch(standoff, 12f, Mathf.Max(0.4f, socialStandoffMeters * 0.5f), agentId, abort));
            if (_lastNavOutcome == NavOutcome.Aborted) yield break;

            StopNavForAction();
            if (partner != null) FaceTowards(partner.transform.position);
        }

        // Scene-reserved agent with no active directive: instead of standing frozen,
        // micro-roam near its post to stay alive-looking. Stays reserved the whole
        // time; ShouldYieldToHigherPriorityWork aborts the instant SceneDirector
        // issues the next directive, so the role resumes immediately.
        private IEnumerator ExecuteReservedIdle(string agentId)
        {
            Vector3 offset = Random.insideUnitSphere; offset.y = 0f;
            if (offset.sqrMagnitude < 0.0001f) offset = transform.forward;
            float radius = Mathf.Max(1f, socialSeekRadius * 0.5f);
            Vector3 target = transform.position + offset.normalized * Random.Range(0.5f, radius);
            if (UnityEngine.AI.NavMesh.SamplePosition(target, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
                target = hit.position;

            yield return StartCoroutine(NavigateAndWatch(target, 8f, Mathf.Max(0.5f, _nav.stoppingDistance), agentId, () => ShouldYieldToHigherPriorityWork(agentId)));
            if (_lastNavOutcome == NavOutcome.Aborted) yield break;

            StopNavForAction();

            float dwell = Random.Range(minDecisionInterval, maxDecisionInterval);
            float t = 0f;
            while (t < dwell)
            {
                if (ShouldYieldToHigherPriorityWork(agentId)) yield break;
                yield return new WaitForSeconds(0.2f);
                t += 0.2f;
            }
        }

        private string EffectiveFaction()
        {
            return DoomsFactionRuntime.EffectiveFactionOf(_tag);
        }

        private float PersonaSociability()
        {
            return _persona != null ? _persona.sociability : 0.5f;
        }

        private void NudgeFaction(string factionId, float delta)
        {
            if (_persona == null || string.IsNullOrEmpty(factionId) || Mathf.Approximately(delta, 0f)) return;
            _persona.Nudge(factionId, delta);
        }

        private void NudgeSceneFactions(float delta)
        {
            if (_persona == null || Mathf.Approximately(delta, 0f)) return;

            var factions = AmbientMoodBoard.SceneFactions;
            for (int i = 0; i < factions.Length; i++)
            {
                var f = factions[i];
                if (string.IsNullOrEmpty(f)) continue;
                _persona.Nudge(f, delta);
            }
        }

        private void BootstrapActivitiesFromCatalog()
        {
            if (!useSharedCatalogWhenLocalEmpty) return;
            bool localAuthored  = activities != null && activities.Count > 0;
            bool explicitCatalog = sharedActivityCatalog != null;
            if (localAuthored && !(explicitCatalog && catalogOverridesLocal)) return;

            var catalog = sharedActivityCatalog != null ? sharedActivityCatalog : ActivityCatalogSO.Instance;
            if (catalog == null) return;

            string factionId = EffectiveFaction();
            var resolved = catalog.Resolve(factionId);
            if (resolved == null || resolved.Count == 0) return;

            activities = resolved;
            LogActivity($"[DOOMS] T4 Agent '{(_tag != null ? _tag.agentId : gameObject.name)}' loaded {activities.Count} activities from catalog '{catalog.name}' (faction={factionId}).");
        }

        private void BootstrapRoutineFromCatalog()
        {
            if (!enableRoutine) return;
            if (routineBlocks != null && routineBlocks.Count > 0) return; // per-agent override authored

            var catalog = sharedRoutineCatalog != null ? sharedRoutineCatalog : DoomsRoutineCatalogSO.Instance;
            if (catalog == null) return;

            var resolved = catalog.Resolve(EffectiveFaction());
            if (resolved == null || resolved.Count == 0) return;
            routineBlocks = resolved;
        }

        private RoutineBlock GetActiveRoutineBlock(float hour)
        {
            if (routineBlocks == null) return null;
            for (int i = 0; i < routineBlocks.Count; i++)
            {
                var b = routineBlocks[i];
                if (b == null) continue;
                if (DoomsRoutineCatalogSO.InWindow(hour, b.startHour, b.endHour)) return b;
            }
            return null;
        }

        // An agent's own assigned InteractionPoint for this class, if set and free.
        private InteractionPoint FindAssignedRoutinePoint(string targetClass)
        {
            if (assignedRoutinePoints == null || string.IsNullOrEmpty(targetClass)) return null;
            for (int i = 0; i < assignedRoutinePoints.Count; i++)
            {
                var p = assignedRoutinePoints[i];
                if (p == null) continue;
                if (string.Equals(p.pointTag, targetClass, System.StringComparison.OrdinalIgnoreCase) && p.HasFreeSlot)
                    return p;
            }
            return null;
        }

        // Perform a scheduled block by reusing the normal activity execution path
        // (navigate -> occupy InteractionPoint -> play -> hold -> restore -> release).
        private IEnumerator ExecuteRoutineBlock(RoutineBlock block)
        {
            var a = new Activity
            {
                activityName = "Routine_" + block.label,
                targetClass = block.targetClass,
                holdSeconds = Mathf.Max(1f, block.holdSeconds),
                restoresNeed = block.restoresNeed,
                restoreAmount = Mathf.Clamp01(block.restoreAmount)
            };

            InteractionPoint preferred = FindAssignedRoutinePoint(block.targetClass);

            // Apply the block's locomotion style (e.g. 'Drunk' during Bar) for the duration
            // of the routine, then restore the agent's default. Routine only runs when the
            // agent is unreserved, so this never fights a SceneSO directive's loco style.
            bool styleApplied = false;
            if (_anim != null && !string.IsNullOrEmpty(block.locoStyle))
            {
                _anim.SetLocoStyle(block.locoStyle);
                styleApplied = true;
            }

            IsBusyWithRoutine = true;
            yield return StartCoroutine(ExecuteActivity(a, preferred));
            IsBusyWithRoutine = false;

            if (styleApplied) RestoreLocoStyle();
        }

        private void OnEnable()
        {
            string agentId = _tag != null ? _tag.agentId : gameObject.name;
            LogActivity($"[DOOMS] T4 Agent '{agentId}' Brain enabled.");
            StartCoroutine(BrainLoop());
        }

        private void Update()
        {
            if (ResolveCurrentHourSource() == null)
            {
                currentHour = (currentHour + simHourPerSecond * Time.deltaTime) % 24f;
            }

            TickCourtesyYield();
        }

        // While standing still (parked/acting — never during active navigation), if
        // another agent is walking toward us, step aside and play a short "after you"
        // gesture, then return to our spot. One-sided: only the idle agent yields.
        private void TickCourtesyYield()
        {
            if (!enableCourtesyYield || _courtesyCo != null) return;
            if (Time.time < _nextYieldAllowed) return;
            if (_nav == null || !_nav.enabled || !_nav.isOnNavMesh) return;
            // isStopped==true means we are parked for an action; active nav sets it false.
            if (!_nav.isStopped) return;
            // Duty-bound: if we're performing an InteractionPoint / anchor / POI action,
            // ignore bypassers and hold our post (never abandon the action to step aside).
            if (_heldPoint != null || _heldAnchor != null || _heldAreaPoint != null) return;

            string agentId = _tag != null && !string.IsNullOrEmpty(_tag.agentId) ? _tag.agentId : gameObject.name;
            if (T4EncounterResolver.IsAgentLocked(agentId)) return;

            var mover = DetectApproachingMover();
            if (mover != null)
                _courtesyCo = StartCoroutine(CourtesyStepAside(mover));
        }

        // Finds the nearest agent that is moving roughly toward us within yieldDetectDistance.
        private DoomsAgentTag DetectApproachingMover()
        {
            float radius = Mathf.Max(0.3f, yieldDetectDistance) + (_nav != null ? _nav.radius : 0.3f);
            var hits = Physics.OverlapSphere(transform.position, radius);

            DoomsAgentTag best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i];
                if (c == null) continue;
                var ot = c.GetComponentInParent<DoomsAgentTag>();
                if (ot == null || ot == _tag || ot.gameObject == gameObject) continue;

                // The other agent must actually be moving (use its nav velocity).
                var onav = ot.GetComponentInParent<NavMeshAgent>();
                Vector3 ovel = onav != null ? onav.velocity : Vector3.zero;
                ovel.y = 0f;
                if (ovel.sqrMagnitude < 0.09f) continue; // ~0.3 m/s threshold

                Vector3 fromOtherToUs = transform.position - ot.transform.position; fromOtherToUs.y = 0f;
                float d = fromOtherToUs.magnitude;
                if (d < 0.01f) continue;
                // Heading roughly AT us?
                if (Vector3.Dot(ovel.normalized, fromOtherToUs / d) < yieldApproachDot) continue;

                if (d < bestDist) { bestDist = d; best = ot; }
            }
            return best;
        }

        private IEnumerator CourtesyStepAside(DoomsAgentTag mover)
        {
            _nextYieldAllowed = Time.time + Mathf.Max(0.5f, yieldCooldownSeconds);

            Vector3 returnPos = transform.position;
            Quaternion returnRot = transform.rotation;

            // Step perpendicular to the mover's travel, to the side that takes us off its path.
            var onav = mover != null ? mover.GetComponentInParent<NavMeshAgent>() : null;
            Vector3 moverFwd = onav != null ? onav.velocity : (mover != null ? mover.transform.forward : transform.forward);
            moverFwd.y = 0f;
            if (moverFwd.sqrMagnitude < 0.0001f) moverFwd = transform.forward;
            moverFwd.Normalize();

            Vector3 side = Vector3.Cross(Vector3.up, moverFwd); // unit, perpendicular
            // Pick the side that currently has us on it (push further off the line).
            Vector3 fromMover = transform.position - (mover != null ? mover.transform.position : transform.position);
            if (Vector3.Dot(side, fromMover) < 0f) side = -side;

            Vector3 stepTarget = transform.position + side * Mathf.Max(0.3f, yieldStepMeters);
            if (NavMesh.SamplePosition(stepTarget, out var hit, 1.5f, NavMesh.AllAreas))
                stepTarget = hit.position;

            // Face the mover (acknowledging) and play the yield gesture if one exists.
            FaceTowards(mover != null ? mover.transform.position : stepTarget);
            if (_anim != null && yieldStateNames != null && yieldStateNames.Length > 0)
                _anim.PlayBumpReaction(yieldStateNames, Mathf.Max(0.1f, yieldHoldSeconds));

            // Move aside.
            if (TryBeginNavMove())
            {
                _nav.SetDestination(stepTarget);
                float t = 0f;
                while (t < 1.5f)
                {
                    Vector3 to = stepTarget - transform.position; to.y = 0f;
                    if (to.sqrMagnitude <= 0.16f) break; // within ~0.4 m
                    yield return null;
                    t += Time.deltaTime;
                }
            }

            // Hold the gesture briefly while they pass.
            StopNavForAction();
            yield return new WaitForSeconds(Mathf.Max(0.1f, yieldHoldSeconds));

            // Return to our original spot so idle agents don't drift / point-holders stay put.
            if (TryBeginNavMove())
            {
                _nav.SetDestination(returnPos);
                float t = 0f;
                while (t < 2f)
                {
                    Vector3 to = returnPos - transform.position; to.y = 0f;
                    if (to.sqrMagnitude <= 0.16f) break;
                    yield return null;
                    t += Time.deltaTime;
                }
            }
            StopNavForAction();
            transform.rotation = returnRot;

            _courtesyCo = null;
        }

        private float? ResolveCurrentHourSource()
        {
            if (!useSharedTimeOfDay)
                return null;

            var manager = TimeOfDayManager.Instance;
            if (manager == null)
                return null;

            return manager.CurrentHour;
        }

        private float GetEffectiveCurrentHour()
        {
            return ResolveCurrentHourSource() ?? currentHour;
        }

        private IEnumerator BrainLoop()
        {
            // Stagger startup so many T4s don't all pick at once
            yield return new WaitForSeconds(Random.Range(0f, maxDecisionInterval));

            while (true)
            {
                string agentId = _tag != null && !string.IsNullOrEmpty(_tag.agentId)
                    ? _tag.agentId
                    : gameObject.name;

                if (T4EncounterResolver.IsAgentLocked(agentId))
                {
                    CurrentActivityName = "Encounter";
                    yield return null;
                    continue;
                }

                // Check for Scene-Specific Agent Directives first
                var agentDir = FactionDirectiveBoard.GetForAgent(agentId);
                if (agentDir != null && !agentDir.IsExpired)
                {
                    DoomsDebug.Log(DoomsDebug.Category.SceneDirector,
                        $"'{agentId}' executing directive kind={agentDir.directiveKind} scene={agentDir.sceneId}.");
                    _activeDirective = agentDir;
                    CurrentActivityName = "Directive:" + agentDir.directiveKind;
                    if (string.Equals(agentDir.directiveKind, "Area", System.StringComparison.OrdinalIgnoreCase))
                    {
                        AreaAnchor reservedArea = null;
                        if (SceneDirector.Instance != null && SceneDirector.Instance.CurrentContext != null)
                        {
                            SceneDirector.Instance.CurrentContext.reservedAreas.TryGetValue(agentId, out reservedArea);
                        }
                        yield return StartCoroutine(ExecuteAreaRole(agentDir, reservedArea));
                    }
                    else if (string.Equals(agentDir.directiveKind, "Timeline", System.StringComparison.OrdinalIgnoreCase))
                    {
                        TimelineAnchor reservedTimeline = null;
                        if (SceneDirector.Instance != null && SceneDirector.Instance.CurrentContext != null)
                        {
                            SceneDirector.Instance.CurrentContext.reservedTimelines.TryGetValue(agentId, out reservedTimeline);
                        }
                        yield return StartCoroutine(ExecuteTimelineRole(agentDir, reservedTimeline));
                    }
                    else // Point
                    {
                        LogActivity($"[DOOMS] T4 Agent '{agentId}' executing Scene Role Directive: scene={agentDir.sceneId}, phase={agentDir.phaseId}, pointTag={agentDir.pointTag}, animation={agentDir.animationState}");
                        var sceneActivity = new Activity
                        {
                            activityName = "SceneRole_" + agentDir.phaseId,
                            targetClass = agentDir.pointTag,
                            animatorStateName = agentDir.animationState,
                            holdSeconds = 1.5f, // brief tick interval while executing role
                            restoreAmount = 0f
                        };
                        yield return StartCoroutine(ExecuteActivity(sceneActivity));
                    }
                    _activeDirective = null;
                    continue;
                }

                // If this agent is scene-reserved but currently has no per-agent directive,
                // do not let personal needs hijack behavior. Wait for SceneDirector updates.
                if (_tag != null && !string.IsNullOrEmpty(_tag.reservedBySceneId))
                {
                    DoomsDebug.Log(DoomsDebug.Category.SceneDirector,
                        $"'{agentId}' reserved by scene '{_tag.reservedBySceneId}' with no active directive; micro-roaming until reassigned.");
                    CurrentActivityName = "ReservedIdle";
                    yield return StartCoroutine(ExecuteReservedIdle(agentId));
                    continue;
                }

                // Ambient cinematic reaction (Option C): an unassigned agent standing
                // inside an active scene's influence reacts by faction relation instead
                // of pursuing personal needs, keeping the scene coherent.
                // Guard: only enter reaction for actionable relations. Neutral agents
                // under a Calm scene-wide mood fall through to procedural layers.
                if (AmbientMoodBoard.HasAnyInfluenceAt(transform.position)
                    && (_tag == null || string.IsNullOrEmpty(_tag.reservedBySceneId))
                    && AmbientMoodBoard.InfluenceFactor(transform.position) > 0f
                    && Time.time >= _nextAmbientReactionAllowed)
                {
                    Relation ambRel = AmbientMoodBoard.WorstRelationForAt(EffectiveFaction(), transform.position);
                    if (ambRel != Relation.Neutral)
                    {
                        DoomsDebug.Log(DoomsDebug.Category.Ambient,
                            $"'{agentId}' ambient reaction (rel={ambRel} mood={AmbientMoodBoard.Mood}).");
                        CurrentActivityName = "AmbientReaction:" + ambRel;
                        yield return StartCoroutine(ExecuteAmbientReaction());
                        // Don't re-watch every tick — that micro-repositioning reads as jitter.
                        _nextAmbientReactionAllowed = Time.time + Mathf.Max(0.5f, ambientReactionCooldown);
                        continue;
                    }
                    DoomsDebug.Log(DoomsDebug.Category.Ambient,
                        $"'{agentId}' inside ambient influence but Neutral — falling through to procedural.");
                }

                // Daily routine backbone: anchored InteractionPoint tasks by time of
                // day. A scheduled block keeps the agent purposeful; free/leisure
                // blocks (or the per-block socialSlack) fall through to procedural.
                if (enableRoutine && (_tag == null || string.IsNullOrEmpty(_tag.reservedBySceneId)))
                {
                    RoutineBlock block = GetActiveRoutineBlock(GetEffectiveCurrentHour());
                    if (block != null && !string.IsNullOrEmpty(block.targetClass)
                        && Random.value >= Mathf.Clamp01(block.socialSlack))
                    {
                        DoomsDebug.Log(DoomsDebug.Category.ActivitySelect,
                            $"'{agentId}' routine block '{block.label}' -> targetClass '{block.targetClass}'.");
                        CurrentActivityName = "Routine:" + block.label;
                        yield return StartCoroutine(ExecuteRoutineBlock(block));
                        continue;
                    }
                    // free block, or socialSlack hit -> fall through to procedural filler
                }

                // Extras Director goal (Option D): roam to and act inside the most
                // interesting AreaAnchor the director assigned us, choosing the
                // interaction by current T4 needs.
                if (ExtrasDirector.TryGetAreaGoal(agentId, out AreaAnchor goalArea)
                    && goalArea != null
                    && (_tag == null || string.IsNullOrEmpty(_tag.reservedBySceneId)))
                {
                    DoomsDebug.Log(DoomsDebug.Category.ActivitySelect,
                        $"'{agentId}' extras goal -> area '{goalArea.GetPrimaryTag(ActiveSceneId())}' ({goalArea.gameObject.name}).");
                    CurrentActivityName = "ExtrasGoal:" + goalArea.GetPrimaryTag(ActiveSceneId());
                    yield return StartCoroutine(ExecuteExtrasGoal(goalArea));
                    continue;
                }

                Activity chosen = PickBestActivity();
                if (chosen != null)
                {
                    DoomsDebug.Log(DoomsDebug.Category.ActivitySelect,
                        $"'{agentId}' personal activity '{chosen.activityName}' (targetClass={chosen.targetClass}).");
                    LogActivity($"[DOOMS] T4 Agent '{agentId}' selected activity: targetClass={chosen.targetClass}, restoresNeed={chosen.restoresNeed}, animatorState={chosen.animatorStateName}");
                    CurrentActivityName = string.IsNullOrEmpty(chosen.activityName) ? "Activity" : chosen.activityName;
                    yield return StartCoroutine(ExecuteActivity(chosen));
                }
                else
                {
                    DoomsDebug.Log(DoomsDebug.Category.ActivitySelect,
                        $"'{agentId}' no valid activity — idling {minDecisionInterval:F1}s.");
                    Debug.LogWarning($"[DOOMS] T4 Agent '{agentId}' could not pick any valid activity.");
                    CurrentActivityName = "Idle";
                    yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
                }
            }
        }

        private Activity PickBestActivity()
        {
            string agentId = _tag != null ? _tag.agentId : gameObject.name;
            if (activities == null || activities.Count == 0)
            {
                Debug.LogWarning($"[DOOMS] T4 Agent '{agentId}' has no configured activities.");
                return null;
            }
            var directive = FactionDirectiveBoard.Get(EffectiveFaction());
            if (directive != null)
            {
                LogActivity($"[DOOMS] T4 Agent '{agentId}' found active Faction Directive: allowedActions={string.Join(",", directive.allowedActions)}, intensity={directive.intensity}");
            }

            Activity best = null;
            float bestScore = float.MinValue;
            string factionId = EffectiveFaction();

            // Pass 1: directive-matching activities only (authoritative)
            if (directive != null)
            {
                for (int i = 0; i < activities.Count; i++)
                {
                    var a = activities[i];
                    if (a == null || a.participantMode == ActivityParticipantMode.Pair) continue;
                    if (!IsFactionCompatible(a, factionId)) continue;
                    bool match = (!string.IsNullOrEmpty(a.targetClass)          && directive.ContainsTargetClass(a.targetClass))
                              || (!string.IsNullOrEmpty(a.directiveMatchAction) && directive.ContainsAction(a.directiveMatchAction));
                    if (!match) continue;
                    float score = ScoreActivity(a, directive);
                    if (score > bestScore) { bestScore = score; best = a; }
                }
            }

            // Pass 2: needs-based fallback (no active directive, or nothing matched it)
            if (best == null)
            {
                bestScore = float.MinValue;
                for (int i = 0; i < activities.Count; i++)
                {
                    var a = activities[i];
                    if (a == null || a.participantMode == ActivityParticipantMode.Pair) continue;
                    if (!IsFactionCompatible(a, factionId)) continue;
                    float score = ScoreActivity(a, directive);
                    if (score > bestScore) { bestScore = score; best = a; }
                }
            }
            return best;
        }

        private float ScoreActivity(Activity a, FactionDirectiveBoard.Directive directive)
        {
            string agentId = _tag != null ? _tag.agentId : gameObject.name;
            float score = 0f;

            // Need deficit contribution
            float deficit = _needs != null ? _needs.GetDeficit(a.restoresNeed) : 0f;
            score += deficit * Mathf.Max(0f, a.needWeight);

            if (a.restoresNeed == DoomsAgentNeeds.NeedType.Social)
            {
                score *= Mathf.Lerp(0.75f, 1.35f, PersonaSociability());
            }

            // Critical need spike
            if (_needs != null && _needs.IsCritical(a.restoresNeed))
            {
                score += 1.5f;
                LogActivity($"[DOOMS] T4 Agent '{agentId}' activity '{a.animatorStateName}' scored critical need spike (+1.5) for {a.restoresNeed}");
            }

            // Directive match
            if (directive != null)
            {
                bool tcMatch = !string.IsNullOrEmpty(a.targetClass) && directive.ContainsTargetClass(a.targetClass);
                bool acMatch = !string.IsNullOrEmpty(a.directiveMatchAction) && directive.ContainsAction(a.directiveMatchAction);
                if (tcMatch || acMatch)
                {
                    float bonus = directiveBonusBase + directiveBonusIntensityMul * Mathf.Clamp01(directive.intensity);
                    score += bonus;
                    LogActivity($"[DOOMS] T4 Agent '{agentId}' activity '{a.animatorStateName}' matched active directive! Applied directive bonus (+{bonus:F2})");
                }
            }

            // Time of day bonus
            if (a.timeBonus > 0f && IsInTimeWindow(GetEffectiveCurrentHour(), a.timeStartHour, a.timeEndHour))
                score += a.timeBonus;

            // Global ambient mood dial (Option B): scale this activity by the active
            // scene's mood so unassigned agents stop loitering / socialising during
            // tense or hostile scenes without any per-point authoring.
            if (AmbientMoodBoard.HasAnyInfluenceAt(transform.position))
            {
                var moodProfile = AmbientMoodProfileSO.Instance;
                if (moodProfile != null)
                    score *= moodProfile.GetActivityMultiplier(AmbientMoodBoard.MoodAt(transform.position), a.activityName);
            }

            // Infectious activities get a small local-join bonus while an ambient
            // influence is active nearby (dance circles, crowd cascades).
            if (a.infectious && AmbientMoodBoard.HasAnyInfluenceAt(transform.position))
            {
                var extras = ExtrasProfileSO.Instance;
                float joinChance = extras != null ? Mathf.Clamp01(extras.infectiousJoinChance) : 0f;
                int cap = extras != null ? Mathf.Max(1, extras.maxInfectiousParticipants) : 3;
                int nearby = CountNearbyExtras(5f);

                float infectiousBonus = Mathf.Lerp(0.15f, 0.75f, joinChance);
                if (nearby > cap)
                    infectiousBonus *= 0.4f;

                score += infectiousBonus;
            }

            // Small random jitter to break ties and de-sync agents
            score += Random.Range(0f, 0.05f);
            return score;
        }

        private static bool IsInTimeWindow(float h, float start, float end)
        {
            if (Mathf.Approximately(start, end)) return true;
            if (start < end) return h >= start && h < end;
            // Wraparound (e.g. 22..6)
            return h >= start || h < end;
        }

        private int CountNearbyExtras(float radius)
        {
            int count = 0;
            var hits = Physics.OverlapSphere(transform.position, Mathf.Max(0.1f, radius));
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i];
                if (c == null) continue;
                var t = c.GetComponentInParent<DoomsAgentTag>();
                if (t == null || t == _tag || t.gameObject == gameObject) continue;
                if (t.tierEnum != DoomsAgentTag.DoomsTier.AnimationOnly
                    && t.tier != (int)DoomsAgentTag.DoomsTier.AnimationOnly) continue;
                count++;
            }
            return count;
        }

        private static string ResolveActivityPlaybackId(Activity a)
        {
            if (a == null) return "";
            if (!string.IsNullOrEmpty(a.sequenceId)) return a.sequenceId;
            if (!string.IsNullOrEmpty(a.animatorStateName)) return a.animatorStateName;
            return a.activityName;
        }

        private void ApplyActivityPropOverride(Activity a)
        {
            if (a == null || string.IsNullOrEmpty(a.propId)) return;

            if (_propDriver != null)
            {
                _propDriver.SetInteractionPropOverride(a.propId);
            }
            else
            {
                Debug.LogWarning($"[DOOMS][T4Brain] Activity '{a.activityName}' wants prop '{a.propId}' but no AnimatorPropDriver exists on '{gameObject.name}'.");
            }
        }

        private static bool MatchesAnyTag(Activity a, IList<string> areaTags)
        {
            if (a == null || string.IsNullOrEmpty(a.targetClass) || areaTags == null || areaTags.Count == 0)
                return false;

            for (int i = 0; i < areaTags.Count; i++)
            {
                string tag = areaTags[i];
                if (string.IsNullOrEmpty(tag)) continue;
                if (string.Equals(a.targetClass, tag, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsFactionCompatible(Activity a, string factionId)
        {
            if (a == null) return false;
            if (string.IsNullOrEmpty(a.factionId)) return true;
            if (string.IsNullOrEmpty(factionId)) return false;
            return string.Equals(a.factionId, factionId, System.StringComparison.OrdinalIgnoreCase);
        }

        private static string ActiveSceneId()
        {
            if (SceneDirector.Instance == null || SceneDirector.Instance.CurrentContext == null || SceneDirector.Instance.CurrentContext.scene == null)
                return "";
            return SceneDirector.Instance.CurrentContext.scene.sceneId;
        }

        private static bool AreaMatchesDirective(AreaAnchor area, FactionDirectiveBoard.Directive directive)
        {
            if (area == null || directive == null) return false;

            var tags = area.GetTagsForScene(ActiveSceneId());
            for (int i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (string.IsNullOrEmpty(tag)) continue;
                if (directive.ContainsTargetClass(tag)) return true;
            }
            return false;
        }

        private void ApplyActivityAmbientInfluence(Activity a, Vector3 origin, float holdSeconds)
        {
            if (a == null || string.IsNullOrEmpty(a.hostilityTag)) return;

            var profile = AmbientMoodProfileSO.Instance;
            var extras = ExtrasProfileSO.Instance;
            float baseRadius = profile != null ? Mathf.Max(2f, profile.baseInfluenceRadius) : 8f;
            float infectiousBoost = extras != null ? Mathf.Clamp01(extras.infectiousJoinChance) : 0f;
            int infectiousCap = extras != null ? Mathf.Max(1, extras.maxInfectiousParticipants) : 1;
            float capBoost = Mathf.Lerp(1f, 1.5f, Mathf.Clamp01((infectiousCap - 1) / 4f));
            float radius = a.infectious ? baseRadius * (1f + infectiousBoost) * capBoost : baseRadius * 0.6f;
            float intensity = a.infectious ? 0.9f : 0.5f;
            float ttl = Mathf.Clamp(holdSeconds, 2f, 10f);

            string faction = EffectiveFaction();
            string[] factions = string.IsNullOrEmpty(faction) ? null : new[] { faction };
            AmbientMoodBoard.InjectLocalTag(a.hostilityTag, origin, radius, intensity, ttl, factions);
        }

        public bool TryGetActivityByName(string activityName, bool includePairActivities, out Activity activity)
        {
            activity = null;
            if (string.IsNullOrEmpty(activityName) || activities == null) return false;
            string factionId = EffectiveFaction();

            for (int i = 0; i < activities.Count; i++)
            {
                var a = activities[i];
                if (a == null) continue;
                if (!includePairActivities && a.participantMode == ActivityParticipantMode.Pair) continue;
                if (!IsFactionCompatible(a, factionId)) continue;
                if (string.Equals(a.activityName, activityName, System.StringComparison.OrdinalIgnoreCase))
                {
                    activity = a;
                    return true;
                }
            }

            return false;
        }

        private IEnumerator ExecuteActivity(Activity a, InteractionPoint preferredPoint = null)
        {
            if (a == null)
            {
                yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
                yield break;
            }

            string agentId = _tag != null ? _tag.agentId : gameObject.name;
            string occId = _tag != null ? _tag.agentId : gameObject.name;
            string factionId = EffectiveFaction();

            if (a.participantMode == ActivityParticipantMode.Pair)
            {
                // Pair activities are staged by T4EncounterResolver / ExtrasDirector,
                // not executed by solo local loops.
                yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
                yield break;
            }

            if (ShouldYieldToHigherPriorityWork(agentId))
            {
                yield break;
            }

            // Resolve anchor: InteractionPoint > LegacyAnchor > AreaAnchor (last resort only)
            InteractionPoint point = null;
            TargetTransformAnchor legacyAnchor = null;

            // Check if SceneDirector has a specific reservation for us
            if (SceneDirector.Instance != null && SceneDirector.Instance.CurrentContext != null)
            {
                SceneDirector.Instance.CurrentContext.reservedPoints.TryGetValue(occId, out point);
            }

            if (ShouldYieldToHigherPriorityWork(agentId))
            {
                yield break;
            }

            // Routine: prefer the agent's own assigned InteractionPoint when free.
            if (point == null && preferredPoint != null && preferredPoint.HasFreeSlot)
            {
                point = preferredPoint;
            }

            if (point == null && !string.IsNullOrEmpty(a.targetClass))
            {
                LogActivity($"[DOOMS] T4 Agent '{agentId}' looking for free targetClass '{a.targetClass}' (faction={factionId})");
                point = InteractionPointIndex.Nearest(transform.position, a.targetClass, factionId);
                if (point == null)
                {
                    legacyAnchor = TargetClassRegistry.FindClosestFree(a.targetClass, transform.position, factionId);
                }
            }

            Transform anchorT = null;
            float hold = Mathf.Max(1f, a.holdSeconds);

            if (point != null)
            {
                if (!point.TryOccupy(occId))
                {
                    Debug.LogWarning($"[DOOMS] T4 Agent '{agentId}' failed to occupy InteractionPoint '{a.targetClass}'. Spots full.");
                    yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
                    yield break;
                }
                _heldPoint = point;
                anchorT = point.GetAnchor();
                hold = Mathf.Max(1f, point.holdSeconds > 0 ? point.holdSeconds : a.holdSeconds);
                LogActivity($"[DOOMS] T4 Agent '{agentId}' occupied InteractionPoint '{point.gameObject.name}'. Navigating...");
            }
            else if (legacyAnchor != null)
            {
                if (!legacyAnchor.TryOccupy(occId))
                {
                    Debug.LogWarning($"[DOOMS] T4 Agent '{agentId}' failed to occupy legacy TargetTransformAnchor '{a.targetClass}'. Spots full.");
                    yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
                    yield break;
                }
                _heldAnchor = legacyAnchor;
                anchorT = legacyAnchor.GetAnchor();
                hold = Mathf.Max(1f, legacyAnchor.holdSeconds > 0 ? legacyAnchor.holdSeconds : a.holdSeconds);
                LogActivity($"[DOOMS] T4 Agent '{agentId}' occupied legacy anchor '{legacyAnchor.gameObject.name}'. Navigating...");
            }
            else
            {
                // InteractionPoint priority: if points serve this targetClass but none are
                // free/reachable right now (e.g. all occupied or the entrance is blocked),
                // do NOT substitute an AreaAnchor random-spot. Wait and retry so the agent
                // satisfies the routine only by reaching a real InteractionPoint.
                if (!string.IsNullOrEmpty(a.targetClass)
                    && InteractionPointIndex.Exists(a.targetClass, factionId))
                {
                    LogActivity($"[DOOMS] T4 Agent '{agentId}' targetClass '{a.targetClass}' has InteractionPoints but none free/reachable; waiting to retry (no area substitute).");
                    yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
                    yield break;
                }

                // No InteractionPoint serves this class — AreaAnchor as genuine last resort.
                if (!string.IsNullOrEmpty(a.targetClass))
                {
                    AreaAnchor area = AreaAnchorIndex.Nearest(transform.position, a.targetClass, factionId);
                    if (area != null)
                    {
                        AreaAnchor.PointOfInterest poi = null;
                        Vector3 targetPt;
                        string areaAnim = !string.IsNullOrEmpty(ResolveActivityPlaybackId(a)) ? ResolveActivityPlaybackId(a) : "Idle";
                        float areaHold = Mathf.Max(1f, a.holdSeconds);

                        if (area.TryGetNearestFreePointOfInterest(transform.position, occId, factionId, out poi) && poi != null && poi.GetAnchor() != null)
                        {
                            targetPt = poi.GetAnchor().position;
                            areaAnim = poi.ResolveAnimationId(areaAnim);
                            areaHold = Mathf.Max(1f, poi.ResolveHoldSeconds(areaHold));
                            _heldAreaPoint = poi;
                            LogActivity($"[DOOMS] T4 Agent '{agentId}' (area fallback) roaming to POI '{poi.GetAnchor().name}' for need {a.restoresNeed}");
                        }
                        else
                        {
                            targetPt = area.GetRandomPointWithin();
                            LogActivity($"[DOOMS] T4 Agent '{agentId}' (area fallback) roaming to point {targetPt} for need {a.restoresNeed}");
                        }

                        yield return StartCoroutine(NavigateAndWatch(targetPt, 15f, Mathf.Max(0.5f, _nav.stoppingDistance), agentId, () => ShouldYieldToHigherPriorityWork(agentId)));
                        if (_lastNavOutcome == NavOutcome.Aborted)
                        {
                            ReleaseHeldTargets(occId);
                            yield break;
                        }
                        StopNavForAction();

                        float dwell = _heldAreaPoint != null ? areaHold : Random.Range(area.roamMinDwellSec, area.roamMaxDwellSec);
                        string anim = areaAnim;
                        FaceNearestCoOccupantOrCenter(area);
                        LogActivity($"[DOOMS] T4 Agent '{agentId}' reached area fallback. Dwelling with animation '{anim}' for {dwell}s.");
                        ApplyActivityPropOverride(a);
                        ApplyActivityAmbientInfluence(a, targetPt, dwell);
                        if (_anim != null && !string.IsNullOrEmpty(anim))
                        {
                            _anim.PlayActionSequence(anim, dwell);
                        }

                        float dwellElapsed = 0f;
                        while (dwellElapsed < dwell)
                        {
                            if (ShouldYieldToHigherPriorityWork(agentId))
                            {
                                ReleaseHeldTargets(occId);
                                yield break;
                            }
                            yield return new WaitForSeconds(0.2f);
                            dwellElapsed += 0.2f;
                        }

                        if (_needs != null && a.restoreAmount > 0f)
                        {
                            _needs.Restore(a.restoresNeed, a.restoreAmount);
                        }
                        ReleaseHeldTargets(occId);
                        yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
                        yield break;
                    }
                }
                Debug.LogWarning($"[DOOMS] T4 Agent '{agentId}' failed to find any free anchor/point for targetClass '{a.targetClass}'.");
                yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
                yield break;
            }

            if (ShouldYieldToHigherPriorityWork(agentId))
            {
                ReleaseHeldTargets(occId);
                yield break;
            }

            // Navigate
            Vector3 destPos = anchorT != null ? anchorT.position : transform.position;
            yield return StartCoroutine(NavigateAndWatch(destPos, 20f, Mathf.Max(0.5f, _nav.stoppingDistance), agentId, () => ShouldYieldToHigherPriorityWork(agentId)));
            if (_lastNavOutcome == NavOutcome.Aborted)
            {
                ReleaseHeldTargets(occId);
                yield break;
            }

            // Require actual arrival: if navigation timed out (e.g. the point is blocked by
            // other agents) and we are not within close grace of the anchor, do NOT play the
            // action or satisfy the need at a wrong spot. Release the point and retry next tick.
            if (_lastNavOutcome != NavOutcome.Arrived && anchorT != null)
            {
                Vector3 toAnchorNav = anchorT.position - transform.position; toAnchorNav.y = 0f;
                if (toAnchorNav.sqrMagnitude > 4f) // farther than ~2 m
                {
                    LogActivity($"[DOOMS] T4 Agent '{agentId}' did not reach point '{a.targetClass}' (timed out, blocked); releasing and retrying.");
                    ReleaseHeldTargets(occId);
                    yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
                    yield break;
                }
            }
            StopNavForAction();

            // Snap + face. Only snap if we actually arrived near the anchor — otherwise
            // a timed-out navigation would teleport the agent across the scene.
            if (anchorT != null)
            {
                Vector3 toAnchor = anchorT.position - transform.position; toAnchor.y = 0f;
                if (toAnchor.sqrMagnitude <= 1f) // within ~0.5 m Previously was 4f
                    transform.position = anchorT.position;
                Vector3 fwd = anchorT.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            }

            // Social/Talk: approach to ~1-2 m of a real partner and face them
            // instead of playing the Talk clip at an empty anchor.
            if (IsSocialActivity(a))
            {
                yield return StartCoroutine(ApproachAndFacePartner(agentId, () => ShouldYieldToHigherPriorityWork(agentId)));
                if (_lastNavOutcome == NavOutcome.Aborted)
                {
                    ReleaseHeldTargets(occId);
                    yield break;
                }
            }

            // Play animator state or sequence
            string animToPlay = (point != null && !string.IsNullOrEmpty(point.sequenceId))
                ? point.sequenceId
                : (point != null && !string.IsNullOrEmpty(point.animatorStateName))
                    ? point.animatorStateName
                    : ResolveActivityPlaybackId(a);
            LogActivity($"[DOOMS] T4 Agent '{agentId}' reached anchor/point. Playing animation/sequence '{animToPlay}' for {hold} seconds.");
            if (_propDriver != null && point != null && !string.IsNullOrEmpty(point.propId))
            {
                Debug.Log($"[DOOMS][T4Brain] Applying prop override '{point.propId}' to '{gameObject.name}' for point '{point.gameObject.name}'.");
                _propDriver.SetInteractionPropOverride(point.propId);
            }
            else if (point != null && !string.IsNullOrEmpty(point.propId))
            {
                Debug.LogWarning($"[DOOMS][T4Brain] InteractionPoint '{point.gameObject.name}' has propId '{point.propId}' but no AnimatorPropDriver was found on '{gameObject.name}'.");
            }
            else
            {
                ApplyActivityPropOverride(a);
            }

            ApplyActivityAmbientInfluence(a, transform.position, hold);

            if (_anim != null && !string.IsNullOrEmpty(animToPlay))
            {
                _anim.PlayActionSequence(animToPlay, hold);
            }

            float holdElapsed = 0f;
            while (holdElapsed < hold)
            {
                if (ShouldYieldToHigherPriorityWork(agentId))
                {
                    ReleaseHeldTargets(occId);
                    yield break;
                }
                yield return new WaitForSeconds(0.2f);
                holdElapsed += 0.2f;
            }

            // Restore need
            if (_needs != null && a.restoreAmount > 0f)
            {
                _needs.Restore(a.restoresNeed, a.restoreAmount);
                LogActivity($"[DOOMS] T4 Agent '{agentId}' restored need '{a.restoresNeed}' by {a.restoreAmount:F2}.");
            }

            // Release point or legacy anchor
            if (_heldPoint != null)
            {
                _heldPoint.Release(occId);
                LogActivity($"[DOOMS] T4 Agent '{agentId}' released InteractionPoint '{_heldPoint.gameObject.name}' and completed activity.");
                _heldPoint = null;
            }
            else if (_heldAnchor != null)
            {
                _heldAnchor.Release(occId);
                LogActivity($"[DOOMS] T4 Agent '{agentId}' released legacy anchor '{_heldAnchor.gameObject.name}' and completed activity.");
                _heldAnchor = null;
            }

            if (_heldAreaPoint != null)
            {
                _heldAreaPoint.Release(occId);
                LogActivity($"[DOOMS] T4 Agent '{agentId}' released POI and completed activity.");
                _heldAreaPoint = null;
            }

            FinishActivityCleanup(occId);
            yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
        }

        private IEnumerator ExecuteAreaRole(FactionDirectiveBoard.AgentDirective dir, AreaAnchor area)
        {
            string agentId = _tag != null ? _tag.agentId : gameObject.name;
            string factionId = EffectiveFaction();
            var factionDirective = FactionDirectiveBoard.Get(factionId);

            if (area == null)
            {
                area = AreaAnchorIndex.Nearest(transform.position, dir.areaTag, factionId);
            }

            if (area == null)
            {
                Debug.LogWarning($"[DOOMS] T4 Agent '{agentId}' cannot execute Area role: no AreaAnchor found for tag '{dir.areaTag}'.");
                yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
                yield break;
            }

            // Apply preferred locomotion blendtree/style
            if (_anim != null && !string.IsNullOrEmpty(dir.preferredBlendTree))
            {
                _anim.SetLocoStyle(dir.preferredBlendTree);
            }

            if (ShouldYieldToHigherPriorityWork(agentId))
            {
                yield break;
            }

            AreaAnchor.PointOfInterest poi = null;
            Vector3 targetPt;
            string anim = !string.IsNullOrEmpty(dir.animationState) ? dir.animationState : "Idle";
            float dwell = Random.Range(area.roamMinDwellSec, area.roamMaxDwellSec);

            if (area.TryGetNearestFreePointOfInterest(transform.position, agentId, factionId, out poi) && poi != null && poi.GetAnchor() != null)
            {
                targetPt = poi.GetAnchor().position;
                anim = poi.ResolveAnimationId(anim);
                dwell = Mathf.Max(1f, poi.ResolveHoldSeconds(dwell));
                _heldAreaPoint = poi;
                LogActivity($"[DOOMS] T4 Agent '{agentId}' roaming to POI '{poi.GetAnchor().name}' in Area '{area.gameObject.name}'");
            }
            else
            {
                targetPt = area.GetRandomPointWithin();
                LogActivity($"[DOOMS] T4 Agent '{agentId}' roaming to point {targetPt} in Area '{area.gameObject.name}'");
            }

            yield return StartCoroutine(NavigateAndWatch(targetPt, 15f, Mathf.Max(0.5f, _nav.stoppingDistance), agentId, () => ShouldYieldToHigherPriorityWork(agentId)));
            if (_lastNavOutcome == NavOutcome.Aborted)
            {
                ReleaseHeldTargets(agentId);
                RestoreLocoStyle();
                yield break;
            }
            StopNavForAction();

            FaceNearestCoOccupantOrCenter(area);
            LogActivity($"[DOOMS] T4 Agent '{agentId}' reached roam target. Dwelling with animation '{anim}' for {dwell}s.");
            if (_anim != null && !string.IsNullOrEmpty(anim))
            {
                _anim.PlayActionSequence(anim, dwell);
            }

            float dwellElapsed = 0f;
            while (dwellElapsed < dwell)
            {
                if (ShouldYieldToHigherPriorityWork(agentId))
                {
                    ReleaseHeldTargets(agentId);
                    RestoreLocoStyle();
                    yield break;
                }

                if (factionDirective != null && AreaMatchesDirective(area, factionDirective))
                {
                    NudgeFaction(factionId, 0.002f * Mathf.Clamp01(factionDirective.intensity));
                }

                yield return new WaitForSeconds(0.2f);
                dwellElapsed += 0.2f;
            }

            ReleaseHeldTargets(agentId);

            // Restore locomotion style back to the agent's loadout default.
            RestoreLocoStyle();
        }

        private IEnumerator ExecuteTimelineRole(FactionDirectiveBoard.AgentDirective dir, TimelineAnchor timeline)
        {
            string agentId = _tag != null ? _tag.agentId : gameObject.name;

            if (timeline == null)
            {
                timeline = TimelineAnchorIndex.Find(dir.timelineAnchorId);
            }

            if (timeline == null)
            {
                Debug.LogWarning($"[DOOMS] T4 Agent '{agentId}' cannot execute Timeline role: no TimelineAnchor found for id '{dir.timelineAnchorId}'.");
                yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
                yield break;
            }

            var slot = timeline.FindSlot(dir.timelineSlotId);
            if (slot == null || slot.anchorTransform == null)
            {
                Debug.LogWarning($"[DOOMS] T4 Agent '{agentId}' cannot execute Timeline role: slot '{dir.timelineSlotId}' is missing or has no transform.");
                yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
                yield break;
            }

            Vector3 destPos = slot.anchorTransform.position;
            LogActivity($"[DOOMS] T4 Agent '{agentId}' moving to Timeline slot '{dir.timelineSlotId}' at {destPos}.");

            yield return StartCoroutine(NavigateAndWatch(destPos, 25f, Mathf.Max(0.3f, _nav.stoppingDistance), agentId, () => ShouldYieldToHigherPriorityWork(agentId)));
            if (_lastNavOutcome == NavOutcome.Aborted)
            {
                yield break;
            }
            StopNavForAction();

            // Snap position & rotation
            transform.position = slot.anchorTransform.position;
            Vector3 fwd = slot.anchorTransform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

            // Play waiting animation
            string waitAnim = !string.IsNullOrEmpty(dir.animationState) ? dir.animationState : "Idle";
            if (_anim != null && !string.IsNullOrEmpty(waitAnim) && !timeline.IsPlaying)
            {
                _anim.PlayActionState(waitAnim, 0.15f, -1f);
            }

            // Wait until timeline completes
            while (timeline != null && !timeline.IsComplete)
            {
                if (ShouldYieldToHigherPriorityWork(agentId))
                {
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
            }

            LogActivity($"[DOOMS] T4 Agent '{agentId}' completed choreographed Timeline execution.");
            yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
        }

        private void FaceNearestCoOccupantOrCenter(AreaAnchor area, float searchRadius = 5f)
        {
            if (area == null) return;
            Vector3 lookTarget = area.transform.position;
            float bestDist = float.MaxValue;
            Collider[] nearby = Physics.OverlapSphere(transform.position, searchRadius);
            for (int i = 0; i < nearby.Length; i++)
            {
                var ot = nearby[i] != null ? nearby[i].GetComponentInParent<DoomsAgentTag>() : null;
                if (ot == null || ot == _tag || ot.gameObject == gameObject) continue;
                float dist = Vector3.Distance(transform.position, ot.transform.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    lookTarget = ot.transform.position;
                }
            }
            Vector3 dir = lookTarget - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        // ─────────────────── Ambient reaction layer (Option C) ───────────────────
        // Unassigned agents inside a live scene's influence react to it based on
        // their faction's relation to the scene's driving factions.
        private IEnumerator ExecuteAmbientReaction()
        {
            string agentId = _tag != null ? _tag.agentId : gameObject.name;
            string factionId = EffectiveFaction();
            var profile = AmbientMoodProfileSO.Instance;
            if (profile == null)
            {
                yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
                yield break;
            }

            Relation rel = AmbientMoodBoard.WorstRelationForAt(factionId, transform.position);
            Vector3 epicenter = AmbientMoodBoard.EpicenterAt(transform.position);
            Vector3 away = transform.position - epicenter; away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) { away = Random.insideUnitSphere; away.y = 0f; }
            away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;

            if (rel == Relation.Hostile)
            {
                // Flee: head past the influence edge, faster gait, no socialising.
                float dist = AmbientMoodBoard.InfluenceRadiusAt(transform.position) + profile.fleeDistance;
                Vector3 fleeTarget = epicenter + away * dist;
                if (profile.fleeLocoStyle >= 0 && _anim != null) _anim.SetLocoStyle(profile.fleeLocoStyle);
                LogActivity($"[DOOMS] T4 Agent '{agentId}' (ambient) fleeing hostile scene to {fleeTarget}.");
                yield return StartCoroutine(MoveToReactionPoint(fleeTarget, agentId, profile.fleeAnimatorState, 0f, null));
                if (profile.fleeLocoStyle >= 0 && _anim != null) RestoreLocoStyle();
                NudgeSceneFactions(-0.01f * Mathf.Clamp01(AmbientMoodBoard.IntensityAt(transform.position)));
            }
            else if (rel == Relation.Ally || rel == Relation.Superior || rel == Relation.Subordinate)
            {
                // Approach & watch from just inside the influence edge.
                float standoffRadius = Mathf.Max(1f, AmbientMoodBoard.InfluenceRadiusAt(transform.position) - profile.watchStandoff);
                Vector3 watchTarget = epicenter + away * standoffRadius;
                float watch = Random.Range(profile.watchMinSec, profile.watchMaxSec);
                LogActivity($"[DOOMS] T4 Agent '{agentId}' (ambient) watching allied scene from edge.");
                // Already at a good vantage? Just face and watch — don't micro-reposition.
                if (Mathf.Abs(Vector3.Distance(transform.position, watchTarget)) <= 1.5f)
                {
                    StopNavForAction();
                    FaceTowards(epicenter);
                    if (_anim != null && !string.IsNullOrEmpty(profile.watchAnimatorState))
                        _anim.PlayActionState(profile.watchAnimatorState, 0.2f, watch);
                    float wt = 0f;
                    while (wt < watch)
                    {
                        if (ShouldYieldToHigherPriorityWork(agentId) || !AmbientMoodBoard.HasAnyInfluenceAt(transform.position))
                            break;
                        yield return new WaitForSeconds(0.2f);
                        wt += 0.2f;
                    }
                }
                else
                {
                    yield return StartCoroutine(MoveToReactionPoint(watchTarget, agentId, profile.watchAnimatorState, watch, epicenter));
                }
                NudgeSceneFactions(0.005f * PersonaSociability() * Mathf.Clamp01(AmbientMoodBoard.IntensityAt(transform.position)));
            }
            else
            {
                // Neutral: brief gawk where we stand, then re-evaluate (likely drifts off next tick).
                FaceTowards(epicenter);
                if (_anim != null && !string.IsNullOrEmpty(profile.watchAnimatorState))
                    _anim.PlayActionState(profile.watchAnimatorState, 0.15f, profile.watchMinSec);
                float t = 0f;
                while (t < profile.watchMinSec)
                {
                    if (ShouldYieldToHigherPriorityWork(agentId) || !AmbientMoodBoard.HasAnyInfluenceAt(transform.position))
                    {
                        ReleaseHeldTargets(agentId);
                        yield break;
                    }
                    yield return new WaitForSeconds(0.2f);
                    t += 0.2f;
                }
            }

            yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
        }

        private IEnumerator MoveToReactionPoint(Vector3 target, string agentId, string anim, float dwellSeconds, Vector3? faceTarget)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(target, out var hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
                target = hit.position;

            yield return StartCoroutine(NavigateAndWatch(target, 12f, Mathf.Max(0.5f, _nav.stoppingDistance), agentId,
                () => ShouldYieldToHigherPriorityWork(agentId) || !AmbientMoodBoard.HasAnyInfluenceAt(transform.position)));
            if (_lastNavOutcome == NavOutcome.Aborted)
            {
                ReleaseHeldTargets(agentId);
                yield break;
            }
            StopNavForAction();

            if (faceTarget.HasValue) FaceTowards(faceTarget.Value);

            if (dwellSeconds > 0f)
            {
                if (_anim != null && !string.IsNullOrEmpty(anim))
                    _anim.PlayActionSequence(anim, dwellSeconds);
                float d = 0f;
                while (d < dwellSeconds)
                {
                    if (ShouldYieldToHigherPriorityWork(agentId) || !AmbientMoodBoard.HasAnyInfluenceAt(transform.position))
                    {
                        ReleaseHeldTargets(agentId);
                        yield break;
                    }
                    yield return new WaitForSeconds(0.2f);
                    d += 0.2f;
                }
            }
        }

        private void FaceTowards(Vector3 worldPos)
        {
            Vector3 dir = worldPos - transform.position; dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        // ─────────────────── Extras Director goal layer (Option D) ───────────────────
        // ExtrasDirector queries this to score how interesting an area is to this
        // agent right now. Returns the best need-deficit*weight (+time bonus) across
        // activities whose targetClass matches the area tag, or -1 if none match.
        public float GetAreaTagInterest(string areaTag)
        {
            if (string.IsNullOrEmpty(areaTag)) return -1f;
            return GetAreaTagsInterest(new[] { areaTag });
        }

        public float GetAreaTagsInterest(IList<string> areaTags)
        {
            if (areaTags == null || areaTags.Count == 0 || activities == null) return -1f;
            float best = -1f;
            string factionId = EffectiveFaction();
            for (int i = 0; i < activities.Count; i++)
            {
                var a = activities[i];
                if (a == null || a.participantMode == ActivityParticipantMode.Pair || !MatchesAnyTag(a, areaTags))
                    continue;
                if (!IsFactionCompatible(a, factionId)) continue;
                float deficit = _needs != null ? _needs.GetDeficit(a.restoresNeed) : 0.5f;
                float s = deficit * Mathf.Max(0f, a.needWeight);
                if (a.restoresNeed == DoomsAgentNeeds.NeedType.Social)
                    s *= Mathf.Lerp(0.75f, 1.35f, PersonaSociability());
                if (a.timeBonus > 0f && IsInTimeWindow(GetEffectiveCurrentHour(), a.timeStartHour, a.timeEndHour))
                    s += a.timeBonus;
                if (s > best) best = s;
            }
            return best;
        }

        private Activity ChooseActivityForArea(IList<string> areaTags)
        {
            Activity best = null;
            float bestDef = -1f;
            if (areaTags == null || areaTags.Count == 0) return PickBestActivity();
            string factionId = EffectiveFaction();
            var directive = FactionDirectiveBoard.Get(factionId);

            if (activities != null)
            {
                // Pass 1: directive-matching activities only (authoritative)
                if (directive != null)
                {
                    for (int i = 0; i < activities.Count; i++)
                    {
                        var a = activities[i];
                        if (a == null || a.participantMode == ActivityParticipantMode.Pair || !MatchesAnyTag(a, areaTags)) continue;
                        if (!IsFactionCompatible(a, factionId)) continue;
                        bool match = (!string.IsNullOrEmpty(a.targetClass)          && directive.ContainsTargetClass(a.targetClass))
                                  || (!string.IsNullOrEmpty(a.directiveMatchAction) && directive.ContainsAction(a.directiveMatchAction));
                        if (!match) continue;
                        float def = _needs != null ? _needs.GetDeficit(a.restoresNeed) : 0.5f;
                        if (def > bestDef) { bestDef = def; best = a; }
                    }
                }

                // Pass 2: needs-based fallback (no active directive, or nothing matched it)
                if (best == null)
                {
                    bestDef = -1f;
                    for (int i = 0; i < activities.Count; i++)
                    {
                        var a = activities[i];
                        if (a == null || a.participantMode == ActivityParticipantMode.Pair || !MatchesAnyTag(a, areaTags)) continue;
                        if (!IsFactionCompatible(a, factionId)) continue;
                        float def = _needs != null ? _needs.GetDeficit(a.restoresNeed) : 0.5f;
                        if (def > bestDef) { bestDef = def; best = a; }
                    }
                }
            }
            return best ?? PickBestActivity();
        }

        private IEnumerator ExecuteExtrasGoal(AreaAnchor area)
        {
            string agentId = _tag != null ? _tag.agentId : gameObject.name;
            string occId = agentId;
            string effectiveFaction = EffectiveFaction();
            var factionDirective = FactionDirectiveBoard.Get(effectiveFaction);

            if (area == null || ShouldYieldToHigherPriorityWork(agentId))
            {
                yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
                yield break;
            }

            var areaTags = area.GetTagsForScene(ActiveSceneId());
            Activity chosen = ChooseActivityForArea(areaTags);
            bool areaOccupied = area.TryOccupy(occId);

            AreaAnchor.PointOfInterest poi = null;
            Vector3 targetPt;
            string anim = (chosen != null && !string.IsNullOrEmpty(ResolveActivityPlaybackId(chosen))) ? ResolveActivityPlaybackId(chosen) : "Idle";
            float dwell = Random.Range(area.roamMinDwellSec, area.roamMaxDwellSec);
            DoomsDebug.Log(DoomsDebug.Category.ActivitySelect,
                $"'{agentId}' ExtrasGoal area='{area.GetPrimaryTag(ActiveSceneId())}' tags=[{string.Join(",", areaTags)}] chosen='{chosen?.activityName ?? "none"}' anim='{anim}'.");

            if (area.TryGetNearestFreePointOfInterest(transform.position, occId, effectiveFaction, out poi) && poi != null && poi.GetAnchor() != null)
            {
                targetPt = poi.GetAnchor().position;
                anim = poi.ResolveAnimationId(anim);
                dwell = Mathf.Max(1f, poi.ResolveHoldSeconds(dwell));
                _heldAreaPoint = poi;
                LogActivity($"[DOOMS] T4 Agent '{agentId}' (extras) heading to POI '{poi.GetAnchor().name}' in area '{area.GetPrimaryTag(ActiveSceneId())}'");
            }
            else
            {
                targetPt = area.GetRandomPointWithin();
                LogActivity($"[DOOMS] T4 Agent '{agentId}' (extras) roaming within area '{area.GetPrimaryTag(ActiveSceneId())}'");
            }

            yield return StartCoroutine(NavigateAndWatch(targetPt, 15f, Mathf.Max(0.5f, _nav.stoppingDistance), agentId, () => ShouldYieldToHigherPriorityWork(agentId)));
            if (_lastNavOutcome == NavOutcome.Aborted)
            {
                if (areaOccupied) area.Release(occId);
                ReleaseHeldTargets(occId);
                yield break;
            }
            StopNavForAction();

            FaceNearestCoOccupantOrCenter(area);
            ApplyActivityPropOverride(chosen);
            ApplyActivityAmbientInfluence(chosen, targetPt, dwell);
            if (_anim != null && !string.IsNullOrEmpty(anim))
                _anim.PlayActionSequence(anim, dwell);

            float dwellElapsed = 0f;
            while (dwellElapsed < dwell)
            {
                if (ShouldYieldToHigherPriorityWork(agentId))
                {
                    if (areaOccupied) area.Release(occId);
                    ReleaseHeldTargets(occId);
                    yield break;
                }

                if (factionDirective != null && AreaMatchesDirective(area, factionDirective))
                {
                    NudgeFaction(effectiveFaction, 0.002f * Mathf.Clamp01(factionDirective.intensity));
                }

                yield return new WaitForSeconds(0.2f);
                dwellElapsed += 0.2f;
            }

            if (_needs != null && chosen != null && chosen.restoreAmount > 0f)
                _needs.Restore(chosen.restoresNeed, chosen.restoreAmount);

            if (_heldAreaPoint != null)
            {
                _heldAreaPoint.Release(occId);
                _heldAreaPoint = null;
            }
            if (areaOccupied) area.Release(occId);

            FinishActivityCleanup(occId);
            yield return new WaitForSeconds(Random.Range(minDecisionInterval, maxDecisionInterval));
        }

        private void OnDisable()
        {
            string occId = _tag != null ? _tag.agentId : gameObject.name;
            IsBusyWithRoutine = false;
            _courtesyCo = null;
            ReleaseHeldTargets(occId);
        }

        private FactionDirectiveBoard.AgentDirective _activeDirective;

        private bool ShouldYieldToHigherPriorityWork(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return false;

            if (T4EncounterResolver.IsAgentLocked(agentId)) return true;

            var cur = FactionDirectiveBoard.GetForAgent(agentId);
            if (cur == null) return false;

            if (_activeDirective != null)
                return cur.IsExpired || !ReferenceEquals(cur, _activeDirective);

            return true;
        }

        private void ReleaseHeldTargets(string occId)
        {
            if (_nav != null && _nav.enabled && _nav.isOnNavMesh)
            {
                _nav.isStopped = false;
                _nav.ResetPath();
            }

            if (_propDriver != null)
                _propDriver.ClearInteractionPropOverride();
            else
                Debug.LogWarning($"[DOOMS][T4Brain] ReleaseHeldTargets on '{gameObject.name}' could not clear prop override because AnimatorPropDriver was missing.");

            if (_anim != null)
            {
                _anim.StopActionPlayback(true);
            }

            if (!string.IsNullOrEmpty(occId))
            {
                if (_heldPoint != null)
                {
                    _heldPoint.Release(occId);
                }

                if (_heldAnchor != null)
                {
                    _heldAnchor.Release(occId);
                }

                if (_heldAreaPoint != null)
                {
                    _heldAreaPoint.Release(occId);
                }
            }

            _heldPoint = null;
            _heldAnchor = null;
            _heldAreaPoint = null;
        }
    }
}
