using UnityEngine;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Drop this MonoBehaviour onto any scene GameObject to control
    /// <see cref="DoomsDebug"/> flags from the Inspector at runtime.
    ///
    /// Changes take effect immediately on Awake, OnEnable, and (in the editor)
    /// OnValidate, giving live updates during Play-mode tweaking.
    /// </summary>
    [AddComponentMenu("DOOMS/Debug Controller")]
    public class DoomsDebugController : MonoBehaviour
    {
        [Tooltip("Master toggle for all [DoomsDirector] diagnostic messages.")]
        public bool enableDebug = false;

        [Tooltip("Which pipeline subsystems to trace. Use 'All' for full pipeline visibility.")]
        public DoomsDebug.Category categories = DoomsDebug.Category.All;

        private void Awake()    => Apply();
        private void OnEnable() => Apply();

#if UNITY_EDITOR
        private void OnValidate() => Apply();
#endif

        private void Apply()
        {
            DoomsDebug.Enabled           = enableDebug;
            DoomsDebug.EnabledCategories = categories;
        }
    }
}
