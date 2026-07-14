using System.Collections.Generic;
using UnityEngine;
using MLA_SIM.Dooms.Scenes;

namespace MLA_SIM.Dooms.Registries
{
    [CreateAssetMenu(fileName = "SceneRegistry", menuName = "DOOMS/Scene Registry")]
    public class SceneRegistrySO : ScriptableObject
    {
        private static SceneRegistrySO _instance;
        public static SceneRegistrySO Instance
        {
            get
            {
#if UNITY_EDITOR
                // Editor: always return the live in-memory asset so inspector edits
                // are reflected immediately without domain reload.
                // A1.1: canonical location under Resources/Dooms (single source of truth).
                const string kAuth = "Assets/Resources/Dooms/DoomsSceneRegistry.asset";
                var reg = UnityEditor.AssetDatabase.LoadAssetAtPath<SceneRegistrySO>(kAuth);
                if (reg != null) return reg;
                var guids = UnityEditor.AssetDatabase.FindAssets("t:SceneRegistrySO");
                foreach (var guid in guids)
                {
                    reg = UnityEditor.AssetDatabase.LoadAssetAtPath<SceneRegistrySO>(
                              UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (reg != null) return reg;
                }
                return null;
#else
                if (_instance == null)
                {
                    _instance = Resources.Load<SceneRegistrySO>("Dooms/DoomsSceneRegistry");
                    if (_instance == null)
                    {
                        var assets = Resources.FindObjectsOfTypeAll<SceneRegistrySO>();
                        if (assets.Length > 0) _instance = assets[0];
                    }
                }
                return _instance;
#endif
            }
        }

        [Tooltip("List of all active scenes registered in the DOOMS narrative engine.")]
        public List<SceneSO> scenes = new List<SceneSO>();

        public string[] GetSceneIds()
        {
            if (scenes == null) return new string[0];
            var list = new List<string>();
            foreach (var s in scenes)
            {
                if (s != null && !string.IsNullOrWhiteSpace(s.sceneId))
                    list.Add(s.sceneId);
            }
            return list.ToArray();
        }
    }
}
