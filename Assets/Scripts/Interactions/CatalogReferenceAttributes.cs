using System;
using UnityEngine;

namespace MLA_SIM.Interactions
{
    /// <summary>
    /// Marks a string field that should be rendered as a dropdown populated
    /// from the bound InteractableCatalog's archetype list. Editor-only UX;
    /// at runtime the field is still a plain string (archetypeId).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class CatalogArchetypeIdAttribute : PropertyAttribute { }

    /// <summary>
    /// Marks a string field that should be rendered as a dropdown populated
    /// from the bound InteractableCatalog's item list. Editor-only UX;
    /// at runtime the field is still a plain string (itemId).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class CatalogItemIdAttribute : PropertyAttribute { }

    /// <summary>
    /// Marks a string field that should be rendered as a dropdown of every
    /// in-scene GameObject that carries an EventSource component. Editor-only
    /// UX; at runtime the field is a plain string (GameObject name) resolved
    /// via EventSource.FindByName.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SceneEventSourceNameAttribute : PropertyAttribute { }
}
