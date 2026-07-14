using UnityEngine;

namespace MLA_SIM
{
    /// <summary>
    /// Interface for objects that can provide a name/description
    /// </summary>
    public interface INameProvider
    {
        string GetObjectName();
    }
} 