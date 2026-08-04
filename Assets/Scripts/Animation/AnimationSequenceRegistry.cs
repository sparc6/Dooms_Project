using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM
{
    [Serializable]
    public class SequenceStep
    {
        public string stateName = "";
        [RegistryDropdown(RegistryType.Prop)]
        public string propId = "";
    }

    // A single point on a blendtree's driving float: at normalized time time01
    // (0..1 of the action's hold duration) the blend parameter should read value.
    [Serializable]
    public class BlendKey
    {
        [Range(0f, 1f)] public float time01 = 0f;
        public float value = 0f;
    }

    [Serializable]
    public class ActionAnimSequence
    {
        public string sequenceId = "";

        [Header("Animator Controller Routing (optional)")]
        [Tooltip("ActionId used by controllers that expose the shared ActionId/ActionRequest/ActionActive protocol. Zero keeps the legacy direct-state playback path.")]
        [Min(0)] public int controllerActionId = 0;
        [Tooltip("ActionVariant value supplied to the Animator when this sequence starts.")]
        [Min(0)] public int controllerActionVariant = 0;
        [Tooltip("Seconds reserved at the end of the total holdSeconds window for the authored End state. Ignored when the sequence has no End state.")]
        [Min(0f)] public float controllerEndLeadSeconds = 0.75f;

        public string startState = "";
        [RegistryDropdown(RegistryType.Prop)]
        public string startPropId = "";
        public string loopState = "";
        [RegistryDropdown(RegistryType.Prop)]
        public string loopPropId = "";
        public string endState = "";
        [RegistryDropdown(RegistryType.Prop)]
        public string endPropId = "";
        public List<SequenceStep> holdSteps = new List<SequenceStep>();
        public float startCrossfade = 0.15f;
        public float endCrossfade = 0.15f;

        [Header("Blend Tree mode (optional)")]
        [Tooltip("If true, this 'sequence' is played as a single blendtree state whose float parameter is driven through blendKeys over the hold duration (e.g. aim->shoot->settle). Leave off for the classic Enter/Loop/Exit discrete-state behavior.")]
        public bool useBlendTree = false;
        [Tooltip("Animator state that contains the blend tree.")]
        public string blendTreeState = "";
        [Tooltip("Float animator parameter that drives the blend tree.")]
        public string blendParam = "";
        [RegistryDropdown(RegistryType.Prop)]
        [Tooltip("Prop held for the duration of the blendtree action (e.g. gun for a shoot blend).")]
        public string blendPropId = "";
        [Tooltip("Keyframes for the driving float, ordered by time01 (0..1 of the hold duration).")]
        public List<BlendKey> blendKeys = new List<BlendKey>();

        public bool HasHoldSteps => holdSteps != null && holdSteps.Count > 0;

        public List<SequenceStep> GetHoldSteps()
        {
            if (holdSteps != null && holdSteps.Count > 0)
            {
                return holdSteps;
            }

            if (!string.IsNullOrEmpty(loopState))
            {
                return new List<SequenceStep>
                {
                    new SequenceStep { stateName = loopState }
                };
            }

            return new List<SequenceStep>();
        }

        public bool TryGetStepForState(string stateName, out SequenceStep step)
        {
            step = null;
            if (string.IsNullOrEmpty(stateName)) return false;

            // Blendtree mode: the single blend state carries the loadout/action prop,
            // so the existing AnimatorPropDriver state-prop path spawns it on entry.
            if (useBlendTree && !string.IsNullOrEmpty(blendTreeState)
                && string.Equals(blendTreeState, stateName, StringComparison.OrdinalIgnoreCase))
            {
                step = new SequenceStep { stateName = blendTreeState, propId = blendPropId };
                return true;
            }

            if (!string.IsNullOrEmpty(startState) && string.Equals(startState, stateName, StringComparison.OrdinalIgnoreCase))
            {
                step = new SequenceStep { stateName = startState, propId = startPropId };
                return true;
            }

            if (holdSteps != null)
            {
                for (int i = 0; i < holdSteps.Count; i++)
                {
                    var candidate = holdSteps[i];
                    if (candidate != null && !string.IsNullOrEmpty(candidate.stateName) && string.Equals(candidate.stateName, stateName, StringComparison.OrdinalIgnoreCase))
                    {
                        step = candidate;
                        return true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(loopState) && string.Equals(loopState, stateName, StringComparison.OrdinalIgnoreCase))
            {
                step = new SequenceStep { stateName = loopState, propId = loopPropId };
                return true;
            }

            if (!string.IsNullOrEmpty(endState) && string.Equals(endState, stateName, StringComparison.OrdinalIgnoreCase))
            {
                step = new SequenceStep { stateName = endState, propId = endPropId };
                return true;
            }

            return false;
        }
    }

    [CreateAssetMenu(fileName = "AnimationSequenceRegistry", menuName = "DOOMS/Animation Sequence Registry")]
    public class AnimationSequenceRegistry : ScriptableObject
    {
        private static AnimationSequenceRegistry _instance;
        public static AnimationSequenceRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<AnimationSequenceRegistry>("Dooms/AnimationSequenceRegistry");
                    if (_instance == null)
                    {
                        var found = Resources.FindObjectsOfTypeAll<AnimationSequenceRegistry>();
                        if (found != null && found.Length > 0)
                        {
                            _instance = found[0];
                        }
                    }
                }
                return _instance;
            }
        }

        public List<ActionAnimSequence> sequences = new List<ActionAnimSequence>();

        public ActionAnimSequence FindSequence(string sequenceId)
        {
            if (string.IsNullOrEmpty(sequenceId)) return null;
            return sequences.Find(s => string.Equals(s.sequenceId, sequenceId, StringComparison.OrdinalIgnoreCase));
        }

        public ActionAnimSequence FindSequenceContainingState(string stateName)
        {
            if (string.IsNullOrEmpty(stateName) || sequences == null) return null;

            for (int i = 0; i < sequences.Count; i++)
            {
                var sequence = sequences[i];
                if (sequence == null) continue;
                if (sequence.TryGetStepForState(stateName, out _))
                {
                    return sequence;
                }
            }

            return null;
        }
    }
}
