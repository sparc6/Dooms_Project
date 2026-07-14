using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Coarse ambient mood classification for a live DOOMS scene. Drives how
    /// UNASSIGNED T4 agents behave nearby so the world stays coherent (e.g. no
    /// casual loitering during a violent confrontation).
    /// </summary>
    public enum SceneMood { Calm, Tense, Hostile }

    [Serializable]
    public class MoodActivityWeight
    {
        [Tooltip("Matches DoomsAgentT4Brain.Activity.activityName (case-insensitive).")]
        [ActivityNameDropdown]
        public string activityName = "";
        [Tooltip("Score multiplier applied to that activity while this mood is active. 0 disables it.")]
        public float multiplier = 1f;
    }

    /// <summary>
    /// Shared, inspector-authored tuning for the ambient/coherence layer
    /// (Options B + C). Read-only at runtime; never mutated by agents.
    /// Create one asset and place it under a Resources/Dooms/ folder named
    /// "AmbientMoodProfile" so the singleton can auto-load it, or assign it
    /// however your bootstrap prefers. If none exists, agents fall back to
    /// neutral behaviour (multiplier 1, no reactions).
    /// </summary>
    [CreateAssetMenu(fileName = "AmbientMoodProfile", menuName = "DOOMS/Ambient Mood Profile")]
    public class AmbientMoodProfileSO : ScriptableObject
    {
        private static AmbientMoodProfileSO _instance;
        public static AmbientMoodProfileSO Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<AmbientMoodProfileSO>("Dooms/AmbientMoodProfile");
                    if (_instance == null)
                    {
                        var all = Resources.FindObjectsOfTypeAll<AmbientMoodProfileSO>();
                        if (all.Length > 0) _instance = all[0];
                    }
                }
                return _instance;
            }
        }

        [Header("Scene tag -> mood (case-insensitive substring match against SceneSO.tags)")]
        [Tooltip("Source of truth for mood-tag vocabulary used by the ambient layer. Other assets (e.g., ActivityCatalog hostilityTag) should reference this list via dropdowns.")]
        public List<string> hostileTags = new List<string> { "violent", "aggressive", "confront", "riot", "hostile", "attack", "panic" };
        public List<string> tenseTags = new List<string> { "tense", "protest", "standoff", "alert", "suspense" };
        // Any scene whose tags match neither list is classified Calm.

        [Tooltip("If a Tense scene's intensity is >= this value, promote it to Hostile.")]
        [Range(0f, 1f)] public float tenseToHostileIntensity = 0.75f;

        [Header("Activity score multipliers per mood (by Activity.activityName)")]
        public List<MoodActivityWeight> calmWeights = new List<MoodActivityWeight>();
        public List<MoodActivityWeight> tenseWeights = new List<MoodActivityWeight>
        {
            new MoodActivityWeight { activityName = "Talk", multiplier = 0.4f }
        };
        public List<MoodActivityWeight> hostileWeights = new List<MoodActivityWeight>
        {
            new MoodActivityWeight { activityName = "Talk",  multiplier = 0.05f },
            new MoodActivityWeight { activityName = "Eat",   multiplier = 0.2f },
            new MoodActivityWeight { activityName = "Sleep", multiplier = 0.1f }
        };

        [Header("Proximity influence (Option C)")]
        [Tooltip("Base radius (m) of a scene's influence around its epicenter. SceneDirector widens it by how spread out the assigned actors are.")]
        public float baseInfluenceRadius = 12f;
        [Tooltip("Agents this far (m) BEYOND the influence radius are unaffected; reactions fade linearly across this band.")]
        public float reactionFalloff = 4f;

        [Header("Flee reaction (hostile relation, inside influence)")]
        [RegistryDropdown(RegistryType.AnimationState)]
        public string fleeAnimatorState = "Walking";
        [Tooltip("Locomotion blend-tree style index passed to AnimatorLocomotionDriver.SetLocoStyle while fleeing. -1 = leave unchanged.")]
        public int fleeLocoStyle = 1;
        [Tooltip("How far (m) past the influence edge a fleeing agent tries to reach.")]
        public float fleeDistance = 8f;

        [Header("Watch reaction (allied / neutral, inside influence)")]
        [RegistryDropdown(RegistryType.AnimationState)]
        public string watchAnimatorState = "Idle";
        [Tooltip("Seconds an onlooker watches before re-evaluating.")]
        public float watchMinSec = 3f;
        public float watchMaxSec = 7f;
        [Tooltip("How far (m) inside the influence edge an onlooker positions itself.")]
        public float watchStandoff = 2f;

        public float GetActivityMultiplier(SceneMood mood, string activityName)
        {
            var list = mood == SceneMood.Hostile ? hostileWeights
                     : mood == SceneMood.Tense ? tenseWeights
                     : calmWeights;
            if (list != null && !string.IsNullOrEmpty(activityName))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null && string.Equals(list[i].activityName, activityName, StringComparison.OrdinalIgnoreCase))
                        return Mathf.Max(0f, list[i].multiplier);
                }
            }
            return 1f;
        }

        public SceneMood ClassifyMood(IEnumerable<string> tags, float intensity)
        {
            bool tense = false;
            if (tags != null)
            {
                foreach (var raw in tags)
                {
                    if (string.IsNullOrEmpty(raw)) continue;
                    string t = raw.ToLowerInvariant();
                    if (MatchesAny(t, hostileTags)) return SceneMood.Hostile;
                    if (MatchesAny(t, tenseTags)) tense = true;
                }
            }
            if (tense)
                return intensity >= tenseToHostileIntensity ? SceneMood.Hostile : SceneMood.Tense;
            return SceneMood.Calm;
        }

        private static bool MatchesAny(string tagLower, List<string> keywords)
        {
            if (keywords == null) return false;
            for (int i = 0; i < keywords.Count; i++)
            {
                var k = keywords[i];
                if (!string.IsNullOrEmpty(k) && tagLower.Contains(k.ToLowerInvariant()))
                    return true;
            }
            return false;
        }
    }
}
