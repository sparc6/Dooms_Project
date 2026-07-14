#if UNITY_EDITOR
using System;

namespace MLA_SIM.Dooms.Editor
{
    /// <summary>
    /// Reserved helper container for NodeCanvas-specific SOG editor utilities.
    /// The string-array registry drawing is now handled by RegistryDropdownNCDrawer
    /// to avoid duplicate drawer registrations.
    /// </summary>
    public static class SogStringArrayNCDrawer
    {
        public static string[] SplitCsv(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return Array.Empty<string>();

            return raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
#endif
