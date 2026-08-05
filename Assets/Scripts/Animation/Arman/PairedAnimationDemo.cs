using System.Collections;
using System.Collections.Generic;
using MLA_SIM.Dooms;
using UnityEngine;
using UnityEngine.AI;

namespace MLA_SIM
{
    [System.Serializable]
    public sealed class PairedAnimationAnchorSet
    {
        [Tooltip("Must match an actionId in the paired animation registry.")]
        public string actionId = "";

        [Tooltip("Exact world pose used by the male participant when this action starts.")]
        public Transform maleAnchor;

        [Tooltip("Exact world pose used by the female participant when this action starts.")]
        public Transform femaleAnchor;
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("DOOMS/Animation/Paired Animation Demo")]
    public sealed class PairedAnimationDemo : MonoBehaviour
    {
        [Header("Participants")]
        public PairedAnimationParticipant maleParticipant;
        public PairedAnimationParticipant femaleParticipant;

        [Header("Scene Anchors")]
        [Tooltip("Per-action scene transforms. Their Inspector position and rotation are used exactly and are never overwritten at runtime.")]
        public List<PairedAnimationAnchorSet> actionAnchors = new List<PairedAnimationAnchorSet>();

        [Header("Playback")]
        public bool playOnStart = true;
        public bool loop = true;
        [Min(0f)] public float startDelay = 1f;
        [Min(0f)] public float pauseBetweenActions = 2f;
        public List<string> actionPlaylist = new List<string> { "Kiss", "Hug" };

        [Header("Approach")]
        [Min(0.05f)] public float arrivalDistance = 0.2f;
        [Min(1f)] public float approachTimeout = 15f;
        public bool exactSettleBeforePlayback = true;
        public bool pauseAgentBrains = true;

        private Coroutine _routine;
        private readonly List<BehaviourState> _pausedBehaviours = new List<BehaviourState>();
        private static readonly List<PairedAnimationDemo> ActiveDemos = new List<PairedAnimationDemo>();

        private struct BehaviourState
        {
            public Behaviour behaviour;
            public bool wasEnabled;
        }

        private void Start()
        {
            if (playOnStart)
            {
                PlayDemo();
            }
        }

        public void PlayDemo()
        {
            StopDemo(true);
            if (!ValidateSetup()) return;

            ClaimParticipants();
            PauseParticipantLogic(maleParticipant);
            PauseParticipantLogic(femaleParticipant);
            _routine = StartCoroutine(RunPlaylist());
        }

        public void StopDemo()
        {
            StopDemo(true);
        }

        private void StopDemo(bool restoreParticipants)
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            StopParticipant(maleParticipant, true);
            StopParticipant(femaleParticipant, true);

            if (restoreParticipants)
            {
                RestoreParticipantLogic();
            }

            ActiveDemos.Remove(this);
        }

        private IEnumerator RunPlaylist()
        {
            if (startDelay > 0f)
            {
                yield return new WaitForSeconds(startDelay);
            }

            do
            {
                for (int i = 0; i < actionPlaylist.Count; i++)
                {
                    string actionId = actionPlaylist[i];
                    var definition = PairedAnimationRegistry.Instance != null
                        ? PairedAnimationRegistry.Instance.Find(actionId)
                        : null;

                    if (definition == null)
                    {
                        Debug.LogWarning($"[PairedAnimationDemo] No paired definition found for '{actionId}'.", this);
                        continue;
                    }

                    PairedAnimationAnchorSet anchors = FindAnchors(actionId);
                    if (anchors == null || anchors.maleAnchor == null || anchors.femaleAnchor == null)
                    {
                        Debug.LogWarning($"[PairedAnimationDemo] No complete scene anchor set found for '{actionId}'.", this);
                        continue;
                    }

                    yield return MoveBothParticipantsToAnchors(anchors.maleAnchor, anchors.femaleAnchor);

                    yield return AlignAndPauseParticipants(anchors.maleAnchor, anchors.femaleAnchor);

                    float hold = Mathf.Max(0.1f, definition.holdSeconds);
                    bool maleStarted = maleParticipant.Driver.PlayActionSequence(definition.maleSequenceId, hold);
                    bool femaleStarted = femaleParticipant.Driver.PlayActionSequence(definition.femaleSequenceId, hold);

                    if (!maleStarted || !femaleStarted)
                    {
                        Debug.LogWarning(
                            $"[PairedAnimationDemo] '{actionId}' failed to start. Male={maleStarted}, Female={femaleStarted}.",
                            this);
                    }
                    else
                    {
                        Debug.Log($"[PairedAnimationDemo] Started '{actionId}' for both participants.", this);
                    }

                    yield return new WaitForSeconds(hold);

                    if (pauseBetweenActions > 0f)
                    {
                        yield return new WaitForSeconds(pauseBetweenActions);
                    }
                }
            }
            while (loop);

            _routine = null;
            RestoreParticipantLogic();
            ActiveDemos.Remove(this);
        }

        private bool ValidateSetup()
        {
            if (maleParticipant == null || femaleParticipant == null)
            {
                Debug.LogError("[PairedAnimationDemo] Both participants must be assigned.", this);
                return false;
            }

            if (actionPlaylist == null || actionPlaylist.Count == 0)
            {
                Debug.LogError("[PairedAnimationDemo] The action playlist is empty.", this);
                return false;
            }

            maleParticipant.CacheComponents();
            femaleParticipant.CacheComponents();
            if (maleParticipant.Driver == null || femaleParticipant.Driver == null)
            {
                Debug.LogError("[PairedAnimationDemo] Both participants require AnimatorLocomotionDriver.", this);
                return false;
            }

            if (maleParticipant.Animator != null) maleParticipant.Animator.applyRootMotion = false;
            if (femaleParticipant.Animator != null) femaleParticipant.Animator.applyRootMotion = false;
            return true;
        }

        private PairedAnimationAnchorSet FindAnchors(string actionId)
        {
            if (actionAnchors == null || string.IsNullOrEmpty(actionId)) return null;
            return actionAnchors.Find(a => a != null && string.Equals(
                a.actionId,
                actionId,
                System.StringComparison.OrdinalIgnoreCase));
        }

        private IEnumerator MoveBothParticipantsToAnchors(Transform maleAnchor, Transform femaleAnchor)
        {
            BeginApproach(maleParticipant, maleAnchor.position);
            BeginApproach(femaleParticipant, femaleAnchor.position);

            float elapsed = 0f;
            bool maleReady = false;
            bool femaleReady = false;
            while (elapsed < approachTimeout && (!maleReady || !femaleReady))
            {
                UpdatePairedApproach(maleParticipant, maleAnchor);
                UpdatePairedApproach(femaleParticipant, femaleAnchor);

                maleReady = HasArrived(maleParticipant, maleAnchor.position);
                femaleReady = HasArrived(femaleParticipant, femaleAnchor.position);

                if (maleReady) PauseNavigation(maleParticipant);
                if (femaleReady) PauseNavigation(femaleParticipant);

                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private static void UpdatePairedApproach(PairedAnimationParticipant participant, Transform anchor)
        {
            if (participant != null && participant.Driver != null && anchor != null)
            {
                participant.Driver.UpdatePairedInteractionApproach(anchor.position);
                participant.Driver.UpdateAnchorApproach(anchor);
            }
        }

        private static void BeginApproach(PairedAnimationParticipant participant, Vector3 destination)
        {
            if (participant == null) return;
            participant.Driver.StopActionPlayback(true);

            NavMeshAgent nav = participant.NavAgent;
            if (nav == null || !nav.enabled || !nav.isOnNavMesh) return;
            nav.isStopped = false;
            nav.SetDestination(destination);
        }

        private bool HasArrived(PairedAnimationParticipant participant, Vector3 destination)
        {
            if (participant == null) return false;
            NavMeshAgent nav = participant.NavAgent;
            if (nav == null || !nav.enabled || !nav.isOnNavMesh)
            {
                Vector3 delta = destination - participant.transform.position;
                delta.y = 0f;
                return delta.sqrMagnitude <= arrivalDistance * arrivalDistance;
            }

            if (nav.pathPending) return false;
            float threshold = Mathf.Max(arrivalDistance, nav.stoppingDistance + 0.05f);
            return nav.remainingDistance <= threshold;
        }

        private IEnumerator AlignAndPauseParticipants(Transform maleAnchor, Transform femaleAnchor)
        {
            if (maleParticipant == null || femaleParticipant == null) yield break;

            PauseNavigation(maleParticipant);
            PauseNavigation(femaleParticipant);

            if (exactSettleBeforePlayback)
            {
                maleParticipant.Driver.BeginAnchorAlignment(maleAnchor);
                femaleParticipant.Driver.BeginAnchorAlignment(femaleAnchor);

                while (maleParticipant.Driver.IsAnchorAlignmentActive
                    || femaleParticipant.Driver.IsAnchorAlignmentActive)
                {
                    yield return null;
                }
            }
            else
            {
                maleParticipant.transform.rotation = maleAnchor.rotation;
                femaleParticipant.transform.rotation = femaleAnchor.rotation;
            }
        }

        private static void PauseNavigation(PairedAnimationParticipant participant)
        {
            NavMeshAgent nav = participant != null ? participant.NavAgent : null;
            if (nav == null || !nav.enabled || !nav.isOnNavMesh) return;
            nav.ResetPath();
            nav.isStopped = true;
        }

        private void PauseParticipantLogic(PairedAnimationParticipant participant)
        {
            if (!pauseAgentBrains || participant == null) return;
            PauseBehaviour(participant.GetComponent<DoomsAgentT4Brain>());
            PauseBehaviour(participant.GetComponent<T4EncounterResolver>());
        }

        private void PauseBehaviour(Behaviour behaviour)
        {
            if (behaviour == null) return;
            _pausedBehaviours.Add(new BehaviourState { behaviour = behaviour, wasEnabled = behaviour.enabled });
            if (behaviour is MonoBehaviour monoBehaviour)
            {
                monoBehaviour.StopAllCoroutines();
            }
            behaviour.enabled = false;
        }

        private void ClaimParticipants()
        {
            for (int i = ActiveDemos.Count - 1; i >= 0; i--)
            {
                PairedAnimationDemo other = ActiveDemos[i];
                if (other == null)
                {
                    ActiveDemos.RemoveAt(i);
                    continue;
                }

                if (other != this && SharesParticipantWith(other))
                {
                    other.StopDemo(true);
                }
            }

            if (!ActiveDemos.Contains(this)) ActiveDemos.Add(this);
        }

        private bool SharesParticipantWith(PairedAnimationDemo other)
        {
            return maleParticipant == other.maleParticipant
                || maleParticipant == other.femaleParticipant
                || femaleParticipant == other.maleParticipant
                || femaleParticipant == other.femaleParticipant;
        }

        private void RestoreParticipantLogic()
        {
            for (int i = 0; i < _pausedBehaviours.Count; i++)
            {
                var state = _pausedBehaviours[i];
                if (state.behaviour != null) state.behaviour.enabled = state.wasEnabled;
            }
            _pausedBehaviours.Clear();
        }

        private static void StopParticipant(PairedAnimationParticipant participant, bool returnToLocomotion)
        {
            if (participant == null) return;
            if (participant.Driver != null) participant.Driver.StopActionPlayback(returnToLocomotion);
            PauseNavigation(participant);
        }

        private void OnDisable()
        {
            StopDemo(true);
        }
    }
}
