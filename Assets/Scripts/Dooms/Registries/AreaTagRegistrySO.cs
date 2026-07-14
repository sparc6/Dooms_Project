using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms.Registries
{
    [CreateAssetMenu(fileName = "AreaTagRegistry", menuName = "DOOMS/Area Tag Registry")]
    public class AreaTagRegistrySO : ScriptableObject
    {
        private static AreaTagRegistrySO _instance;
        public static AreaTagRegistrySO Instance
        {
            get
            {
#if UNITY_EDITOR
                // A1.1: canonical location under Resources/Dooms (single source of truth).
                const string kAuth = "Assets/Resources/Dooms/DoomsAreaTagRegistry.asset";
                var reg = UnityEditor.AssetDatabase.LoadAssetAtPath<AreaTagRegistrySO>(kAuth);
                if (reg != null) return reg;
                var guids = UnityEditor.AssetDatabase.FindAssets("t:AreaTagRegistrySO");
                foreach (var guid in guids)
                {
                    reg = UnityEditor.AssetDatabase.LoadAssetAtPath<AreaTagRegistrySO>(
                              UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (reg != null) return reg;
                }
                return null;
#else
                if (_instance == null)
                {
                    _instance = Resources.Load<AreaTagRegistrySO>("Dooms/DoomsAreaTagRegistry");
                    if (_instance == null)
                    {
                        var assets = Resources.FindObjectsOfTypeAll<AreaTagRegistrySO>();
                        if (assets.Length > 0) _instance = assets[0];
                    }
                }
                return _instance;
#endif
            }
        }

        [Tooltip("List of all valid tags for AreaAnchors in the scene.")]
        public List<string> areaTags = new List<string>
        {
            "PatrolZone",
            "ProtestZone",
            "MingleZone",
            "MarketArea"
        };
    }
}
