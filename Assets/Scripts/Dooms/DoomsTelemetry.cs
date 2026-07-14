using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;
using MLA_SIM;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Scene-level telemetry recorder for the DOOMS T4 system. Drop one in the
    /// scene and on Play it writes two files under &lt;project&gt;/Temporary/DoomsTelemetry/:
    ///   - agents_&lt;timestamp&gt;.csv  : one row per agent per sample tick
    ///                                  (position, nav + animator state, activity,
    ///                                  needs, faction affinities, dead/locked).
    ///   - events_&lt;timestamp&gt;.jsonl: one line per discrete event
    ///                                  (encounter staged, violence/kill, faction shift).
    ///
    /// Fully passive and opt-in: subscribes to existing static events and reads
    /// public members only. Disable the component to stop logging.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MLA_SIM/DOOMS/Dooms Telemetry")]
    public class DoomsTelemetry : MonoBehaviour
    {
        [Header("What to log")]
        public bool logAgents = true;
        public bool logEvents = true;

        [Header("Sampling")]
        [Tooltip("Seconds between agent snapshot rows.")]
        public float sampleInterval = 0.5f;

        [Header("Output")]
        [Tooltip("Subfolder under <project>/Temporary/.")]
        public string outputSubdir = "DoomsTelemetry";
        [Tooltip("Log to the Unity console when files open/close.")]
        public bool verbose = true;

        [Header("Backend relay")]
        [Tooltip("Best-effort relay of encounter/violence/faction-shift events to /api/dooms/event.")]
        public bool emitBackendEvents = true;
        [Tooltip("Override for the backend base URL. Leave empty to reuse DoomsConfigPusher resolution.")]
        public string backendBaseUrlOverride = "";
        [Range(1f, 15f)]
        public float requestTimeoutSec = 5f;

        private StreamWriter _agentsWriter;
        private StreamWriter _eventsWriter;
        private float _nextSample;
        private bool _open;

        private void OnEnable()
        {
            OpenFiles();
            T4EncounterResolver.OnEncounterStaged += HandleEncounter;
            DoomsViolence.OnViolence += HandleViolence;
            DoomsAgentPersona.OnFactionShift += HandleFactionShift;
            _nextSample = Time.time;
        }

        private void OnDisable()
        {
            T4EncounterResolver.OnEncounterStaged -= HandleEncounter;
            DoomsViolence.OnViolence -= HandleViolence;
            DoomsAgentPersona.OnFactionShift -= HandleFactionShift;
            CloseFiles();
        }

        private void OpenFiles()
        {
            if (_open) return;
            try
            {
                string baseDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temporary", outputSubdir));
                Directory.CreateDirectory(baseDir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                if (logAgents)
                {
                    _agentsWriter = new StreamWriter(Path.Combine(baseDir, $"agents_{stamp}.csv"), false, Encoding.UTF8) { AutoFlush = true };
                    _agentsWriter.WriteLine("t,agentId,faction,effectiveFaction,tier,x,y,z,navState,navSpeed,animState,locoStyle,activity,energy,hunger,social,duty,affinities,isDead,locked");
                }
                if (logEvents)
                {
                    _eventsWriter = new StreamWriter(Path.Combine(baseDir, $"events_{stamp}.jsonl"), false, Encoding.UTF8) { AutoFlush = true };
                }

                _open = true;
                if (verbose) Debug.Log($"[DOOMS][Telemetry] Logging to '{baseDir}' (agents={logAgents}, events={logEvents}).");
            }
            catch (Exception e)
            {
                Debug.LogError($"[DOOMS][Telemetry] Failed to open output files: {e.Message}");
                CloseFiles();
            }
        }

        private void CloseFiles()
        {
            _open = false;
            try { _agentsWriter?.Flush(); _agentsWriter?.Dispose(); } catch { }
            try { _eventsWriter?.Flush(); _eventsWriter?.Dispose(); } catch { }
            _agentsWriter = null;
            _eventsWriter = null;
        }

        private void Update()
        {
            if (!_open || !logAgents || _agentsWriter == null) return;
            if (Time.time < _nextSample) return;
            _nextSample = Time.time + Mathf.Max(0.05f, sampleInterval);
            SampleAgents();
        }

        private void SampleAgents()
        {
            var tags = FindObjectsByType<DoomsAgentTag>(FindObjectsSortMode.None);
            float t = Time.time;
            for (int i = 0; i < tags.Length; i++)
            {
                var tag = tags[i];
                if (tag == null) continue;
                try { _agentsWriter.WriteLine(BuildAgentRow(t, tag)); }
                catch (Exception e) { Debug.LogError($"[DOOMS][Telemetry] write failed: {e.Message}"); break; }
            }
        }

        private static string BuildAgentRow(float t, DoomsAgentTag tag)
        {
            string agentId = string.IsNullOrEmpty(tag.agentId) ? tag.gameObject.name : tag.agentId;
            string effFaction = DoomsFactionRuntime.EffectiveFactionOf(tag);
            Vector3 p = tag.transform.position;

            var nav = tag.GetComponent<NavMeshAgent>();
            string navState = "none";
            float navSpeed = 0f;
            if (nav != null && nav.enabled)
            {
                navSpeed = nav.velocity.magnitude;
                if (!nav.isOnNavMesh) navState = "offmesh";
                else if (nav.isStopped || !nav.hasPath) navState = "idle";
                else navState = "moving";
            }

            var anim = tag.GetComponent<AnimatorLocomotionDriver>();
            string animState = "";
            if (anim != null)
                animState = !string.IsNullOrEmpty(anim.CurrentStateName) ? anim.CurrentStateName : anim.locomotionStateName;

            int locoStyle = ReadLocoStyle(tag);

            var brain = tag.GetComponent<DoomsAgentT4Brain>();
            string activity = brain != null ? brain.CurrentActivityName : "";

            var needs = tag.GetComponent<DoomsAgentNeeds>();
            string energy = needs != null ? needs.energy.ToString("F2", CultureInfo.InvariantCulture) : "";
            string hunger = needs != null ? needs.hunger.ToString("F2", CultureInfo.InvariantCulture) : "";
            string social = needs != null ? needs.social.ToString("F2", CultureInfo.InvariantCulture) : "";
            string duty = needs != null ? needs.duty.ToString("F2", CultureInfo.InvariantCulture) : "";

            var persona = tag.GetComponent<DoomsAgentPersona>();
            string affinities = persona != null ? persona.AffinitySummary() : "";

            var combat = tag.GetComponent<DoomsAgentCombat>();
            bool isDead = combat != null && combat.IsDead;
            bool locked = T4EncounterResolver.IsAgentLocked(agentId);

            var sb = new StringBuilder(160);
            sb.Append(t.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Csv(agentId)).Append(',');
            sb.Append(Csv(tag.factionId)).Append(',');
            sb.Append(Csv(effFaction)).Append(',');
            sb.Append(tag.tier).Append(',');
            sb.Append(p.x.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(p.y.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(p.z.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(navState).Append(',');
            sb.Append(navSpeed.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Csv(animState)).Append(',');
            sb.Append(locoStyle).Append(',');
            sb.Append(Csv(activity)).Append(',');
            sb.Append(energy).Append(',');
            sb.Append(hunger).Append(',');
            sb.Append(social).Append(',');
            sb.Append(duty).Append(',');
            sb.Append(Csv(affinities)).Append(',');
            sb.Append(isDead ? 1 : 0).Append(',');
            sb.Append(locked ? 1 : 0);
            return sb.ToString();
        }

        // Read the "LocoStyle" int without spamming warnings when the param is absent.
        private static int ReadLocoStyle(DoomsAgentTag tag)
        {
            var animator = tag.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null) return 0;
            var ps = animator.parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].type == AnimatorControllerParameterType.Int && ps[i].name == "LocoStyle")
                    return animator.GetInteger("LocoStyle");
            }
            return 0;
        }

        private void HandleEncounter(string aId, string bId, string action, Relation relation, Vector3 pos)
        {
            if (logEvents && _eventsWriter != null)
            {
                var sb = new StringBuilder(160);
                sb.Append("{\"t\":").Append(Time.time.ToString("F2", CultureInfo.InvariantCulture));
                sb.Append(",\"type\":\"encounter\"");
                sb.Append(",\"a\":").Append(JStr(aId));
                sb.Append(",\"b\":").Append(JStr(bId));
                sb.Append(",\"action\":").Append(JStr(action));
                sb.Append(",\"relation\":").Append(JStr(relation.ToString()));
                sb.Append(",\"x\":").Append(pos.x.ToString("F2", CultureInfo.InvariantCulture));
                sb.Append(",\"z\":").Append(pos.z.ToString("F2", CultureInfo.InvariantCulture));
                sb.Append('}');
                WriteEvent(sb.ToString());
            }
            if (emitBackendEvents)
            {
                StartCoroutine(PostBackendEvent(
                    "encounter",
                    aId,
                    new Dictionary<string, object>
                    {
                        { "other_agent_id", bId },
                        { "action", action },
                        { "relation", relation.ToString() },
                        { "x", pos.x },
                        { "z", pos.z }
                    }
                ));
            }
        }

        private void HandleViolence(string aggId, string vicId, string aggFaction, string vicFaction, bool lethal, int witnesses, Vector3 pos)
        {
            if (logEvents && _eventsWriter != null)
            {
                var sb = new StringBuilder(200);
                sb.Append("{\"t\":").Append(Time.time.ToString("F2", CultureInfo.InvariantCulture));
                sb.Append(",\"type\":\"violence\"");
                sb.Append(",\"aggressor\":").Append(JStr(aggId));
                sb.Append(",\"victim\":").Append(JStr(vicId));
                sb.Append(",\"aggFaction\":").Append(JStr(aggFaction));
                sb.Append(",\"vicFaction\":").Append(JStr(vicFaction));
                sb.Append(",\"lethal\":").Append(lethal ? "true" : "false");
                sb.Append(",\"witnesses\":").Append(witnesses);
                sb.Append(",\"x\":").Append(pos.x.ToString("F2", CultureInfo.InvariantCulture));
                sb.Append(",\"z\":").Append(pos.z.ToString("F2", CultureInfo.InvariantCulture));
                sb.Append('}');
                WriteEvent(sb.ToString());
            }
            if (emitBackendEvents)
            {
                StartCoroutine(PostBackendEvent(
                    "violence",
                    aggId,
                    new Dictionary<string, object>
                    {
                        { "victim", vicId },
                        { "aggFaction", aggFaction },
                        { "vicFaction", vicFaction },
                        { "lethal", lethal },
                        { "witnesses", witnesses },
                        { "x", pos.x },
                        { "z", pos.z }
                    }
                ));
            }
        }

        private void HandleFactionShift(string agentId, string from, string to)
        {
            if (logEvents && _eventsWriter != null)
            {
                var sb = new StringBuilder(120);
                sb.Append("{\"t\":").Append(Time.time.ToString("F2", CultureInfo.InvariantCulture));
                sb.Append(",\"type\":\"faction_shift\"");
                sb.Append(",\"agent\":").Append(JStr(agentId));
                sb.Append(",\"from\":").Append(JStr(from));
                sb.Append(",\"to\":").Append(JStr(to));
                sb.Append('}');
                WriteEvent(sb.ToString());
            }
            if (emitBackendEvents)
            {
                StartCoroutine(PostBackendEvent(
                    "faction_shift",
                    agentId,
                    new Dictionary<string, object>
                    {
                        { "from", from },
                        { "to", to }
                    }
                ));
            }
        }

        private void WriteEvent(string line)
        {
            try { _eventsWriter.WriteLine(line); }
            catch (Exception e) { Debug.LogError($"[DOOMS][Telemetry] event write failed: {e.Message}"); }
        }

        private string ResolveBaseUrl()
        {
            if (!string.IsNullOrWhiteSpace(backendBaseUrlOverride))
                return backendBaseUrlOverride.TrimEnd('/');

            var pusher = FindFirstObjectByType<DoomsConfigPusher>();
            if (pusher != null)
            {
                var url = pusher.GetResolvedBaseUrl();
                if (!string.IsNullOrEmpty(url)) return url;
            }
            return null;
        }

        private IEnumerator PostBackendEvent(string kind, string agentId, Dictionary<string, object> data)
        {
            string baseUrl = ResolveBaseUrl();
            if (string.IsNullOrEmpty(baseUrl)) yield break;

            string url = $"{baseUrl}/dooms/event";
            string json = BuildBackendEventJson(kind, agentId, data);
            byte[] body = Encoding.UTF8.GetBytes(json);

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = Mathf.CeilToInt(requestTimeoutSec);
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();
            }
        }

        private static string BuildBackendEventJson(string kind, string agentId, Dictionary<string, object> data)
        {
            var sb = new StringBuilder(192);
            sb.Append('{');
            sb.Append("\"kind\":").Append(JStr(kind)).Append(',');
            sb.Append("\"agent_id\":").Append(JStr(agentId)).Append(',');
            sb.Append("\"data\":{");
            bool first = true;
            if (data != null)
            {
                foreach (var kv in data)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(JStr(kv.Key)).Append(':');
                    AppendJsonValue(sb, kv.Value);
                }
            }
            sb.Append("}}");
            return sb.ToString();
        }

        private static void AppendJsonValue(StringBuilder sb, object value)
        {
            if (value == null) { sb.Append("null"); return; }
            switch (value)
            {
                case string s: sb.Append(JStr(s)); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case float f: sb.Append(f.ToString(CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d.ToString(CultureInfo.InvariantCulture)); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                default: sb.Append(JStr(value.ToString())); break;
            }
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string JStr(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";
        }
    }
}
