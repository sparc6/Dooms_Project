using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms.Registries
{
    [System.Serializable]
    public class FactionEntry
    {
        public string factionId;
        public Color displayColor = Color.white;
        public string description;
    }

    [CreateAssetMenu(fileName = "FactionRegistry", menuName = "DOOMS/Faction Registry")]
    public class FactionRegistrySO : ScriptableObject
    {
        private static FactionRegistrySO _instance;
        public static FactionRegistrySO Instance
        {
            get
            {
#if UNITY_EDITOR
                // Editor: always return the live in-memory asset so inspector edits
                // (add/remove factions) are reflected immediately without domain reload.
                // AssetDatabase.LoadAssetAtPath is free — Unity caches in-memory objects.
                // A1.1: canonical location under Resources/Dooms (single source of truth).
                const string kAuth = "Assets/Resources/Dooms/DoomsFactionRegistry.asset";
                var reg = UnityEditor.AssetDatabase.LoadAssetAtPath<FactionRegistrySO>(kAuth);
                if (reg != null) return reg;
                var guids = UnityEditor.AssetDatabase.FindAssets("t:FactionRegistrySO");
                foreach (var guid in guids)
                {
                    reg = UnityEditor.AssetDatabase.LoadAssetAtPath<FactionRegistrySO>(
                              UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (reg != null) return reg;
                }
                return null;
#else
                if (_instance == null)
                {
                    _instance = Resources.Load<FactionRegistrySO>("Dooms/DoomsFactionRegistry");
                    if (_instance == null)
                    {
                        var assets = Resources.FindObjectsOfTypeAll<FactionRegistrySO>();
                        if (assets.Length > 0) _instance = assets[0];
                    }
                }
                return _instance;
#endif
            }
        }

        [Tooltip("List of all recognized factions in the DOOMS system.")]
        public List<FactionEntry> factions = new List<FactionEntry>
        {
            new FactionEntry { factionId = "Guard", displayColor = Color.blue, description = "Security forces monitoring order." },
            new FactionEntry { factionId = "Luddite", displayColor = Color.red, description = "Anti-technology protesters." },
            new FactionEntry { factionId = "Builder", displayColor = Color.yellow, description = "Construction workers." },
            new FactionEntry { factionId = "Server", displayColor = Color.cyan, description = "AI and system nodes." },
            new FactionEntry { factionId = "Clone", displayColor = Color.magenta, description = "Cloned workers." },
            new FactionEntry { factionId = "Civilian", displayColor = Color.gray, description = "Neutral bystanders." }
        };
    }
}
