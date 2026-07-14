using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using MLA_SIM;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Optional combat capability + death handling for a DOOMS T4 agent.
    ///
    /// Two cohesive responsibilities:
    ///   - Capability: whether this agent can initiate a lethal (ranged) act,
    ///     at what range, and which animator states to play.
    ///   - Death: a terminal state entered via <see cref="Kill"/> — disables the
    ///     brain/encounter resolver, removes the agent from NavMesh avoidance,
    ///     plays a death clip, and leaves a corpse for a configurable time.
    ///
    /// Absent component or canInitiateLethal=false reproduces pre-combat behavior,
    /// so this is fully opt-in per prefab (e.g. only armed guards get it).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MLA_SIM/DOOMS/Dooms Agent Combat")]
    public class DoomsAgentCombat : MonoBehaviour
    {
        [Header("Capability")]
        [Tooltip("If true this agent can initiate a lethal (ranged) act when personal hostility crosses the threshold. Tie this to an armed loadout.")]
        public bool canInitiateLethal = false;
        [Tooltip("Standoff distance (m) the shooter holds from the target — ranged, no warp-together.")]
        public float weaponRange = 8f;
        [Tooltip("Blended hostility (persona + faction relation + aggression) at/above which a capable agent escalates to lethal.")]
        [Range(0f, 1f)] public float lethalHostilityThreshold = 0.6f;
        [Tooltip("Probability a shot kills outright; otherwise the target is wounded and flees.")]
        [Range(0f, 1f)] public float killProbability = 0.7f;

        [Header("Animation — blendtree sequence (preferred)")]
        [Tooltip("Animation sequence (blendtree-capable, prop-carrying) for the shooting action. Preferred over shootStateNames when set.")]
        [RegistryDropdown(RegistryType.AnimationSequence)]
        public string shootSequenceId = "";
        [Tooltip("Animation sequence for death/collapse. Preferred over deathStateNames when set. Held on the final pose.")]
        [RegistryDropdown(RegistryType.AnimationSequence)]
        public string deathSequenceId = "";

        [Header("Animation — fallback states")]
        [Tooltip("Fallback animator states for the shooting action if no shootSequenceId. One present state is chosen at random.")]
        public string[] shootStateNames = { "Shoot", "Aim" };
        [Tooltip("Fallback animator states for death/collapse if no deathSequenceId. One present state is chosen at random and held.")]
        public string[] deathStateNames = { "Death", "Collapse" };

        [Header("Corpse")]
        [Tooltip("Seconds the corpse remains before the GameObject is deactivated. <= 0 keeps the body indefinitely.")]
        public float corpseLingerSeconds = 20f;

        /// <summary>True once this agent has been killed. Other systems must skip dead agents.</summary>
        public bool IsDead { get; private set; }

        /// <summary>Agent id of whoever last killed this agent (for grudge / reporting).</summary>
        public string LastAttackerId { get; private set; } = "";

        private DoomsAgentTag _tag;
        private AnimatorLocomotionDriver _anim;
        private NavMeshAgent _nav;

        private void Awake()
        {
            _tag = GetComponent<DoomsAgentTag>();
            _anim = GetComponent<AnimatorLocomotionDriver>();
            _nav = GetComponent<NavMeshAgent>();
        }

        /// <summary>Can this agent currently initiate a lethal act?</summary>
        public bool CanShoot => canInitiateLethal && !IsDead && _anim != null;

        /// <summary>
        /// Enter the terminal death state. Idempotent. Disables the brain and
        /// encounter resolver, removes the agent from avoidance, plays a death
        /// clip, and schedules corpse cleanup. Violence reporting (mood + witness
        /// drift) is done by the caller via <see cref="DoomsViolence"/>.
        /// </summary>
        public void Kill(DoomsAgentTag attacker)
        {
            if (IsDead) return;
            IsDead = true;
            LastAttackerId = attacker != null ? attacker.agentId : "";

            // Disable behavior FIRST so their OnDisable cleanup (which crossfades
            // back to locomotion) runs before we play the death pose.
            var brain = GetComponent<DoomsAgentT4Brain>();
            if (brain != null) brain.enabled = false;

            var resolver = GetComponent<T4EncounterResolver>();
            if (resolver != null) resolver.enabled = false;

            // Remove from NavMesh avoidance so movers don't treat the corpse as a
            // live obstacle to negotiate with.
            if (_nav != null && _nav.enabled)
            {
                if (_nav.isOnNavMesh)
                {
                    _nav.isStopped = true;
                    _nav.ResetPath();
                }
                _nav.enabled = false;
            }

            // Hold the death pose (holdSeconds < 0 = no auto-return to locomotion).
            if (_anim != null)
            {
                if (!string.IsNullOrEmpty(deathSequenceId))
                    _anim.PlayActionSequence(deathSequenceId, -1f);
                else
                    _anim.PlayBumpReaction(deathStateNames, -1f);
            }

            StartCoroutine(CorpseRoutine());
        }

        private IEnumerator CorpseRoutine()
        {
            if (corpseLingerSeconds <= 0f) yield break; // lie indefinitely
            yield return new WaitForSeconds(corpseLingerSeconds);
            gameObject.SetActive(false);
        }

        /// <summary>Convenience: is the given agent's combat marker dead?</summary>
        public static bool IsAgentDead(DoomsAgentTag tag)
        {
            if (tag == null) return false;
            var c = tag.GetComponent<DoomsAgentCombat>();
            return c != null && c.IsDead;
        }
    }
}
