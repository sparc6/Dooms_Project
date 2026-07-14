using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Serializes the Inspector-driven DOOMS configuration on this GameObject
    /// (and any sibling DOOMS config components) and POSTs it to the backend
    /// DOOMS plugin endpoints.
    ///
    /// Zero coupling to the core MLA_SIM pipeline: this script only reads its
    /// BackendConfig reference at runtime via a type lookup and uses raw
    /// UnityWebRequest. Deleting the Dooms/ folder removes all DOOMS code
    /// without dangling references elsewhere.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DoomsToggle))]
    [AddComponentMenu("MLA_SIM/DOOMS/Dooms Config Pusher")]
    public class DoomsConfigPusher : MonoBehaviour
    {
        [Header("Backend")]
        [Tooltip("Override for the backend base URL. Leave empty to auto-resolve from MLA_SIM.BackendCommunicator.config.ApiBaseUrl.")]
        public string backendBaseUrlOverride = "";

        [Tooltip("Timeout per DOOMS HTTP request, in seconds.")]
        [Range(1f, 60f)]
        public float requestTimeoutSec = 10f;

        private DoomsToggle _toggle;

        void Awake()
        {
            _toggle = GetComponent<DoomsToggle>();
        }

        void Start()
        {
            if (_toggle == null || !_toggle.enableDooms)
            {
                Debug.Log("[DoomsConfigPusher] DOOMS disabled; skipping push.");
                return;
            }

            if (_toggle.pushOnStart)
            {
                StartCoroutine(PushAllThenHealthz());
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Edit-time hook — only triggers if the play session is running
            // and the user has asked for push-on-validate. Non-playing edits
            // do nothing: Unity Inspector coroutines are not permitted then.
            if (_toggle != null && _toggle.pushOnValidate && Application.isPlaying)
            {
                StartCoroutine(PushConfig());
            }
        }
#endif

        // ------------------------------------------------------------------
        // Public API: callable from other DOOMS components to re-push config
        // when Inspector values are changed through gameplay UI.
        // ------------------------------------------------------------------
        public void PushNow()
        {
            if (_toggle == null || !_toggle.enableDooms) return;
            StartCoroutine(PushAllThenHealthz());
        }

        private IEnumerator PushAllThenHealthz()
        {
            yield return StartCoroutine(PushConfig());
            yield return StartCoroutine(PushAgents());
            yield return StartCoroutine(PushSceneIds());
            yield return StartCoroutine(PushRegistries());
            yield return StartCoroutine(CheckHealthz());
        }

        private IEnumerator PushSceneIds()
        {
            string baseUrl = ResolveBaseUrl();
            if (string.IsNullOrEmpty(baseUrl)) yield break;

            string url = $"{baseUrl}/dooms/scene_ids";

            // Fetch scene IDs from registry
            var sceneReg = Registries.SceneRegistrySO.Instance;
            var ids = new List<string>();
            if (sceneReg != null && sceneReg.scenes != null)
            {
                foreach (var s in sceneReg.scenes)
                {
                    if (s != null && !string.IsNullOrEmpty(s.sceneId))
                        ids.Add(s.sceneId);
                }
            }

            // Build simple JSON {"scene_ids": ["scene1", "scene2"]}
            var sb = new StringBuilder();
            sb.Append("{\"scene_ids\":[");
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(JsonString(ids[i]));
            }
            sb.Append("]}");

            yield return PostJson(url, sb.ToString(), "scene_ids");
        }

        private IEnumerator PushRegistries()
        {
            string baseUrl = ResolveBaseUrl();
            if (string.IsNullOrEmpty(baseUrl)) yield break;

            string url = $"{baseUrl}/dooms/registries";

            // Fetch point tags
            var pointReg = Registries.InteractionPointRegistrySO.Instance;
            var tags = new List<string>();
            if (pointReg != null && pointReg.pointTags != null)
            {
                tags.AddRange(pointReg.pointTags);
            }

            // Fetch factions
            var factionReg = Registries.FactionRegistrySO.Instance;
            var factions = new List<string>();
            if (factionReg != null && factionReg.factions != null)
            {
                foreach (var f in factionReg.factions)
                {
                    if (f != null && !string.IsNullOrEmpty(f.factionId))
                        factions.Add(f.factionId);
                }
            }

            // Build simple JSON {"point_tags": [...], "faction_ids": [...]}
            var sb = new StringBuilder();
            sb.Append("{\"point_tags\":[");
            for (int i = 0; i < tags.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(JsonString(tags[i]));
            }
            sb.Append("],\"faction_ids\":[");
            for (int i = 0; i < factions.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(JsonString(factions[i]));
            }
            sb.Append("]}");

            yield return PostJson(url, sb.ToString(), "registries");
        }

        /// <summary>Public accessor used by SceneDirector and other DOOMS components.</summary>
        public string GetResolvedBaseUrl() => ResolveBaseUrl();

        private string ResolveBaseUrl()
        {
            if (!string.IsNullOrWhiteSpace(backendBaseUrlOverride))
            {
                return backendBaseUrlOverride.TrimEnd('/');
            }

            // Reflection-style lookup to avoid a hard compile-time dependency
            // on the core BackendCommunicator class. If the core project is
            // restructured, DOOMS still compiles; worst case the override is
            // required.
            try
            {
                var t = Type.GetType("MLA_SIM.BackendCommunicator, Assembly-CSharp");
                if (t != null)
                {
                    var obj = UnityEngine.Object.FindFirstObjectByType(t) as Component;
                    if (obj != null)
                    {
                        var cfgField = t.GetField("config");
                        var cfg = cfgField != null ? cfgField.GetValue(obj) : null;
                        if (cfg != null)
                        {
                            var prop = cfg.GetType().GetProperty("ApiBaseUrl");
                            var val = prop != null ? prop.GetValue(cfg) as string : null;
                            if (!string.IsNullOrWhiteSpace(val)) return val.TrimEnd('/');
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DoomsConfigPusher] Base URL auto-resolve failed: {e.Message}");
            }

            Debug.LogError("[DoomsConfigPusher] No backend base URL resolved. Set backendBaseUrlOverride in the Inspector.");
            return null;
        }

        // ------------------------------------------------------------------
        // Config serialization. Plain Unity-serializable structs so that
        // JsonUtility can convert Inspector values into JSON without extra
        // dependencies.
        // ------------------------------------------------------------------
        // Config bundle (beats / factions / global_state) is hand-serialized
        // below because Dictionary<string, List<string>> in routine masks
        // cannot be encoded by JsonUtility. Agent tags still use JsonUtility.

        [Serializable] private class AgentsBundleWire
        {
            public AgentTagWire[] agents;
        }

        [Serializable] private class AgentTagWire
        {
            public string agent_id;
            public int tier;
            public string faction_id;
            public string archetype;
            public float visibility_importance;
        }

        private string BuildConfigJson()
        {
            // Hand-build the JSON so we can include factions with their
            // routine_mask_per_beat dictionaries. Stays dependency-free.
            var sb = new StringBuilder();
            sb.Append('{');
            string scenarioId = _toggle != null ? _toggle.scenarioId : "dooms";
            sb.Append("\"scenario_id\":").Append(JsonString(scenarioId)).Append(',');

            // Add top-level starting_narrative and narrative_principles matching Python schemas
            string startingNarrative = _toggle != null ? _toggle.startingNarrative : "";
            sb.Append("\"starting_narrative\":").Append(JsonString(startingNarrative)).Append(',');

            sb.Append("\"narrative_principles\":[");
            if (_toggle != null && _toggle.narrativePrinciples != null)
            {
                for (int i = 0; i < _toggle.narrativePrinciples.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(JsonString(_toggle.narrativePrinciples[i]));
                }
            }
            sb.Append("],");

            sb.Append("\"global_state\":{},");
            // Phase C: beats array stays empty here; Phase D will populate from
            // DoomsDirectorController. Backend treats empty as \"no beats yet\".
            sb.Append("\"beats\":[],");
            sb.Append("\"factions\":").Append(BuildFactionsJsonArray()).Append(',');
            sb.Append("\"extra\":").Append(BuildExtraJson());
            sb.Append('}');
            return sb.ToString();
        }

        private string BuildExtraJson()
        {
            if (_toggle == null) return "{}";
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"narrator\":{");
            sb.Append("\"enabled\":").Append(_toggle.enableWorldNarrator ? "true" : "false").Append(',');
            sb.Append("\"weight\":").Append(_toggle.narratorInfluence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"tick_sec\":").Append(_toggle.narratorTickSec.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"min_dwell_sec\":").Append(_toggle.narratorMinDwellSec.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"max_tokens\":").Append(_toggle.narratorMaxTokens).Append(',');
            sb.Append("\"temperature\":").Append(_toggle.narratorTemperature.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

            // Optional system prompt override.
            if (!string.IsNullOrWhiteSpace(_toggle.narratorSystemPromptOverride))
                sb.Append(',').Append("\"system\":").Append(JsonString(_toggle.narratorSystemPromptOverride.Trim()));

            // Optional themes override (one per line → JSON array).
            if (!string.IsNullOrWhiteSpace(_toggle.narratorThemesOverride))
            {
                var lines = _toggle.narratorThemesOverride.Split(new[]{'\n', '\r', ','}, StringSplitOptions.RemoveEmptyEntries);
                sb.Append(",\"themes\":[");
                bool first = true;
                foreach (var line in lines)
                {
                    string t = line.Trim();
                    if (string.IsNullOrEmpty(t)) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(JsonString(t));
                }
                sb.Append(']');
            }

            sb.Append('}'); // end narrator
            sb.Append('}'); // end extra
            return sb.ToString();
        }

        private string BuildFactionsJsonArray()
        {
            var factions = UnityEngine.Object.FindObjectsByType<DoomsFactionConfig>(FindObjectsSortMode.None);
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < factions.Length; i++)
            {
                var f = factions[i];
                if (f == null) continue;
                if (i > 0) sb.Append(',');
                sb.Append('{');
                sb.Append("\"faction_id\":").Append(JsonString(f.factionId ?? "")).Append(',');

                sb.Append("\"routine_mask_per_beat\":{");
                bool firstMask = true;
                if (f.routineMaskPerBeat != null)
                {
                    foreach (var entry in f.routineMaskPerBeat)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.beatId)) continue;
                        if (!firstMask) sb.Append(',');
                        firstMask = false;
                        sb.Append(JsonString(entry.beatId)).Append(":[");
                        if (entry.routineNames != null)
                        {
                            for (int k = 0; k < entry.routineNames.Count; k++)
                            {
                                if (k > 0) sb.Append(',');
                                sb.Append(JsonString(entry.routineNames[k] ?? ""));
                            }
                        }
                        sb.Append(']');
                    }
                }
                sb.Append("},");

                sb.Append("\"pressure_thresholds\":").Append(NamedFloatListToJson(f.pressureThresholds)).Append(',');
                sb.Append("\"default_emotional_state\":").Append(NamedFloatListToJson(f.defaultEmotionalState));
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string NamedFloatListToJson(List<DoomsFactionConfig.NamedFloat> list)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            if (list != null)
            {
                bool first = true;
                foreach (var nf in list)
                {
                    if (nf == null || string.IsNullOrEmpty(nf.key)) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(JsonString(nf.key)).Append(':').Append(nf.value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string JsonString(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private string BuildAgentsJson()
        {
            var tags = UnityEngine.Object.FindObjectsByType<DoomsAgentTag>(FindObjectsSortMode.None);
            var outList = new List<AgentTagWire>(tags.Length);
            foreach (var t in tags)
            {
                if (t == null) continue;
                
                // Force TryAutoBind using SendMessage to guarantee identity resolution 
                // in case the tag component is initialized after the config pusher.
                if (string.IsNullOrWhiteSpace(t.agentId))
                {
                    t.SendMessage("TryAutoBind", SendMessageOptions.DontRequireReceiver);
                }

                if (string.IsNullOrWhiteSpace(t.agentId))
                {
                    Debug.LogWarning($"[DoomsConfigPusher] Skipping DoomsAgentTag on '{t.gameObject.name}': empty agentId.");
                    continue;
                }
                outList.Add(new AgentTagWire
                {
                    agent_id = t.agentId,
                    tier = t.tier,
                    faction_id = t.factionId,
                    archetype = t.archetype,
                    visibility_importance = t.visibilityImportance,
                });
            }
            var bundle = new AgentsBundleWire { agents = outList.ToArray() };
            return JsonUtility.ToJson(bundle);
        }

        // ------------------------------------------------------------------
        // HTTP coroutines
        // ------------------------------------------------------------------
        private IEnumerator PushConfig()
        {
            string baseUrl = ResolveBaseUrl();
            if (string.IsNullOrEmpty(baseUrl)) yield break;

            string url = $"{baseUrl}/dooms/config";
            string json = BuildConfigJson();
            yield return PostJson(url, json, "config");
        }

        private IEnumerator PushAgents()
        {
            string baseUrl = ResolveBaseUrl();
            if (string.IsNullOrEmpty(baseUrl)) yield break;

            string url = $"{baseUrl}/dooms/agents";
            string json = BuildAgentsJson();
            yield return PostJson(url, json, "agents");
        }

        private IEnumerator CheckHealthz()
        {
            string baseUrl = ResolveBaseUrl();
            if (string.IsNullOrEmpty(baseUrl)) yield break;

            string url = $"{baseUrl}/dooms/healthz";
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = Mathf.CeilToInt(requestTimeoutSec);
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[DoomsConfigPusher] /healthz OK: {req.downloadHandler.text}");
                }
                else
                {
                    Debug.LogWarning($"[DoomsConfigPusher] /healthz FAILED ({req.responseCode}): {req.error}");
                }
            }
        }

        private IEnumerator PostJson(string url, string json, string label)
        {
            byte[] body = Encoding.UTF8.GetBytes(json ?? "{}");
            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = Mathf.CeilToInt(requestTimeoutSec);

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[DoomsConfigPusher] /{label} pushed OK: {req.downloadHandler.text}");
                }
                else
                {
                    Debug.LogWarning($"[DoomsConfigPusher] /{label} push FAILED ({req.responseCode}): {req.error}");
                }
            }
        }
    }
}
