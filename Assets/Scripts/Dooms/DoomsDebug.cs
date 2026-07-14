using System;
using UnityEngine;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Unified diagnostic logger for the DOOMS pipeline.
    ///
    /// All messages are gated behind <see cref="Enabled"/> and an optional
    /// per-category filter so you can focus on specific subsystems without noise.
    ///
    /// Wire a <see cref="DoomsDebugController"/> MonoBehaviour into the scene to
    /// toggle flags from the Inspector at runtime without recompiling.
    /// </summary>
    public static class DoomsDebug
    {
        [Flags]
        public enum Category
        {
            None           = 0,
            SceneDirector  = 1 << 0,
            Extras         = 1 << 1,
            ActivitySelect = 1 << 2,
            Ambient        = 1 << 3,
            Encounter      = 1 << 4,
            Nav            = 1 << 5,
            All            = ~0,
        }

        /// <summary>Master on/off switch. Off by default; toggled by DoomsDebugController.</summary>
        public static bool Enabled { get; set; } = false;

        /// <summary>Category filter — only active categories emit messages.</summary>
        public static Category EnabledCategories { get; set; } = Category.All;

        /// <summary>Emit a log if <see cref="Enabled"/> and the category is active.</summary>
        public static void Log(Category category, string message)
        {
            if (!Enabled) return;
            if ((EnabledCategories & category) == 0) return;
            Debug.Log($"[DoomsDirector][{category}] {message}");
        }

        /// <summary>Emit a warning if <see cref="Enabled"/> and the category is active.</summary>
        public static void LogWarn(Category category, string message)
        {
            if (!Enabled) return;
            if ((EnabledCategories & category) == 0) return;
            Debug.LogWarning($"[DoomsDirector][{category}] {message}");
        }
    }
}
