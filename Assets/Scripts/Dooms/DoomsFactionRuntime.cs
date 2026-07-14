using UnityEngine;

namespace MLA_SIM.Dooms
{
    public static class DoomsFactionRuntime
    {
        public static string EffectiveFactionOf(DoomsAgentTag tag)
        {
            if (tag == null) return "";

            var persona = tag.GetComponent<DoomsAgentPersona>();
            if (persona != null && !string.IsNullOrEmpty(persona.EffectiveFaction))
                return persona.EffectiveFaction;

            return tag.factionId;
        }
    }
}
