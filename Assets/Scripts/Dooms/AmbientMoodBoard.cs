using UnityEngine;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Static, read-only-from-agents broadcast describing the currently active
    /// scene's "ambient mood" and spatial influence. SceneDirector writes it on
    /// scene / phase transitions; UNASSIGNED T4 agents (DoomsAgentT4Brain) read
    /// it to stay coherent with the scene without being directly assigned a role.
    ///
    /// Mirrors the FactionDirectiveBoard static-board convention so deleting the
    /// Dooms folder leaves the rest of the project compiling.
    /// </summary>
    public static class AmbientMoodBoard
    {
        public static bool Active { get; private set; }
        public static SceneMood Mood { get; private set; } = SceneMood.Calm;
        public static float Intensity { get; private set; }
        public static Vector3 Epicenter { get; private set; }
        public static float InfluenceRadius { get; private set; }
        public static string SceneId { get; private set; } = "";

        private static bool _localActive;
        private static SceneMood _localMood = SceneMood.Calm;
        private static float _localIntensity;
        private static Vector3 _localEpicenter;
        private static float _localInfluenceRadius;
        private static float _localUntil;
        private static string[] _localFactions = new string[0];

        private static string[] _sceneFactions = new string[0];

        public static string[] SceneFactions
        {
            get
            {
                var copy = new string[_sceneFactions.Length];
                _sceneFactions.CopyTo(copy, 0);
                return copy;
            }
        }

        public static void Set(SceneMood mood, float intensity, Vector3 epicenter,
                               float influenceRadius, string sceneId, string[] sceneFactions)
        {
            Active = true;
            Mood = mood;
            Intensity = Mathf.Clamp01(intensity);
            Epicenter = epicenter;
            InfluenceRadius = Mathf.Max(0f, influenceRadius);
            SceneId = sceneId ?? "";
            _sceneFactions = sceneFactions ?? new string[0];
        }

        public static void Clear()
        {
            Active = false;
            Mood = SceneMood.Calm;
            Intensity = 0f;
            InfluenceRadius = 0f;
            SceneId = "";
            _sceneFactions = new string[0];
            _localActive = false;
            _localMood = SceneMood.Calm;
            _localIntensity = 0f;
            _localInfluenceRadius = 0f;
            _localUntil = 0f;
            _localFactions = new string[0];
        }

        /// <summary>
        /// Inject a short-lived local ambient tag influence without replacing the
        /// current scene broadcast. Used for infectious/hostile activity bursts.
        /// </summary>
        public static void InjectLocalTag(string tag, Vector3 epicenter, float influenceRadius, float intensity, float ttlSeconds, string[] factions = null)
        {
            if (string.IsNullOrEmpty(tag)) return;

            var profile = AmbientMoodProfileSO.Instance;
            SceneMood mood = profile != null ? profile.ClassifyMood(new[] { tag }, intensity) : SceneMood.Tense;

            _localActive = true;
            _localMood = mood;
            _localIntensity = Mathf.Clamp01(intensity);
            _localEpicenter = epicenter;
            _localInfluenceRadius = Mathf.Max(0f, influenceRadius);
            _localUntil = Time.time + Mathf.Max(0.5f, ttlSeconds);
            _localFactions = factions ?? new string[0];
        }

        private static bool IsLocalActive()
        {
            if (!_localActive) return false;
            if (Time.time > _localUntil)
            {
                _localActive = false;
                _localMood = SceneMood.Calm;
                _localIntensity = 0f;
                _localInfluenceRadius = 0f;
                _localFactions = new string[0];
                return false;
            }
            return true;
        }

        private static bool UseLocalForPosition(Vector3 pos)
        {
            if (!IsLocalActive()) return false;

            var profile = AmbientMoodProfileSO.Instance;
            float falloff = profile != null ? Mathf.Max(0f, profile.reactionFalloff) : 4f;
            float d = Vector3.Distance(pos, _localEpicenter);
            return d <= (_localInfluenceRadius + falloff);
        }

        public static bool HasAnyInfluenceAt(Vector3 pos)
        {
            return IsInsideInfluence(pos) || UseLocalForPosition(pos);
        }

        public static SceneMood MoodAt(Vector3 pos)
        {
            if (UseLocalForPosition(pos)) return _localMood;
            return Mood;
        }

        public static float IntensityAt(Vector3 pos)
        {
            if (UseLocalForPosition(pos)) return _localIntensity;
            return Intensity;
        }

        public static Vector3 EpicenterAt(Vector3 pos)
        {
            if (UseLocalForPosition(pos)) return _localEpicenter;
            return Epicenter;
        }

        public static float InfluenceRadiusAt(Vector3 pos)
        {
            if (UseLocalForPosition(pos)) return _localInfluenceRadius;
            return InfluenceRadius;
        }

        /// <summary>0 outside influence (+falloff band), 1 at/inside the radius.</summary>
        public static float InfluenceFactor(Vector3 pos)
        {
            if (UseLocalForPosition(pos))
            {
                var localProfile = AmbientMoodProfileSO.Instance;
                float localFalloff = localProfile != null ? Mathf.Max(0f, localProfile.reactionFalloff) : 4f;
                float localDistance = Vector3.Distance(pos, _localEpicenter);
                float localOuter = _localInfluenceRadius + localFalloff;
                if (localDistance >= localOuter) return 0f;
                if (localDistance <= _localInfluenceRadius) return 1f;
                return 1f - (localDistance - _localInfluenceRadius) / Mathf.Max(0.0001f, localFalloff);
            }

            if (!Active || InfluenceRadius <= 0f) return 0f;
            var profile = AmbientMoodProfileSO.Instance;
            float falloff = profile != null ? Mathf.Max(0f, profile.reactionFalloff) : 4f;
            float d = Vector3.Distance(pos, Epicenter);
            float outer = InfluenceRadius + falloff;
            if (d >= outer) return 0f;
            if (d <= InfluenceRadius) return 1f;
            return 1f - (d - InfluenceRadius) / Mathf.Max(0.0001f, falloff);
        }

        public static bool IsInsideInfluence(Vector3 pos) => InfluenceFactor(pos) > 0f;

        /// <summary>
        /// Strongest (most actionable) relation the given faction holds toward any
        /// faction driving the active scene. Hostile dominates, then Ally,
        /// Superior, Subordinate, else Neutral.
        /// </summary>
        public static Relation WorstRelationFor(string factionId)
        {
            return WorstRelationForAt(factionId, Epicenter);
        }

        public static Relation WorstRelationForAt(string factionId, Vector3 pos)
        {
            string[] factions = UseLocalForPosition(pos) && _localFactions != null && _localFactions.Length > 0
                ? _localFactions
                : _sceneFactions;

            if (string.IsNullOrEmpty(factionId) || factions.Length == 0) return Relation.Neutral;
            var rel = FactionRelationsSO.Instance;
            if (rel == null) return Relation.Neutral;

            Relation result = Relation.Neutral;
            int bestRank = -1;
            for (int i = 0; i < factions.Length; i++)
            {
                var sf = factions[i];
                if (string.IsNullOrEmpty(sf)) continue;
                var r = rel.GetRelation(factionId, sf);
                int rank = RelationRank(r);
                if (rank > bestRank) { bestRank = rank; result = r; }
            }
            return result;
        }

        private static int RelationRank(Relation r)
        {
            switch (r)
            {
                case Relation.Hostile: return 4;
                case Relation.Ally: return 3;
                case Relation.Superior: return 2;
                case Relation.Subordinate: return 1;
                default: return 0;
            }
        }
    }
}
