#if UNITY_EDITOR
using MLA_SIM.EditorTools;

namespace MLA_SIM.ModularInteractions
{
    internal static class SceneObjectNodeEditor
    {
        public static string[] GetRegistryOptions(RegistryType type)
        {
            return SogRegistryProvider.GetOptions(type);
        }
    }
}
#endif
