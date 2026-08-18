using System.Collections;
using System.Collections.Generic;
using MLA_SIM.Dooms;
using UnityEngine;

namespace MLA_SIM
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AnimatorLocomotionDriver))]
    [AddComponentMenu("DOOMS/Animation/Animator Action Preview")]
    public sealed class AnimatorActionPreview : MonoBehaviour
    {
        [System.Serializable]
        public class PreviewStep
        {
            [RegistryDropdown(RegistryType.AnimationSequence)]
            public string sequenceId = "";
            [Min(0.1f)] public float holdSeconds = 5f;
        }

        [Header("Preview Playback")]
        public bool playOnStart = true;
        public bool loop = true;
        public bool pauseT4Brain = true;
        [Min(0f)] public float startDelay = 1f;
        [Min(0f)] public float transitionPause = 1f;

        [Header("Optional Action Anchor")]
        [Tooltip("When assigned, the preview aligns the character to this transform before each action begins.")]
        public Transform actionAnchor;
        public bool alignToAnchorBeforePlayback = false;

        public List<PreviewStep> playlist = new List<PreviewStep>
        {
            new PreviewStep { sequenceId = "DoomScroll", holdSeconds = 6f },
            new PreviewStep { sequenceId = "PhoneCall", holdSeconds = 6f },
            new PreviewStep { sequenceId = "Eating", holdSeconds = 6f },
            new PreviewStep { sequenceId = "Catatonic", holdSeconds = 6f },
            new PreviewStep { sequenceId = "Sit", holdSeconds = 6f },
            new PreviewStep { sequenceId = "Farming", holdSeconds = 6f },
            new PreviewStep { sequenceId = "Fishing", holdSeconds = 8f },
            new PreviewStep { sequenceId = "Single_Action", holdSeconds = 4f },
            new PreviewStep { sequenceId = "ShootBlend", holdSeconds = 5f },
        };

        private AnimatorLocomotionDriver _driver;
        private DoomsAgentT4Brain _brain;
        private Coroutine _previewRoutine;
        private bool _brainWasEnabled;
        private bool _brainPausedByPreview;

        private void Awake()
        {
            _driver = GetComponent<AnimatorLocomotionDriver>();
            _brain = GetComponent<DoomsAgentT4Brain>();
        }

        private void Start()
        {
            if (playOnStart)
            {
                PlayPreview();
            }
        }

        public void PlayPreview()
        {
            StopPreview(false);

            if (_driver == null || playlist == null || playlist.Count == 0)
            {
                return;
            }

            if (pauseT4Brain && _brain != null && !_brainPausedByPreview)
            {
                _brainWasEnabled = _brain.enabled;
                _brain.enabled = false;
                _brainPausedByPreview = true;
            }

            _previewRoutine = StartCoroutine(PlayPlaylist());
        }

        public void StopPreview()
        {
            StopPreview(true);
        }

        private void StopPreview(bool restoreBrain)
        {
            if (_previewRoutine != null)
            {
                StopCoroutine(_previewRoutine);
                _previewRoutine = null;
            }

            if (_driver != null)
            {
                _driver.StopActionPlayback(true);
            }

            if (restoreBrain && _brainPausedByPreview)
            {
                if (_brain != null && _brainWasEnabled)
                {
                    _brain.enabled = true;
                }
                _brainWasEnabled = false;
                _brainPausedByPreview = false;
            }
        }

        private IEnumerator PlayPlaylist()
        {
            if (startDelay > 0f)
            {
                yield return new WaitForSeconds(startDelay);
            }

            do
            {
                for (int i = 0; i < playlist.Count; i++)
                {
                    var step = playlist[i];
                    if (step == null || string.IsNullOrEmpty(step.sequenceId))
                    {
                        continue;
                    }

                    float duration = Mathf.Max(0.1f, step.holdSeconds);
                    AlignToActionAnchor();
                    if (_driver.PlayActionSequence(step.sequenceId, duration))
                    {
                        yield return new WaitForSeconds(duration);
                    }

                    if (transitionPause > 0f)
                    {
                        yield return new WaitForSeconds(transitionPause);
                    }
                }
            }
            while (loop);

            _previewRoutine = null;
            if (_brainPausedByPreview)
            {
                if (_brain != null && _brainWasEnabled)
                {
                    _brain.enabled = true;
                }
                _brainWasEnabled = false;
                _brainPausedByPreview = false;
            }
        }

        private void AlignToActionAnchor()
        {
            if (!alignToAnchorBeforePlayback || actionAnchor == null)
            {
                return;
            }

            transform.SetPositionAndRotation(actionAnchor.position, actionAnchor.rotation);
        }

        private void OnDisable()
        {
            StopPreview(true);
        }
    }
}
