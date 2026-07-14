using UnityEngine;

namespace MLA_SIM.Dooms
{
    /// <summary>
    /// Inspector dropdown backed by AmbientMoodProfileSO hostile/tense tags.
    /// Falls back to text input when no mood profile instance is available.
    /// </summary>
    public class MoodTagDropdownAttribute : PropertyAttribute
    {
    }
}
