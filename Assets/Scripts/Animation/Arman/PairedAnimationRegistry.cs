using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM
{
    [Serializable]
    public sealed class PairedAnimationDefinition
    {
        public string actionId = "";

        [Header("Role Sequences")]
        [RegistryDropdown(RegistryType.AnimationSequence)]
        public string maleSequenceId = "";
        [RegistryDropdown(RegistryType.AnimationSequence)]
        public string femaleSequenceId = "";

        [Header("Timing")]
        [Min(0.1f)] public float holdSeconds = 4f;
    }

    [CreateAssetMenu(fileName = "PairedAnimationRegistry", menuName = "DOOMS/Animation/Paired Animation Registry")]
    public sealed class PairedAnimationRegistry : ScriptableObject
    {
        private static PairedAnimationRegistry _instance;

        public static PairedAnimationRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<PairedAnimationRegistry>("Dooms/PairedAnimationRegistry");
                }
                return _instance;
            }
        }

        public List<PairedAnimationDefinition> actions = new List<PairedAnimationDefinition>();

        public PairedAnimationDefinition Find(string actionId)
        {
            if (string.IsNullOrEmpty(actionId) || actions == null) return null;
            return actions.Find(a => a != null && string.Equals(
                a.actionId,
                actionId,
                StringComparison.OrdinalIgnoreCase));
        }

        public bool ContainsSequence(string sequenceId)
        {
            if (string.IsNullOrEmpty(sequenceId) || actions == null) return false;
            return actions.Exists(a => a != null
                && (string.Equals(a.maleSequenceId, sequenceId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a.femaleSequenceId, sequenceId, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
