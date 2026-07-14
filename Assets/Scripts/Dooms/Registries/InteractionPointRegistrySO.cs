using System.Collections.Generic;
using UnityEngine;

namespace MLA_SIM.Dooms.Registries
{
    [CreateAssetMenu(fileName = "InteractionPointRegistry", menuName = "DOOMS/Interaction Point Registry")]
    public class InteractionPointRegistrySO : ScriptableObject
    {
        private static InteractionPointRegistrySO _instance;
        public static InteractionPointRegistrySO Instance
        {
            get
            {
#if UNITY_EDITOR
                // Editor: always return the live in-memory asset so inspector edits
                // are reflected immediately without domain reload.
                // A1.1: canonical location under Resources/Dooms (single source of truth).
                const string kAuth = "Assets/Resources/Dooms/DoomsInteractionPointRegistry.asset";
                var reg = UnityEditor.AssetDatabase.LoadAssetAtPath<InteractionPointRegistrySO>(kAuth);
                if (reg != null) return reg;
                var guids = UnityEditor.AssetDatabase.FindAssets("t:InteractionPointRegistrySO");
                foreach (var guid in guids)
                {
                    reg = UnityEditor.AssetDatabase.LoadAssetAtPath<InteractionPointRegistrySO>(
                              UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (reg != null) return reg;
                }
                return null;
#else
                if (_instance == null)
                {
                    _instance = Resources.Load<InteractionPointRegistrySO>("Dooms/DoomsInteractionPointRegistry");
                    if (_instance == null)
                    {
                        var assets = Resources.FindObjectsOfTypeAll<InteractionPointRegistrySO>();
                        if (assets.Length > 0) _instance = assets[0];
                    }
                }
                return _instance;
#endif
            }
        }

        [Tooltip("List of all valid tags for interaction points/anchors in the scene.")]
        public List<string> pointTags = new List<string>
        {
            "ProtestLine",
            "GuardLine",
            "WorkBench",
            "Bar",
            "Bed",
            "SocialSpot",
            "Observer",
            "HidingSpot",
            "MarketStall",
            "ServerConsole",
            "PatrolRoute",
            "Throne",
            "Stage",
            "VisionAltar",
            "Pulpit",
            "EvacuationPoint",
            "DetentionZone"
        };
    }
}
