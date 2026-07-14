using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using MLA_SIM;
using MLA_SIM.Dooms.Scenes;

namespace MLA_SIM.Dooms
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DoomsAgentTag))]
    [RequireComponent(typeof(DoomsAgentNeeds))]
    [AddComponentMenu("MLA_SIM/DOOMS/T4 Encounter Resolver")]
    public class T4EncounterResolver : MonoBehaviour
    {
        [Header("Encounter Tuning")]
        [Tooltip("How often this resolver checks for nearby agents.")]
        public float checkIntervalSec = 1.25f;

        [Tooltip("Maximum distance for an encounter candidate.")]
        public float encounterRadius = 4f;

        [Range(0f, 1f)]
        [Tooltip("Chance to actually trigger when a valid partner is found.")]
        public float encounterChance = 0.2f;

        [Tooltip("How long an agent must wait after an encounter before starting another one.")]
        public float perAgentCooldownSec = 8f;

        [Tooltip("Minimum encounter hold time.")]
        public float minEncounterHoldSec = 2f;

        [Tooltip("Maximum encounter hold time.")]
        public float maxEncounterHoldSec = 4f;

        [Tooltip("Maximum time the resolver will wait before self-terminating an encounter.")]
        public float maxEncounterWatchdogSec = 6f;

        [Tooltip("Spacing applied when placing the two agents at the encounter point.")]
        public float pairSeparationMeters = 0.45f;

        [Tooltip("NavMesh sampling radius around the encounter point.")]
        public float navSampleRadius = 2f;

        [Tooltip("How far (m) each agent runs away from the other when an encounter resolves to Flee.")]
        public float fleeRunDistance = 8f;

        /// <summary>
        /// When true (set by ExtrasDirector), the resolver does NOT self-trigger
        /// random encounters in Update(); the director invokes TryStageEncounterWith.
        /// </summary>
        public static bool DirectorControlled = false;

        /// <summary>
        /// Fired when an encounter begins, for telemetry.
        /// (agentAId, agentBId, actionId, relation, position)
        /// </summary>
        public static event Action<string, string, string, Relation, Vector3> OnEncounterStaged;

        private static readonly Dictionary<string, string> _agentLocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, float> _agentCooldownUntil = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, EncounterSession> _activeSessions = new Dictionary<string, EncounterSession>(StringComparer.OrdinalIgnoreCase);

        private DoomsAgentTag _tag;
        private NavMeshAgent _nav;
        private AnimatorLocomotionDriver _anim;
        private float _nextScanTime;
        private Coroutine _activeRoutine;
        private string _ownedPairKey = "";
        private float _lastNavWarnTime = -999f;

        private sealed class EncounterSession
        {
            public string pairKey = "";
            public string agentAId = "";
            public string agentBId = "";
            public DoomsAgentTag tagA;
            public DoomsAgentTag tagB;
            public NavMeshAgent navA;
            public NavMeshAgent navB;
            public AnimatorLocomotionDriver animA;
            public AnimatorLocomotionDriver animB;
            public AreaAnchor area;
            public Relation relation = Relation.Neutral;
            public string sceneId = "";
            public string actionId = "Talk";
            public float holdSeconds = 2.5f;
            public float startedAt = 0f;
            public float watchdogDeadline = 0f;
            public Vector3 meetingPoint;
        }

        public static bool IsAgentLocked(string agentId)
        {
            return !string.IsNullOrEmpty(agentId) && _agentLocks.ContainsKey(agentId);
        }

        private void Awake()
        {
            _tag = GetComponent<DoomsAgentTag>();
            _nav = GetComponent<NavMeshAgent>();
            _anim = GetComponent<AnimatorLocomotionDriver>();
        }

        private void OnEnable()
        {
            _nextScanTime = Time.time + UnityEngine.Random.Range(0f, Mathf.Max(0.1f, checkIntervalSec));
        }

        private void Update()
        {
            if (DirectorControlled)
            {
                // ExtrasDirector owns encounter selection; skip autonomous scanning.
                return;
            }

            if (_activeRoutine != null)
            {
                return;
            }

            if (Time.time < _nextScanTime)
            {
                return;
            }

            _nextScanTime = Time.time + Mathf.Max(0.2f, checkIntervalSec);
            TryStartEncounter();
        }

        /// <summary>
        /// Director-invoked entry point: stage an encounter with an explicit partner,
        /// bypassing the random encounterChance. Action (Talk/Fight/Flee) is still
        /// resolved from faction relation + area via ResolvePairAction. Returns true
        /// if an encounter session was started.
        /// </summary>
        public bool TryStageEncounterWith(DoomsAgentTag partner)
        {
            if (_activeRoutine != null) return false;
            if (_tag == null || _nav == null || _anim == null) return false;
            if (partner == null) return false;
            if (!CanSeekEncounter(_tag) || !CanSeekEncounter(partner)) return false;
            if (Vector3.Distance(transform.position, partner.transform.position) > encounterRadius * 1.5f) return false;

            if (!TryCreateSession(_tag, partner, out var session)) return false;

            _ownedPairKey = session.pairKey;
            _activeRoutine = StartCoroutine(RunEncounter(session));
            return true;
        }

        private void OnDisable()
        {
            if (_activeRoutine != null)
            {
                StopCoroutine(_activeRoutine);
                _activeRoutine = null;
            }

            if (!string.IsNullOrEmpty(_ownedPairKey))
            {
                ForceReleaseSession(_ownedPairKey, true);
                _ownedPairKey = "";
            }
        }

        private void TryStartEncounter()
        {
            if (_tag == null || _nav == null || _anim == null)
            {
                return;
            }

            string myId = _tag.agentId;
            string myFaction = DoomsFactionRuntime.EffectiveFactionOf(_tag);
            if (string.IsNullOrEmpty(myId) || string.IsNullOrEmpty(myFaction))
            {
                return;
            }

            if (!CanSeekEncounter(_tag))
            {
                return;
            }

            var partner = FindCandidatePartner();
            if (partner == null)
            {
                return;
            }

            if (UnityEngine.Random.value > Mathf.Clamp01(encounterChance))
            {
                return;
            }

            if (!TryCreateSession(_tag, partner, out var session))
            {
                return;
            }

            _ownedPairKey = session.pairKey;
            _activeRoutine = StartCoroutine(RunEncounter(session));
        }

        private bool CanSeekEncounter(DoomsAgentTag tag)
        {
            if (tag == null)
            {
                return false;
            }

            string agentId = tag.agentId;
            if (string.IsNullOrEmpty(agentId))
            {
                return false;
            }

            if (DoomsAgentCombat.IsAgentDead(tag))
            {
                return false;
            }

            // Don't pull an agent off a scheduled routine task for random chatter.
            var brain = tag.GetComponent<DoomsAgentT4Brain>();
            if (brain != null && brain.IsBusyWithRoutine)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(tag.reservedBySceneId))
            {
                return false;
            }

            if (FactionDirectiveBoard.GetForAgent(agentId) != null)
            {
                return false;
            }

            if (IsAgentLocked(agentId))
            {
                return false;
            }

            if (_agentCooldownUntil.TryGetValue(agentId, out float until) && Time.time < until)
            {
                return false;
            }

            return true;
        }

        private DoomsAgentTag FindCandidatePartner()
        {
            var allTags = FindObjectsByType<DoomsAgentTag>(FindObjectsSortMode.None);
            DoomsAgentTag best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < allTags.Length; i++)
            {
                var candidate = allTags[i];
                if (candidate == null || candidate == _tag) continue;
                if (DoomsAgentCombat.IsAgentDead(candidate)) continue;
                if (candidate.tierEnum != DoomsAgentTag.DoomsTier.AnimationOnly && candidate.tier != (int)DoomsAgentTag.DoomsTier.AnimationOnly)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(candidate.agentId) || string.IsNullOrEmpty(DoomsFactionRuntime.EffectiveFactionOf(candidate)))
                {
                    continue;
                }

                if (!CanSeekEncounter(candidate))
                {
                    continue;
                }

                float distance = Vector3.Distance(transform.position, candidate.transform.position);
                if (distance > encounterRadius)
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private bool TryCreateSession(DoomsAgentTag tagA, DoomsAgentTag tagB, out EncounterSession session)
        {
            session = null;
            if (tagA == null || tagB == null)
            {
                return false;
            }

            string idA = tagA.agentId;
            string idB = tagB.agentId;
            if (string.IsNullOrEmpty(idA) || string.IsNullOrEmpty(idB))
            {
                return false;
            }

            string pairKey = GetPairKey(idA, idB);
            if (_activeSessions.ContainsKey(pairKey))
            {
                return false;
            }

            if (IsAgentLocked(idA) || IsAgentLocked(idB))
            {
                return false;
            }

            string factionA = DoomsFactionRuntime.EffectiveFactionOf(tagA);
            string factionB = DoomsFactionRuntime.EffectiveFactionOf(tagB);
            if (string.IsNullOrEmpty(factionA) || string.IsNullOrEmpty(factionB))
            {
                return false;
            }

            var area = FindSharedArea(tagA.transform.position, tagB.transform.position, factionA, factionB);

            var relations = FactionRelationsSO.Instance;
            Relation relation = relations != null ? relations.GetRelation(factionA, factionB) : Relation.Neutral;
            string actionId = ResolvePairAction(area, relation, tagA, tagB);

            // Social encounters stay grounded in an authored shared area, but hostile /
            // lethal confrontations may happen anywhere (meeting falls back to midpoint).
            bool hostileAction = string.Equals(actionId, "Fight", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(actionId, "Shoot", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(actionId, "Flee", StringComparison.OrdinalIgnoreCase);
            if (area == null && !hostileAction)
            {
                return false;
            }

            float holdSeconds = UnityEngine.Random.Range(Mathf.Max(1f, minEncounterHoldSec), Mathf.Max(minEncounterHoldSec, maxEncounterHoldSec));
            holdSeconds = Mathf.Clamp(holdSeconds, Mathf.Max(1f, minEncounterHoldSec), Mathf.Max(minEncounterHoldSec, maxEncounterHoldSec));

            session = new EncounterSession
            {
                pairKey = pairKey,
                agentAId = idA,
                agentBId = idB,
                tagA = tagA,
                tagB = tagB,
                navA = tagA.GetComponent<NavMeshAgent>(),
                navB = tagB.GetComponent<NavMeshAgent>(),
                animA = tagA.GetComponent<AnimatorLocomotionDriver>(),
                animB = tagB.GetComponent<AnimatorLocomotionDriver>(),
                area = area,
                relation = relation,
                sceneId = GetActiveSceneId(),
                actionId = actionId,
                holdSeconds = holdSeconds,
                startedAt = Time.time,
                watchdogDeadline = Time.time + Mathf.Max(maxEncounterWatchdogSec, holdSeconds + 1f)
            };

            if (!TryLockSession(session))
            {
                session = null;
                return false;
            }

            return true;
        }

        private static bool TryLockSession(EncounterSession session)
        {
            if (session == null || string.IsNullOrEmpty(session.pairKey))
            {
                return false;
            }

            if (_activeSessions.ContainsKey(session.pairKey))
            {
                return false;
            }

            if (_agentLocks.ContainsKey(session.agentAId) || _agentLocks.ContainsKey(session.agentBId))
            {
                return false;
            }

            _activeSessions[session.pairKey] = session;
            _agentLocks[session.agentAId] = session.pairKey;
            _agentLocks[session.agentBId] = session.pairKey;
            return true;
        }

        private IEnumerator RunEncounter(EncounterSession session)
        {
            bool aborted = false;
            try
            {
                if (!PrepareSession(session))
                {
                    aborted = true;
                    yield break;
                }

                if (ShouldAbortEncounter(session))
                {
                    aborted = true;
                    yield break;
                }

                bool isFlee = string.Equals(session.actionId, "Flee", StringComparison.OrdinalIgnoreCase);
                bool isShoot = string.Equals(session.actionId, "Shoot", StringComparison.OrdinalIgnoreCase);
                PublishEncounterAmbientInfluence(session);

                Vector3 encPos = (session.tagA.transform.position + session.tagB.transform.position) * 0.5f;
                OnEncounterStaged?.Invoke(session.agentAId, session.agentBId, session.actionId, session.relation, encPos);

                if (isShoot)
                {
                    // Ranged + terminal: stand off, shoot, resolve kill/wound. No
                    // warp-together staging.
                    yield return StartCoroutine(RunShoot(session));
                }
                else if (isFlee)
                {
                    // Run away from each other along real NavMesh paths instead of
                    // warping together and playing an in-place clip.
                    StartFlee(session);
                }
                else
                {
                    if (!TryResolveMeetingPoint(session.area, session.tagA.transform.position, session.tagB.transform.position, out var meetingPoint))
                    {
                        aborted = true;
                        yield break;
                    }
                    session.meetingPoint = meetingPoint;

                    if (ShouldAbortEncounter(session))
                    {
                        aborted = true;
                        yield break;
                    }

                    // Walk both agents to the meeting point and only play the
                    // interaction once they've arrived (no instant warp/snap).
                    yield return StartCoroutine(WalkParticipantsToMeeting(session));

                    if (ShouldAbortEncounter(session))
                    {
                        aborted = true;
                        yield break;
                    }

                    // Restart the hold/watchdog clock from arrival so the walk time
                    // doesn't eat into the interaction's hold duration.
                    session.startedAt = Time.time;
                    session.watchdogDeadline = Time.time + Mathf.Max(maxEncounterWatchdogSec, session.holdSeconds + 1f);

                    PlayEncounterSequence(session);
                }

                while (Time.time < session.watchdogDeadline)
                {
                    if (ShouldAbortEncounter(session))
                    {
                        aborted = true;
                        break;
                    }

                    if (Time.time >= session.startedAt + session.holdSeconds)
                    {
                        break;
                    }

                    yield return new WaitForSeconds(0.1f);
                }
            }
            finally
            {
                CleanupSession(session, aborted);
                _activeRoutine = null;
                if (_ownedPairKey == session.pairKey)
                {
                    _ownedPairKey = "";
                }
            }
        }

        private bool PrepareSession(EncounterSession session)
        {
            if (session == null || session.tagA == null || session.tagB == null)
            {
                return false;
            }

            if (FactionDirectiveBoard.GetForAgent(session.agentAId) != null || FactionDirectiveBoard.GetForAgent(session.agentBId) != null)
            {
                return false;
            }

            string activeSceneId = GetActiveSceneId();
            if (!string.IsNullOrEmpty(activeSceneId) && !string.Equals(activeSceneId, session.sceneId, StringComparison.OrdinalIgnoreCase))
            {
                // If the scene changed before the encounter is prepared, defer to the scene.
                return false;
            }

            PauseParticipant(session.navA, session.animA);
            PauseParticipant(session.navB, session.animB);
            return true;
        }

        private bool PositionParticipants(EncounterSession session)
        {
            if (session == null || session.tagA == null || session.tagB == null)
            {
                return false;
            }

            Vector3 center = session.meetingPoint;
            Vector3 between = session.tagB.transform.position - session.tagA.transform.position;
            between.y = 0f;
            if (between.sqrMagnitude < 0.01f)
            {
                between = session.tagA.transform.forward;
                between.y = 0f;
            }
            if (between.sqrMagnitude < 0.01f)
            {
                between = Vector3.forward;
            }

            Vector3 sideways = Vector3.Cross(Vector3.up, between.normalized);
            if (!TryProjectToNavMesh(center - sideways * pairSeparationMeters, out Vector3 posA) ||
                !TryProjectToNavMesh(center + sideways * pairSeparationMeters, out Vector3 posB))
            {
                WarnNavIssue("could not project encounter participant positions to NavMesh");
                return false;
            }

            if (session.navA != null && session.navA.isOnNavMesh)
            {
                session.navA.Warp(posA);
            }
            else
            {
                session.tagA.transform.position = posA;
            }

            if (session.navB != null && session.navB.isOnNavMesh)
            {
                session.navB.Warp(posB);
            }
            else
            {
                session.tagB.transform.position = posB;
            }

            Vector3 faceA = (session.tagB.transform.position - session.tagA.transform.position);
            faceA.y = 0f;
            if (faceA.sqrMagnitude > 0.001f)
            {
                session.tagA.transform.rotation = Quaternion.LookRotation(faceA.normalized, Vector3.up);
            }

            Vector3 faceB = (session.tagA.transform.position - session.tagB.transform.position);
            faceB.y = 0f;
            if (faceB.sqrMagnitude > 0.001f)
            {
                session.tagB.transform.rotation = Quaternion.LookRotation(faceB.normalized, Vector3.up);
            }

            return true;
        }

        // Walk both participants to their slots around the meeting point, then pause +
        // face them. Replaces the instant Warp so encounters read as the agents coming
        // together. A straggler that can't path in time is warped as a last resort.
        private IEnumerator WalkParticipantsToMeeting(EncounterSession session)
        {
            if (session == null || session.tagA == null || session.tagB == null) yield break;

            Vector3 center = session.meetingPoint;
            Vector3 between = session.tagB.transform.position - session.tagA.transform.position;
            between.y = 0f;
            if (between.sqrMagnitude < 0.01f) { between = session.tagA.transform.forward; between.y = 0f; }
            if (between.sqrMagnitude < 0.01f) between = Vector3.forward;
            Vector3 sideways = Vector3.Cross(Vector3.up, between.normalized);

            if (!TryProjectToNavMesh(center - sideways * pairSeparationMeters, out Vector3 posA))
                posA = session.tagA.transform.position;
            if (!TryProjectToNavMesh(center + sideways * pairSeparationMeters, out Vector3 posB))
                posB = session.tagB.transform.position;

            // PrepareSession paused them — resume and head to the slots.
            ResumeWalk(session.navA, posA);
            ResumeWalk(session.navB, posB);

            float timeout = Mathf.Max(2f, maxEncounterWatchdogSec);
            float t = 0f;
            while (t < timeout)
            {
                if (ShouldAbortEncounter(session)) yield break;
                if (HasReached(session.navA, posA) && HasReached(session.navB, posB)) break;
                yield return new WaitForSeconds(0.1f);
                t += 0.1f;
            }

            // Settle: warp any straggler still far from its slot, then pause + face.
            SettleAtSlot(session.navA, session.tagA, posA);
            SettleAtSlot(session.navB, session.tagB, posB);
            PauseParticipant(session.navA, session.animA);
            PauseParticipant(session.navB, session.animB);
            FaceEachOther(session.tagA, session.tagB);
        }

        private static void ResumeWalk(NavMeshAgent nav, Vector3 dest)
        {
            if (nav == null || !nav.enabled || !nav.isOnNavMesh) return;
            nav.isStopped = false;
            nav.avoidancePriority = 50; // normal mover priority while approaching
            nav.SetDestination(dest);
        }

        private static bool HasReached(NavMeshAgent nav, Vector3 dest)
        {
            if (nav == null || !nav.enabled || !nav.isOnNavMesh) return true; // can't move -> don't block
            if (nav.pathPending) return false;
            if (!nav.hasPath) return true;
            float arrive = Mathf.Max(0.4f, nav.stoppingDistance + 0.3f);
            return nav.remainingDistance <= arrive;
        }

        private void SettleAtSlot(NavMeshAgent nav, DoomsAgentTag tag, Vector3 slot)
        {
            if (tag == null) return;
            Vector3 d = slot - tag.transform.position; d.y = 0f;
            if (d.sqrMagnitude <= 4f) return; // within ~2m, close enough — no warp
            if (nav != null && nav.enabled && nav.isOnNavMesh) nav.Warp(slot);
            else tag.transform.position = slot;
        }

        private static void FaceEachOther(DoomsAgentTag a, DoomsAgentTag b)
        {
            if (a == null || b == null) return;
            Vector3 fa = b.transform.position - a.transform.position; fa.y = 0f;
            if (fa.sqrMagnitude > 0.001f) a.transform.rotation = Quaternion.LookRotation(fa.normalized, Vector3.up);
            Vector3 fb = a.transform.position - b.transform.position; fb.y = 0f;
            if (fb.sqrMagnitude > 0.001f) b.transform.rotation = Quaternion.LookRotation(fb.normalized, Vector3.up);
        }

        // Ranged lethal encounter: the initiator (A) faces the target (B) from
        // standoff range, plays a shoot clip, then resolves a kill (terminal death
        // on B) or a wound (B flees). All consequences route through DoomsViolence.
        private IEnumerator RunShoot(EncounterSession session)
        {
            var shooter = session.tagA;
            var target = session.tagB;
            if (shooter == null || target == null) yield break;

            var shooterCombat = shooter.GetComponent<DoomsAgentCombat>();
            if (shooterCombat == null) yield break;

            // Face the target; do NOT warp together (ranged).
            Vector3 toTarget = target.transform.position - shooter.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
                shooter.transform.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);

            // Pause the shooter's locomotion and play the shoot clip (blendtree
            // sequence preferred; falls back to a discrete state).
            PauseParticipant(session.navA, session.animA);
            if (session.animA != null)
            {
                float shootHold = Mathf.Max(0.5f, session.holdSeconds * 0.5f);
                if (!string.IsNullOrEmpty(shooterCombat.shootSequenceId))
                    session.animA.PlayActionSequence(shooterCombat.shootSequenceId, shootHold);
                else
                    session.animA.PlayBumpReaction(shooterCombat.shootStateNames, shootHold);
            }

            // Brief wind-up before the shot lands.
            yield return new WaitForSeconds(0.5f);

            if (shooter == null || target == null) yield break;

            var targetCombat = target.GetComponent<DoomsAgentCombat>();
            bool lethal = targetCombat != null
                          && !targetCombat.IsDead
                          && UnityEngine.Random.value < Mathf.Clamp01(shooterCombat.killProbability);

            Vector3 pos = target.transform.position;

            if (lethal)
            {
                targetCombat.Kill(shooter);
                DoomsViolence.ReportViolence(shooter, target, pos, true);
            }
            else
            {
                // Wounded / missed: the target flees, witnesses still react.
                DoomsViolence.ReportViolence(shooter, target, pos, false);
                FleeAway(session.navB, session.animB, target.transform.position, shooter.transform.position);
            }
        }

        private void StartFlee(EncounterSession session)
        {
            if (session == null) return;
            Vector3 mid = (session.tagA.transform.position + session.tagB.transform.position) * 0.5f;
            FleeAway(session.navA, session.animA, session.tagA.transform.position, mid);
            FleeAway(session.navB, session.animB, session.tagB.transform.position, mid);
        }

        private void FleeAway(NavMeshAgent nav, AnimatorLocomotionDriver anim, Vector3 pos, Vector3 from)
        {
            // Resume locomotion (PrepareSession paused it for Talk/Fight staging).
            if (anim != null) anim.StopActionPlayback(true);

            Vector3 away = pos - from; away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) { away = UnityEngine.Random.insideUnitSphere; away.y = 0f; }
            away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;

            if (!TryProjectToNavMesh(pos + away * Mathf.Max(1f, fleeRunDistance), out Vector3 dest))
            {
                WarnNavIssue("could not project flee destination to NavMesh");
                return;
            }
            if (nav != null && nav.enabled && nav.isOnNavMesh)
            {
                nav.isStopped = false;
                nav.SetDestination(dest);
            }
        }

        private void PlayEncounterSequence(EncounterSession session)
        {
            if (session == null)
            {
                return;
            }

            TryResolveActivityDefinition(session, out var resolvedActivity);
            string playbackId = ResolveActivityPlaybackId(resolvedActivity, session.actionId);

            if (resolvedActivity != null && !string.IsNullOrEmpty(resolvedActivity.propId))
            {
                ApplyPropOverride(session.tagA, resolvedActivity.propId);
                ApplyPropOverride(session.tagB, resolvedActivity.propId);
            }

            if (session.animA != null && !string.IsNullOrEmpty(playbackId))
            {
                session.animA.PlayActionSequence(playbackId, session.holdSeconds);
            }

            if (session.animB != null && !string.IsNullOrEmpty(playbackId))
            {
                session.animB.PlayActionSequence(playbackId, session.holdSeconds);
            }
        }

        private void CleanupSession(EncounterSession session, bool aborted)
        {
            if (session == null)
            {
                return;
            }

            if (!aborted)
            {
                ApplyPersonaOutcomeDrift(session);
            }

            ClearPropOverride(session.tagA);
            ClearPropOverride(session.tagB);

            if (aborted)
            {
                AbortParticipant(session.navA, session.animA);
                AbortParticipant(session.navB, session.animB);
            }
            else
            {
                ResumeParticipant(session.navA, session.animA);
                ResumeParticipant(session.navB, session.animB);
            }

            _agentCooldownUntil[session.agentAId] = Time.time + Mathf.Max(1f, perAgentCooldownSec);
            _agentCooldownUntil[session.agentBId] = Time.time + Mathf.Max(1f, perAgentCooldownSec);

            if (_agentLocks.TryGetValue(session.agentAId, out string lockA) && string.Equals(lockA, session.pairKey, StringComparison.OrdinalIgnoreCase))
            {
                _agentLocks.Remove(session.agentAId);
            }

            if (_agentLocks.TryGetValue(session.agentBId, out string lockB) && string.Equals(lockB, session.pairKey, StringComparison.OrdinalIgnoreCase))
            {
                _agentLocks.Remove(session.agentBId);
            }

            if (_activeSessions.ContainsKey(session.pairKey))
            {
                _activeSessions.Remove(session.pairKey);
            }
        }

        private void ForceReleaseSession(string pairKey, bool aborted)
        {
            if (string.IsNullOrEmpty(pairKey))
            {
                return;
            }

            if (_activeSessions.TryGetValue(pairKey, out var session))
            {
                CleanupSession(session, aborted);
            }
        }

        private bool ShouldAbortEncounter(EncounterSession session)
        {
            if (session == null || session.tagA == null || session.tagB == null)
            {
                return true;
            }

            if (!string.Equals(GetActiveSceneId(), session.sceneId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (FactionDirectiveBoard.GetForAgent(session.agentAId) != null || FactionDirectiveBoard.GetForAgent(session.agentBId) != null)
            {
                return true;
            }

            if (!IsAgentLocked(session.agentAId) || !IsAgentLocked(session.agentBId))
            {
                return true;
            }

            if (!string.Equals(_agentLocks[session.agentAId], session.pairKey, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_agentLocks[session.agentBId], session.pairKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static void PauseParticipant(NavMeshAgent nav, AnimatorLocomotionDriver anim)
        {
            if (nav != null && nav.enabled)
            {
                nav.isStopped = true;
                // High-importance obstacle (low number) so moving agents route around a
                // standing encounter pair instead of shoving through and deadlocking.
                nav.avoidancePriority = 20;
                if (nav.isOnNavMesh)
                {
                    nav.ResetPath();
                }
            }

            if (anim != null)
            {
                anim.StopActionPlayback(true);
            }
        }

        // A killed agent holds its death pose and is off the NavMesh — never reset
        // its animator or nav during cleanup.
        private static bool IsDeadParticipant(AnimatorLocomotionDriver anim)
        {
            if (anim == null) return false;
            var combat = anim.GetComponent<DoomsAgentCombat>();
            return combat != null && combat.IsDead;
        }

        private static void AbortParticipant(NavMeshAgent nav, AnimatorLocomotionDriver anim)
        {
            if (IsDeadParticipant(anim)) return;

            if (anim != null)
            {
                anim.StopActionPlayback(true);
            }

            if (nav != null && nav.enabled)
            {
                nav.isStopped = false;
                if (nav.isOnNavMesh)
                {
                    nav.ResetPath();
                }
            }
        }

        private static void ResumeParticipant(NavMeshAgent nav, AnimatorLocomotionDriver anim)
        {
            if (IsDeadParticipant(anim)) return;

            if (nav != null && nav.enabled)
            {
                nav.isStopped = false;
                // Restore a normal mover priority; the brain re-asserts its own on the
                // next TryBeginNavMove.
                nav.avoidancePriority = 50;
            }

            if (anim != null)
            {
                anim.StopActionPlayback(true);
            }
        }

        private AreaAnchor FindSharedArea(Vector3 posA, Vector3 posB, string factionA, string factionB)
        {
            var allAreas = FindObjectsByType<AreaAnchor>(FindObjectsSortMode.None);
            if (allAreas == null || allAreas.Length == 0)
            {
                return null;
            }

            Vector3 midpoint = (posA + posB) * 0.5f;
            AreaAnchor best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < allAreas.Length; i++)
            {
                var area = allAreas[i];
                if (area == null) continue;
                if (!area.IsFactionAllowed(factionA) || !area.IsFactionAllowed(factionB)) continue;

                float distance = Vector3.Distance(midpoint, area.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = area;
                }
            }

            return best;
        }

        // tagA is always the encounter initiator (this resolver's owner); tagB the target.
        private string ResolvePairAction(AreaAnchor area, Relation relation, DoomsAgentTag tagA, DoomsAgentTag tagB)
        {
            var profile = ExtrasProfileSO.Instance;
            string factionB = DoomsFactionRuntime.EffectiveFactionOf(tagB);
            var personaA = tagA != null ? tagA.GetComponent<DoomsAgentPersona>() : null;
            float aggrA = personaA != null ? personaA.aggression : 0.5f;

            // Blend the INITIATOR's personal hostility toward the target's faction
            // with the faction-level relation and the aggression trait. This lets an
            // individual grudge (persona affinity) override faction direction — e.g.
            // a guard whose personal anti-luddite affinity is high shoots even when
            // his faction is only "patrol".
            float wPersona = profile != null ? profile.hostilityPersonaWeight : 0.6f;
            float wRelation = profile != null ? profile.hostilityRelationWeight : 0.5f;
            float wAggr = profile != null ? profile.hostilityAggressionWeight : 0.3f;

            float personaHostility = personaA != null ? Mathf.Clamp01(personaA.HostilityToward(factionB)) : 0f;
            float relationHostility = relation == Relation.Hostile ? 1f : 0f;
            float hostility = wPersona * personaHostility + wRelation * relationHostility + wAggr * aggrA;

            // Lethal escalation: capable + gate open + target alive + hostility past
            // the initiator's personal lethal threshold.
            var combatA = tagA != null ? tagA.GetComponent<DoomsAgentCombat>() : null;
            if (combatA != null && combatA.CanShoot && DoomsViolence.LethalAllowed
                && hostility >= combatA.lethalHostilityThreshold
                && !DoomsAgentCombat.IsAgentDead(tagB))
            {
                return "Shoot";
            }

            // Blended hostility (persona + relation + aggression) drives melee — a strong
            // individual grudge starts a fight even across a non-Hostile faction relation.
            float fightThreshold = profile != null ? profile.fightHostilityThreshold : 0.4f;
            if (hostility >= fightThreshold)
            {
                // Aggressive initiators brawl; timid ones flee.
                float fightChance = Mathf.Lerp(0.2f, 0.9f, aggrA);
                return UnityEngine.Random.value < fightChance ? "Fight" : "Flee";
            }

            // Non-hostile: an authored area pair-activity wins (designer intent),
            // otherwise default to Talk. (Checked AFTER hostility so a grudge is
            // never overridden by the area's social activity such as "drink".)
            if (area != null && !string.IsNullOrEmpty(area.defaultPairActivity))
            {
                return area.defaultPairActivity;
            }

            return "Talk";
        }

        private static string ResolveActivityPlaybackId(DoomsAgentT4Brain.Activity activity, string fallback)
        {
            if (activity == null) return fallback;
            if (!string.IsNullOrEmpty(activity.sequenceId)) return activity.sequenceId;
            if (!string.IsNullOrEmpty(activity.animatorStateName)) return activity.animatorStateName;
            if (!string.IsNullOrEmpty(activity.activityName)) return activity.activityName;
            return fallback;
        }

        private static void ApplyPropOverride(DoomsAgentTag tag, string propId)
        {
            if (tag == null || string.IsNullOrEmpty(propId)) return;

            var prop = tag.GetComponent<AnimatorPropDriver>()
                       ?? tag.GetComponentInChildren<AnimatorPropDriver>(true)
                       ?? tag.GetComponentInParent<AnimatorPropDriver>();

            if (prop != null)
                prop.SetInteractionPropOverride(propId);
        }

        private static void ClearPropOverride(DoomsAgentTag tag)
        {
            if (tag == null) return;

            var prop = tag.GetComponent<AnimatorPropDriver>()
                       ?? tag.GetComponentInChildren<AnimatorPropDriver>(true)
                       ?? tag.GetComponentInParent<AnimatorPropDriver>();

            if (prop != null)
                prop.ClearInteractionPropOverride();
        }

        private bool TryResolveActivityDefinition(EncounterSession session, out DoomsAgentT4Brain.Activity activity)
        {
            activity = null;
            if (session == null || string.IsNullOrEmpty(session.actionId)) return false;

            var brainA = session.tagA != null ? session.tagA.GetComponent<DoomsAgentT4Brain>() : null;
            if (brainA != null && brainA.TryGetActivityByName(session.actionId, includePairActivities: true, out activity))
                return true;

            var brainB = session.tagB != null ? session.tagB.GetComponent<DoomsAgentT4Brain>() : null;
            if (brainB != null && brainB.TryGetActivityByName(session.actionId, includePairActivities: true, out activity))
                return true;

            var catalog = ActivityCatalogSO.Instance;
            if (catalog == null) return false;

            string factionA = DoomsFactionRuntime.EffectiveFactionOf(session.tagA);
            string factionB = DoomsFactionRuntime.EffectiveFactionOf(session.tagB);

            if (TryFindCatalogActivity(catalog.Resolve(factionA), session.actionId, out activity)) return true;
            if (TryFindCatalogActivity(catalog.Resolve(factionB), session.actionId, out activity)) return true;
            if (TryFindCatalogActivity(catalog.shared, session.actionId, out activity)) return true;

            return false;
        }

        private static bool TryFindCatalogActivity(List<DoomsAgentT4Brain.Activity> list, string activityName, out DoomsAgentT4Brain.Activity activity)
        {
            activity = null;
            if (list == null || string.IsNullOrEmpty(activityName)) return false;

            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a == null) continue;
                if (!string.Equals(a.activityName, activityName, StringComparison.OrdinalIgnoreCase)) continue;
                activity = a;
                return true;
            }

            return false;
        }

        private void PublishEncounterAmbientInfluence(EncounterSession session)
        {
            if (session == null || session.tagA == null || session.tagB == null) return;
            if (!TryResolveActivityDefinition(session, out var activity) || activity == null) return;
            if (string.IsNullOrEmpty(activity.hostilityTag)) return;

            var profile = AmbientMoodProfileSO.Instance;
            var extras = ExtrasProfileSO.Instance;
            float baseRadius = profile != null ? Mathf.Max(2f, profile.baseInfluenceRadius) : 8f;
            float infectiousBoost = extras != null ? Mathf.Clamp01(extras.infectiousJoinChance) : 0f;
            int infectiousCap = extras != null ? Mathf.Max(1, extras.maxInfectiousParticipants) : 1;
            float capBoost = Mathf.Lerp(1f, 1.5f, Mathf.Clamp01((infectiousCap - 1) / 4f));
            float radius = activity.infectious ? baseRadius * (1f + infectiousBoost) * capBoost : baseRadius * 0.7f;
            float intensity = activity.infectious ? 0.95f : 0.55f;

            Vector3 center = (session.tagA.transform.position + session.tagB.transform.position) * 0.5f;
            string factionA = DoomsFactionRuntime.EffectiveFactionOf(session.tagA);
            string factionB = DoomsFactionRuntime.EffectiveFactionOf(session.tagB);
            string[] factions = new[] { factionA, factionB };

            AmbientMoodBoard.InjectLocalTag(activity.hostilityTag, center, radius, intensity, Mathf.Clamp(session.holdSeconds, 2f, 8f), factions);
        }

        private void ApplyPersonaOutcomeDrift(EncounterSession session)
        {
            if (session == null || session.tagA == null || session.tagB == null) return;

            var personaA = session.tagA.GetComponent<DoomsAgentPersona>();
            var personaB = session.tagB.GetComponent<DoomsAgentPersona>();
            string factionA = DoomsFactionRuntime.EffectiveFactionOf(session.tagA);
            string factionB = DoomsFactionRuntime.EffectiveFactionOf(session.tagB);
            if (string.IsNullOrEmpty(factionA) || string.IsNullOrEmpty(factionB)) return;

            if (string.Equals(session.actionId, "Talk", StringComparison.OrdinalIgnoreCase))
            {
                float sociabilityA = personaA != null ? personaA.sociability : 0.5f;
                float sociabilityB = personaB != null ? personaB.sociability : 0.5f;
                if (personaA != null) personaA.Nudge(factionB, 0.02f * sociabilityA);
                if (personaB != null) personaB.Nudge(factionA, 0.02f * sociabilityB);
                return;
            }

            if (string.Equals(session.actionId, "Fight", StringComparison.OrdinalIgnoreCase)
                || string.Equals(session.actionId, "Flee", StringComparison.OrdinalIgnoreCase))
            {
                float aggressionA = personaA != null ? personaA.aggression : 0.5f;
                float aggressionB = personaB != null ? personaB.aggression : 0.5f;
                if (personaA != null) personaA.Nudge(factionB, -0.025f * (1f - aggressionA));
                if (personaB != null) personaB.Nudge(factionA, -0.025f * (1f - aggressionB));
            }
        }

        private bool TryResolveMeetingPoint(AreaAnchor area, Vector3 posA, Vector3 posB, out Vector3 result)
        {
            // Meet at the two agents' midpoint — they are already within scan range, so
            // this is a small adjustment, not a teleport. (Previously this used
            // area.GetRandomPointWithin(), which flung both agents to a random spot
            // across the area = the instant cross-scene teleport.)
            Vector3 desired = (posA + posB) * 0.5f;

            if (TryProjectToNavMesh(desired, out result))
            {
                return true;
            }

            WarnNavIssue("could not project meeting point to NavMesh");
            result = desired;
            return false;
        }

        private bool TryProjectToNavMesh(Vector3 point, out Vector3 projected)
        {
            if (NavMesh.SamplePosition(point, out var hit, navSampleRadius, NavMesh.AllAreas))
            {
                projected = hit.position;
                return true;
            }

            projected = point;
            return false;
        }

        private Vector3 ProjectToNavMesh(Vector3 point)
        {
            if (TryProjectToNavMesh(point, out var projected))
            {
                return projected;
            }

            return point;
        }

        private void WarnNavIssue(string reason)
        {
            if (Time.time - _lastNavWarnTime < 10f) return;
            _lastNavWarnTime = Time.time;
            Debug.LogWarning($"[DOOMS][T4EncounterResolver] '{gameObject.name}' {reason}.");
        }

        private string GetActiveSceneId()
        {
            var director = FindFirstObjectByType<SceneDirector>();
            if (director == null || director.CurrentContext == null || director.CurrentContext.scene == null)
            {
                return "";
            }

            return director.CurrentContext.scene.sceneId ?? "";
        }

        private static string GetPairKey(string agentAId, string agentBId)
        {
            if (string.Compare(agentAId, agentBId, StringComparison.OrdinalIgnoreCase) <= 0)
            {
                return agentAId + "||" + agentBId;
            }

            return agentBId + "||" + agentAId;
        }
    }
}
