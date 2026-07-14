using System;
using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms
{
    [DisallowMultipleComponent]
    [AddComponentMenu("MLA_SIM/DOOMS/Dooms Agent Persona")]
    public class DoomsAgentPersona : MonoBehaviour
    {
        [Serializable]
        public class FactionAffinity
        {
            [RegistryDropdown(RegistryType.Faction)]
            public string factionId = "";
            [Range(-1f, 1f)] public float affinity = 0f;
        }

        [Header("Affinity")]
        [SerializeField] private List<FactionAffinity> _affinities = new List<FactionAffinity>();

        [Header("Traits")]
        [Range(0f, 1f)] public float aggression = 0.5f;
        [Range(0f, 1f)] public float sociability = 0.5f;

        [Header("Defection Hysteresis")]
        public float defectionMargin = 0.25f;
        public float defectionHoldSec = 6f;

        public string EffectiveFaction { get; private set; } = "";

        // Fired when an agent commits to a new effective faction (after hysteresis).
        // (agentId, fromFaction, toFaction) — consumed by telemetry.
        public static event System.Action<string, string, string> OnFactionShift;

        private DoomsAgentTag _tag;
        private DoomsAgentNeeds _needs;
        private string _candidateFaction = "";
        private float _candidateSince = 0f;

        private void Awake()
        {
            _tag = GetComponent<DoomsAgentTag>();
            _needs = GetComponent<DoomsAgentNeeds>();

            SeedIdentityIfNeeded();
            if (_tag != null && !string.IsNullOrEmpty(_tag.factionId))
                EffectiveFaction = _tag.factionId;
            else
                EffectiveFaction = FindBestFaction();

            ApplyTraitRuntimeDials();
        }

        private void Update()
        {
            if (string.IsNullOrEmpty(EffectiveFaction))
            {
                EffectiveFaction = FindBestFaction();
                _candidateFaction = "";
                _candidateSince = 0f;
                return;
            }

            string best = FindBestFaction();
            if (string.IsNullOrEmpty(best) || string.Equals(best, EffectiveFaction, StringComparison.OrdinalIgnoreCase))
            {
                _candidateFaction = "";
                _candidateSince = 0f;
                return;
            }

            float currentAffinity = GetAffinity(EffectiveFaction);
            float bestAffinity = GetAffinity(best);
            if (bestAffinity <= currentAffinity + Mathf.Max(0f, defectionMargin))
            {
                _candidateFaction = "";
                _candidateSince = 0f;
                return;
            }

            if (!string.Equals(_candidateFaction, best, StringComparison.OrdinalIgnoreCase))
            {
                _candidateFaction = best;
                _candidateSince = Time.time;
                return;
            }

            if (Time.time - _candidateSince >= Mathf.Max(0.1f, defectionHoldSec))
            {
                string from = EffectiveFaction;
                EffectiveFaction = _candidateFaction;
                _candidateFaction = "";
                _candidateSince = 0f;
                string aid = _tag != null ? _tag.agentId : gameObject.name;
                OnFactionShift?.Invoke(aid, from, EffectiveFaction);
            }
        }

        public void Nudge(string factionId, float delta)
        {
            if (string.IsNullOrEmpty(factionId) || Mathf.Approximately(delta, 0f)) return;

            int idx = FindAffinityIndex(factionId);
            if (idx < 0)
            {
                _affinities.Add(new FactionAffinity
                {
                    factionId = factionId,
                    affinity = Mathf.Clamp(delta, -1f, 1f)
                });
                return;
            }

            _affinities[idx].affinity = Mathf.Clamp(_affinities[idx].affinity + delta, -1f, 1f);
        }

        private void SeedIdentityIfNeeded()
        {
            if (_tag == null || string.IsNullOrEmpty(_tag.factionId)) return;
            if (_affinities != null && _affinities.Count > 0) return;

            _affinities = new List<FactionAffinity>
            {
                new FactionAffinity { factionId = _tag.factionId, affinity = 1f }
            };
        }

        private void ApplyTraitRuntimeDials()
        {
            if (_needs == null) return;
            _needs.socialDecayMultiplier = Mathf.Lerp(1.35f, 0.65f, Mathf.Clamp01(sociability));
        }

        private int FindAffinityIndex(string factionId)
        {
            if (_affinities == null) return -1;
            for (int i = 0; i < _affinities.Count; i++)
            {
                var a = _affinities[i];
                if (a == null || string.IsNullOrEmpty(a.factionId)) continue;
                if (string.Equals(a.factionId, factionId, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        public float GetAffinity(string factionId)
        {
            int idx = FindAffinityIndex(factionId);
            if (idx < 0) return 0f;
            return _affinities[idx].affinity;
        }

        /// <summary>
        /// Personal hostility toward a faction in [-1..1]: the inverse of affinity.
        /// Used by T4EncounterResolver to let an individual's grudge override the
        /// faction-level relation when choosing a pair action (e.g. shoot).
        /// </summary>
        public float HostilityToward(string factionId)
        {
            return -GetAffinity(factionId);
        }

        /// <summary>Compact "faction=value|faction=value" snapshot of all affinities (telemetry).</summary>
        public string AffinitySummary()
        {
            if (_affinities == null || _affinities.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _affinities.Count; i++)
            {
                var a = _affinities[i];
                if (a == null || string.IsNullOrEmpty(a.factionId)) continue;
                if (sb.Length > 0) sb.Append('|');
                sb.Append(a.factionId).Append('=').Append(a.affinity.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private string FindBestFaction()
        {
            if (_affinities == null || _affinities.Count == 0)
            {
                return _tag != null ? _tag.factionId : "";
            }

            string bestFaction = _tag != null ? _tag.factionId : "";
            float best = float.MinValue;
            for (int i = 0; i < _affinities.Count; i++)
            {
                var a = _affinities[i];
                if (a == null || string.IsNullOrEmpty(a.factionId)) continue;
                if (a.affinity > best)
                {
                    best = a.affinity;
                    bestFaction = a.factionId;
                }
            }

            return bestFaction;
        }
    }
}
