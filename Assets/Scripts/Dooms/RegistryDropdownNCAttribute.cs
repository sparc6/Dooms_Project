using ParadoxNotion.Design;

namespace MLA_SIM.Dooms
{
    // ParadoxNotion-compatible companion to RegistryDropdownAttribute.
    // Apply this alongside [RegistryDropdown] on RoleSlot fields so that
    // NodeCanvas's PropertyDrawerFactory renders registry popups in the canvas panel.
    public class RegistryDropdownNCAttribute : DrawerAttribute
    {
        public readonly RegistryType registryType;

        public RegistryDropdownNCAttribute(RegistryType type)
        {
            registryType = type;
        }
    }
}
