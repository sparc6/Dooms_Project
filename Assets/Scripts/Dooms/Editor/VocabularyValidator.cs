#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MLA_SIM;                       // InteractableCatalog, AnimationSequenceRegistry
using MLA_SIM.Dooms.Registries;      // registries

namespace MLA_SIM.Dooms.EditorTools
{
    /// <summary>
    /// AREA 04 — A4.2. Shared validation + non-destructive merge core used by the
    /// scene and interactable importers.
    ///
    /// Given referenced ids per vocabulary kind, it buckets each id as:
    ///   resolved (exists in a registry/catalog),
    ///   newDeclared (listed in the authored JSON's "new" block),
    ///   unknown (neither — a typo/hallucination).
    ///
    /// Closed-by-default safety: importers refuse when there are unknowns unless
    /// the author explicitly chooses to generate; declared-new ids are MERGED
    /// (never cleared) into the registries.
    /// </summary>
    public static class VocabularyValidator
    {
        public enum Kind { Faction, PointTag, AreaTag, AnimationState, AnimationSequence, Prop, ContextTag, Action, Item, ObjectArchetype, Scene }

        public class Report
        {
            public List<(Kind kind, string id)> resolved = new List<(Kind, string)>();
            public List<(Kind kind, string id)> newDeclared = new List<(Kind, string)>();
            public List<(Kind kind, string id)> unknown = new List<(Kind, string)>();

            public bool HasBlockers => unknown.Count > 0;
            public int Created;

            public string Summarize()
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Resolved: {resolved.Count}   New (declared): {newDeclared.Count}   Unknown: {unknown.Count}");
                if (unknown.Count > 0)
                {
                    sb.AppendLine("\nUNKNOWN references (blocked — fix typo, reuse an id, or declare in 'new'):");
                    foreach (var u in unknown.Distinct()) sb.AppendLine($"  X  {u.kind}: {u.id}");
                }
                if (newDeclared.Count > 0)
                {
                    sb.AppendLine("\nNEW (declared) references — will be created on import:");
                    foreach (var n in newDeclared.Distinct()) sb.AppendLine($"  +  {n.kind}: {n.id}");
                }
                return sb.ToString();
            }
        }

        // ── existence lookups ────────────────────────────────────────────────

        private static HashSet<string> Existing(Kind kind)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            switch (kind)
            {
                case Kind.Faction:
                    var fr = FactionRegistrySO.Instance;
                    if (fr?.factions != null) foreach (var f in fr.factions) if (f != null && !string.IsNullOrEmpty(f.factionId)) set.Add(f.factionId);
                    break;
                case Kind.PointTag:
                    var pr = InteractionPointRegistrySO.Instance;
                    if (pr?.pointTags != null) foreach (var t in pr.pointTags) set.Add(t);
                    break;
                case Kind.AreaTag:
                    var ar = AreaTagRegistrySO.Instance;
                    if (ar?.areaTags != null) foreach (var t in ar.areaTags) set.Add(t);
                    break;
                case Kind.AnimationState:
                    var sr = AnimationStateRegistrySO.Instance;
                    if (sr?.states != null) foreach (var s in sr.states) set.Add(s);
                    break;
                case Kind.AnimationSequence:
                    var qr = AnimationSequenceRegistry.Instance;
                    if (qr?.sequences != null) foreach (var s in qr.sequences) if (s != null) set.Add(s.sequenceId);
                    break;
                case Kind.Prop:
                    var pp = PropRegistrySO.Instance;
                    if (pp != null) foreach (var id in pp.GetPropIds()) set.Add(id);
                    break;
                case Kind.Scene:
                    var scn = SceneRegistrySO.Instance;
                    if (scn != null) foreach (var id in scn.GetSceneIds()) set.Add(id);
                    break;
                case Kind.ContextTag:
                    if (InteractableCatalog.Instance != null) foreach (var t in InteractableCatalog.Instance.GetContextTags()) set.Add(t);
                    break;
                case Kind.Action:
                    if (InteractableCatalog.Instance != null) foreach (var a in InteractableCatalog.Instance.GetActionVocabulary()) set.Add(a);
                    break;
                case Kind.Item:
                    if (InteractableCatalog.Instance != null) foreach (var i in InteractableCatalog.Instance.GetItemIds()) set.Add(i);
                    break;
                case Kind.ObjectArchetype:
                    if (InteractableCatalog.Instance != null) foreach (var a in InteractableCatalog.Instance.GetArchetypeIds()) set.Add(a);
                    break;
            }
            return set;
        }

        /// <summary>
        /// Validate a flat list of (kind, id) references against the registries,
        /// given the set of ids the JSON explicitly declared as new per kind.
        /// </summary>
        public static Report Validate(
            IEnumerable<(Kind kind, string id)> references,
            IDictionary<Kind, HashSet<string>> declaredNew)
        {
            var report = new Report();
            var cache = new Dictionary<Kind, HashSet<string>>();
            foreach (var (kind, id) in references)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (!cache.TryGetValue(kind, out var exist)) { exist = Existing(kind); cache[kind] = exist; }
                if (exist.Contains(id)) { report.resolved.Add((kind, id)); continue; }
                bool declared = declaredNew != null && declaredNew.TryGetValue(kind, out var dn) && dn.Contains(id);
                if (declared) report.newDeclared.Add((kind, id));
                else report.unknown.Add((kind, id));
            }
            return report;
        }

        // ── non-destructive merge of declared-new ids ────────────────────────

        public static int MergeNew(Report report)
        {
            int created = 0;
            var catalog = InteractableCatalog.Instance;

            foreach (var (kind, id) in report.newDeclared.Distinct())
            {
                switch (kind)
                {
                    case Kind.Faction:
                        var fr = FactionRegistrySO.Instance;
                        if (fr != null && !fr.factions.Any(f => f != null && string.Equals(f.factionId, id, StringComparison.OrdinalIgnoreCase)))
                        { fr.factions.Add(new FactionEntry { factionId = id }); EditorUtility.SetDirty(fr); created++; }
                        break;
                    case Kind.PointTag:
                        created += AddString(InteractionPointRegistrySO.Instance, r => r.pointTags, id);
                        break;
                    case Kind.AreaTag:
                        created += AddString(AreaTagRegistrySO.Instance, r => r.areaTags, id);
                        break;
                    case Kind.AnimationState:
                        created += AddString(AnimationStateRegistrySO.Instance, r => r.states, id);
                        break;
                    case Kind.AnimationSequence:
                        var qr = AnimationSequenceRegistry.Instance;
                        if (qr != null && qr.FindSequence(id) == null)
                        { qr.sequences.Add(new ActionAnimSequence { sequenceId = id }); EditorUtility.SetDirty(qr); created++; }
                        break;
                    case Kind.Prop:
                        var pp = PropRegistrySO.Instance;
                        if (pp != null && pp.FindProp(id) == null)
                        { pp.props.Add(new PropEntry { propId = id }); EditorUtility.SetDirty(pp); created++; }
                        break;
                    case Kind.ContextTag:
                        if (catalog != null && !catalog.contextTags.Any(t => string.Equals(t, id, StringComparison.OrdinalIgnoreCase)))
                        { catalog.contextTags.Add(id); EditorUtility.SetDirty(catalog); created++; }
                        break;
                    case Kind.Action:
                        if (catalog != null && !catalog.actionVocabulary.Any(a => string.Equals(a, id, StringComparison.OrdinalIgnoreCase)))
                        { catalog.actionVocabulary.Add(id); EditorUtility.SetDirty(catalog); created++; }
                        break;
                    case Kind.Item:
                        if (catalog != null && !catalog.items.Any(i => i != null && string.Equals(i.itemId, id, StringComparison.OrdinalIgnoreCase)))
                        { catalog.items.Add(new InventoryItemDefinition { itemId = id, displayName = id }); EditorUtility.SetDirty(catalog); created++; }
                        break;
                    case Kind.ObjectArchetype:
                        if (catalog != null && !catalog.archetypes.Any(a => a != null && string.Equals(a.archetypeId, id, StringComparison.OrdinalIgnoreCase)))
                        { catalog.archetypes.Add(new InteractableArchetype { archetypeId = id, defaultName = id }); EditorUtility.SetDirty(catalog); created++; }
                        break;
                }
            }
            if (created > 0) AssetDatabase.SaveAssets();
            report.Created = created;
            return created;
        }

        private static int AddString<T>(T reg, Func<T, List<string>> sel, string id) where T : ScriptableObject
        {
            if (reg == null) return 0;
            var list = sel(reg);
            if (list == null) return 0;
            if (list.Any(s => string.Equals(s, id, StringComparison.OrdinalIgnoreCase))) return 0;
            list.Add(id);
            EditorUtility.SetDirty(reg);
            return 1;
        }
    }
}
#endif
