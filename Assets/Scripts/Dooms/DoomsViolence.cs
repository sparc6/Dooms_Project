using UnityEngine;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Central seam for the consequences of a violent act in the DOOMS crowd sim.
    /// Both T4EncounterResolver (shoot/fight resolution) and DoomsAgentCombat.Kill
    /// route through <see cref="ReportViolence"/> so mood broadcast, witness affinity
    /// drift, and (Phase 3) escalation all live in one place.
    ///
    /// Static board convention — like FactionDirectiveBoard / AmbientMoodBoard — so
    /// deleting the Dooms/ folder leaves the rest of the project compiling.
    /// </summary>
    public static class DoomsViolence
    {
        private static float _forceLethalUntil = -1f;

        /// <summary>
        /// Fired on any reported violent act, for telemetry/escalation.
        /// (aggressorId, victimId, aggressorFaction, victimFaction, lethal, witnessCount, position)
        /// </summary>
        public static event System.Action<string, string, string, string, bool, int, Vector3> OnViolence;

        /// <summary>
        /// Whether lethal (Shoot) acts are currently permitted. Off by default to
        /// keep the opening orderly; authored on via ExtrasProfile.lethalEncountersEnabled
        /// or temporarily via <see cref="EnableLethalFor"/> (Phase 3 escalation).
        /// </summary>
        public static bool LethalAllowed
        {
            get
            {
                if (Time.time < _forceLethalUntil) return true;
                var p = ExtrasProfileSO.Instance;
                return p != null && p.lethalEncountersEnabled;
            }
        }

        /// <summary>
        /// Phase 3 escalation hook: temporarily unlock lethal acts (e.g. after the
        /// first killing, or when a security_alert threshold is crossed).
        /// </summary>
        public static void EnableLethalFor(float seconds)
        {
            _forceLethalUntil = Mathf.Max(_forceLethalUntil, Time.time + Mathf.Max(0f, seconds));
        }

        /// <summary>
        /// Report a violent act (fight or lethal shot). Broadcasts a local mood tag
        /// so the surrounding crowd reacts through the existing ambient path, and
        /// skews nearby witnesses' faction affinities toward the victim / against the
        /// aggressor. Persona hysteresis turns sustained pressure into real defection.
        /// </summary>
        public static void ReportViolence(DoomsAgentTag aggressor, DoomsAgentTag victim, Vector3 pos, bool lethal)
        {
            string aggFaction = DoomsFactionRuntime.EffectiveFactionOf(aggressor);
            string vicFaction = DoomsFactionRuntime.EffectiveFactionOf(victim);
            var profile = ExtrasProfileSO.Instance;

            // 1. Local mood injection — reuse the existing ambient reaction flow.
            string tag = profile != null && !string.IsNullOrEmpty(profile.violenceMoodTag)
                ? profile.violenceMoodTag : "violent";
            float radius = profile != null ? Mathf.Max(2f, profile.violenceInfluenceRadius) : 10f;
            float intensity = lethal ? 0.95f : 0.6f;
            float ttl = lethal ? 6f : 3f;
            AmbientMoodBoard.InjectLocalTag(tag, pos, radius, intensity, ttl, BuildFactionArray(aggFaction, vicFaction));

            // 2. Witness affinity drift.
            int witnesses = ApplyWitnessDrift(aggressor, victim, aggFaction, vicFaction, pos, lethal, profile);

            // 3. Notify listeners (telemetry, future escalation).
            OnViolence?.Invoke(
                aggressor != null ? aggressor.agentId : "",
                victim != null ? victim.agentId : "",
                aggFaction, vicFaction, lethal, witnesses, pos);
        }

        private static string[] BuildFactionArray(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return new string[0];
            if (string.IsNullOrEmpty(a)) return new[] { b };
            if (string.IsNullOrEmpty(b)) return new[] { a };
            return new[] { a, b };
        }

        private static int ApplyWitnessDrift(DoomsAgentTag aggressor, DoomsAgentTag victim,
            string aggFaction, string vicFaction, Vector3 pos, bool lethal, ExtrasProfileSO profile)
        {
            if (string.IsNullOrEmpty(aggFaction) && string.IsNullOrEmpty(vicFaction)) return 0;

            float baseDrift = profile != null ? profile.witnessDriftBase : 0.05f;
            if (baseDrift <= 0f) return 0;
            float lethalMul = lethal ? (profile != null ? Mathf.Max(1f, profile.lethalDriftMultiplier) : 2.5f) : 1f;
            float radius = profile != null ? Mathf.Max(1f, profile.witnessRadius) : 12f;

            int witnessCount = 0;
            var seen = new System.Collections.Generic.HashSet<DoomsAgentTag>();
            var hits = Physics.OverlapSphere(pos, radius);
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i];
                if (c == null) continue;
                // Collider is usually on a child mesh; the tag lives on the root.
                var wTag = c.GetComponentInParent<DoomsAgentTag>();
                if (wTag == null || wTag == aggressor || wTag == victim) continue;
                if (!seen.Add(wTag)) continue; // dedupe multi-collider agents

                var persona = wTag.GetComponent<DoomsAgentPersona>();
                if (persona == null) continue;

                float d = Vector3.Distance(pos, wTag.transform.position);
                float prox = Mathf.Clamp01(1f - d / radius);
                if (prox <= 0f) continue;

                // Empathic witnesses (high sociability) skew harder.
                float delta = baseDrift * lethalMul * prox * Mathf.Lerp(0.6f, 1.4f, Mathf.Clamp01(persona.sociability));

                // Sympathy toward the victim grows with prior liking; blame toward the
                // aggressor shrinks the more the witness already sided with them.
                if (!string.IsNullOrEmpty(vicFaction))
                {
                    float sympathy = 1f + Mathf.Max(0f, persona.GetAffinity(vicFaction));
                    persona.Nudge(vicFaction, +delta * sympathy);
                }
                if (!string.IsNullOrEmpty(aggFaction))
                {
                    float alignment = Mathf.Clamp01(persona.GetAffinity(aggFaction));
                    persona.Nudge(aggFaction, -delta * (1f - alignment));
                }
                witnessCount++;
            }
            return witnessCount;
        }
    }
}
