using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace MLA_SIM.Dooms
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayableDirector))]
    [AddComponentMenu("DOOMS/Timeline Anchor")]
    public class TimelineAnchor : MonoBehaviour
    {
        [System.Serializable]
        public class TimelineSlot
        {
            public string slotId = "";
            public Transform anchorTransform;
            [Tooltip("Name of the timeline track to bind this agent's Animator to (optional).")]
            public string animatorBindingName = "";

            [HideInInspector] public string occupantAgentId = "";
            public bool IsOccupied => !string.IsNullOrEmpty(occupantAgentId);
        }

        [Tooltip("Unique ID matching role declarations in JSON scene configuration.")]
        public string timelineAnchorId = "";

        [Tooltip("Direct reference to the PlayableDirector on this object.")]
        public PlayableDirector playableDirector;

        [Tooltip("Defined actor slots for this choreographed timeline.")]
        public List<TimelineSlot> slots = new List<TimelineSlot>();

        private void Awake()
        {
            if (playableDirector == null)
                playableDirector = GetComponent<PlayableDirector>();
            
            if (playableDirector != null)
                playableDirector.playOnAwake = false;
        }

        public bool TryOccupySlot(string slotId, string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return false;
            
            // Release from any other slots in this timeline first
            Release(agentId);

            var slot = slots.Find(s => string.Equals(s.slotId, slotId, StringComparison.OrdinalIgnoreCase));
            if (slot == null || slot.IsOccupied) return false;

            slot.occupantAgentId = agentId;
            return true;
        }

        public void Release(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return;
            foreach (var slot in slots)
            {
                if (string.Equals(slot.occupantAgentId, agentId, StringComparison.OrdinalIgnoreCase))
                {
                    slot.occupantAgentId = "";
                }
            }
        }

        public TimelineSlot GetSlotForAgent(string agentId)
        {
            if (string.IsNullOrEmpty(agentId)) return null;
            return slots.Find(s => string.Equals(s.occupantAgentId, agentId, StringComparison.OrdinalIgnoreCase));
        }

        public TimelineSlot FindSlot(string slotId)
        {
            return slots.Find(s => string.Equals(s.slotId, slotId, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsFullyOccupied()
        {
            // If any slot is empty, we are not fully occupied
            foreach (var slot in slots)
            {
                if (!slot.IsOccupied) return false;
            }
            return true;
        }

        public bool IsPlaying => playableDirector != null && playableDirector.state == PlayState.Playing;
        public bool IsComplete => playableDirector == null || playableDirector.state != PlayState.Playing;

        public void Play()
        {
            if (playableDirector == null) return;

            // Optional: Bind animators dynamically to timeline tracks if animatorBindingName is configured
            BindAnimators();

            playableDirector.Play();
            Debug.Log($"[DOOMS][TimelineAnchor] Play called on timeline '{timelineAnchorId}' with {slots.Count} actors.");
        }

        private void BindAnimators()
        {
            if (playableDirector == null || playableDirector.playableAsset == null) return;

            foreach (var slot in slots)
            {
                if (!slot.IsOccupied || string.IsNullOrEmpty(slot.animatorBindingName)) continue;

                // Find occupant's Animator
                var occupantObj = FindOccupantGameObject(slot.occupantAgentId);
                if (occupantObj == null) continue;

                var anim = occupantObj.GetComponent<Animator>();
                if (anim == null) continue;

                // Attempt dynamic binding on PlayableDirector
                foreach (var output in playableDirector.playableAsset.outputs)
                {
                    if (string.Equals(output.streamName, slot.animatorBindingName, StringComparison.OrdinalIgnoreCase))
                    {
                        playableDirector.SetGenericBinding(output.sourceObject, anim);
                        Debug.Log($"[DOOMS][TimelineAnchor] Dynamically bound Animator on '{slot.occupantAgentId}' to track '{slot.animatorBindingName}'.");
                        break;
                    }
                }
            }
        }

        private GameObject FindOccupantGameObject(string agentId)
        {
            var tags = FindObjectsByType<DoomsAgentTag>(FindObjectsSortMode.None);
            foreach (var tag in tags)
            {
                if (tag != null && string.Equals(tag.agentId, agentId, StringComparison.OrdinalIgnoreCase))
                {
                    return tag.gameObject;
                }
            }
            return null;
        }

        private void OnEnable()
        {
            TimelineAnchorIndex.Register(this);
        }

        private void OnDisable()
        {
            TimelineAnchorIndex.Unregister(this);
        }
    }
}
