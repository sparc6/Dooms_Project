using UnityEngine;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Phase 1: Per-agent maslow-style needs driver. Drives the "live-sim"
    /// portion of T3 and T4 behavior. Values are 0..1 (1 = fully satisfied).
    ///
    /// Decay runs every frame in Update. Brains query <see cref="GetHighestDeficit"/>
    /// and <see cref="IsCritical"/> each tick and pick activities accordingly.
    /// Brains call <see cref="Restore"/> after completing an anchor-based action
    /// (e.g. Sleep restores energy, Eat restores hunger).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MLA_SIM/DOOMS/Dooms Agent Needs")]
    public class DoomsAgentNeeds : MonoBehaviour
    {
        public enum NeedType { Energy, Hunger, Social, Duty }

        [Header("Current Values (0..1)")]
        [Range(0f, 1f)] public float energy = 0.8f;
        [Range(0f, 1f)] public float hunger = 0.8f;
        [Range(0f, 1f)] public float social = 0.7f;
        [Range(0f, 1f)] public float duty = 0.6f;

        [Header("Decay Rates (per hour, sim-time)")]
        [Tooltip("Fraction lost per hour of sim time. 0.04 = -4% per hour.")]
        public float energyDecayPerHour = 0.03f;
        public float hungerDecayPerHour = 0.05f;
        public float socialDecayPerHour = 0.04f;
        public float dutyDecayPerHour = 0.02f;

        [Header("Runtime Multipliers")]
        [Tooltip("Runtime multiplier for social decay (set by persona traits).")]
        public float socialDecayMultiplier = 1f;

        [Header("Critical Threshold")]
        [Tooltip("Below this value the need is considered critical and overrides other activity choices.")]
        [Range(0f, 0.5f)]
        public float criticalThreshold = 0.2f;

        private float _lastTick;

        private void Start()
        {
            _lastTick = Time.time;
        }

        private void Update()
        {
            float now = Time.time;
            float dtSec = now - _lastTick;
            if (dtSec <= 0f) return;
            _lastTick = now;

            // Convert per-hour rate into per-second.
            float hoursElapsed = dtSec / 3600f;
            energy = Mathf.Clamp01(energy - energyDecayPerHour * hoursElapsed);
            hunger = Mathf.Clamp01(hunger - hungerDecayPerHour * hoursElapsed);
            social = Mathf.Clamp01(social - socialDecayPerHour * Mathf.Max(0f, socialDecayMultiplier) * hoursElapsed);
            duty = Mathf.Clamp01(duty - dutyDecayPerHour * hoursElapsed);
        }

        public float Get(NeedType n)
        {
            switch (n)
            {
                case NeedType.Energy: return energy;
                case NeedType.Hunger: return hunger;
                case NeedType.Social: return social;
                case NeedType.Duty: return duty;
            }
            return 1f;
        }

        public float GetDeficit(NeedType n) => 1f - Get(n);

        public bool IsCritical(NeedType n) => Get(n) < criticalThreshold;

        public bool AnyCritical => IsCritical(NeedType.Energy) || IsCritical(NeedType.Hunger)
                                || IsCritical(NeedType.Social) || IsCritical(NeedType.Duty);

        public NeedType GetHighestDeficit()
        {
            NeedType best = NeedType.Duty;
            float bestDef = GetDeficit(NeedType.Duty);
            if (GetDeficit(NeedType.Energy) > bestDef) { bestDef = GetDeficit(NeedType.Energy); best = NeedType.Energy; }
            if (GetDeficit(NeedType.Hunger) > bestDef) { bestDef = GetDeficit(NeedType.Hunger); best = NeedType.Hunger; }
            if (GetDeficit(NeedType.Social) > bestDef) { bestDef = GetDeficit(NeedType.Social); best = NeedType.Social; }
            return best;
        }

        public void Restore(NeedType n, float amount)
        {
            amount = Mathf.Max(0f, amount);
            switch (n)
            {
                case NeedType.Energy: energy = Mathf.Clamp01(energy + amount); break;
                case NeedType.Hunger: hunger = Mathf.Clamp01(hunger + amount); break;
                case NeedType.Social: social = Mathf.Clamp01(social + amount); break;
                case NeedType.Duty:   duty   = Mathf.Clamp01(duty   + amount); break;
            }
        }
    }
}
