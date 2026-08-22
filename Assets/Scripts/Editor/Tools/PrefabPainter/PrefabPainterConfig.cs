using System;
using System.Collections.Generic;
using UnityEngine;

public enum PrefabPainterAttachmentSide
{
    [InspectorName("Назад (-Z)")]
    Back,

    [InspectorName("Вперёд (+Z)")]
    Forward,

    [InspectorName("Влево (-X)")]
    Left,

    [InspectorName("Вправо (+X)")]
    Right,

    [InspectorName("Вверх (+Y)")]
    Up,

    [InspectorName("Вниз (-Y)")]
    Down
}

[Serializable]
public sealed class PrefabPainterEntry
{
    public const string DefaultSectionName = "Основные";

    [Tooltip("Префаб, который будет доступен в библиотеке Prefab Painter.")]
    [SerializeField] private GameObject _prefab;

    [Tooltip("Разрешить размещение префаба только на почти вертикальных стенах.")]
    [SerializeField] private bool _wallOnly;

    [Tooltip("Локальная сторона префаба, которая должна быть обращена к стене или поверхности крепления. Для сторон ±Y локальная ось Z считается вертикальной опорой.")]
    [SerializeField] private PrefabPainterAttachmentSide _attachmentSide = PrefabPainterAttachmentSide.Back;

    [Tooltip("Индивидуальное локальное смещение после автоматического выравнивания и прилипания.")]
    [SerializeField] private Vector3 _localOffset;

    [Tooltip("Индивидуальная локальная поправка начального поворота префаба в градусах после автоматической ориентации.")]
    [SerializeField] private Vector3 _startRotationEuler;

    [SerializeField, HideInInspector] private string _section = DefaultSectionName;

    public GameObject Prefab => _prefab;
    public bool WallOnly => _wallOnly;
    public PrefabPainterAttachmentSide AttachmentSide => _attachmentSide;
    public Vector3 LocalOffset => _localOffset;
    public Vector3 StartRotationEuler => _startRotationEuler;
    public string Section => string.IsNullOrWhiteSpace(_section) ? DefaultSectionName : _section;

    internal void SetSection(string sectionName)
    {
        _section = sectionName;
    }
}

[CreateAssetMenu(
    fileName = nameof(PrefabPainterConfig),
    menuName = "Dreamcore/Rendering/Prefab Painter Config")]
public sealed class PrefabPainterConfig : ScriptableObject
{
    [Header("Библиотека")]
    [Tooltip("Префабы и индивидуальные параметры их размещения.")]
    [SerializeField, HideInInspector] private List<PrefabPainterEntry> _prefabs = new List<PrefabPainterEntry>();

    [SerializeField, HideInInspector]
    private List<string> _sections = new List<string> { PrefabPainterEntry.DefaultSectionName };

    [Header("Поверхности")]
    [Tooltip("Слои коллайдеров, на которых разрешено размещать префабы.")]
    [SerializeField] private LayerMask _surfaceMask = ~0;

    [Tooltip("Максимальный угол поверхности относительно мирового Up, который ещё считается полом или склоном.")]
    [Range(0f, 89f)]
    [SerializeField] private float _maxFloorSlopeAngle = 60f;

    [Tooltip("Максимальное отклонение поверхности от вертикальной стены.")]
    [Range(0f, 45f)]
    [SerializeField] private float _wallDeviationAngle = 15f;

    [Tooltip("Отступ от поверхности. Положительное значение создаёт зазор, отрицательное слегка утапливает prefab в поверхность.")]
    [SerializeField] private float _surfaceOffset = 0.01f;

    [Header("Прилипание к стене")]
    [Tooltip("Максимальное расстояние, на котором напольный объект ищет ближайшую стену.")]
    [Min(0.01f)]
    [SerializeField] private float _nearbyWallDistance = 2f;

    [Tooltip("Высота точки, из которой напольный объект ищет ближайшую стену.")]
    [Min(0f)]
    [SerializeField] private float _nearbyWallProbeHeight = 0.5f;

    [Header("Измерение высоты")]
    [Tooltip("Максимальная дистанция поиска пола под настенным объектом.")]
    [Min(0.01f)]
    [SerializeField] private float _floorProbeDistance = 50f;

    [Header("Стирание")]
    [Tooltip("Экранный радиус в пикселях для выбора мелких объектов в режиме стирания.")]
    [Range(2, 40)]
    [SerializeField] private int _eraserPickRadiusPixels = 14;

    [Header("Случайное вращение")]
    [Tooltip("Диапазон случайного YAW для напольного объекта, рядом с которым не найдена стена.")]
    [SerializeField] private Vector2 _randomYawRange = new Vector2(0f, 360f);

    public IReadOnlyList<PrefabPainterEntry> Prefabs => _prefabs;
    public IReadOnlyList<string> Sections => _sections;
    public LayerMask SurfaceMask => _surfaceMask;
    public float MaxFloorSlopeAngle => Mathf.Clamp(_maxFloorSlopeAngle, 0f, 89f);
    public float WallDeviationAngle => Mathf.Clamp(_wallDeviationAngle, 0f, 45f);
    public float SurfaceOffset => _surfaceOffset;
    public float NearbyWallDistance => Mathf.Max(0.01f, _nearbyWallDistance);
    public float NearbyWallProbeHeight => Mathf.Max(0f, _nearbyWallProbeHeight);
    public float FloorProbeDistance => Mathf.Max(0.01f, _floorProbeDistance);
    public int EraserPickRadiusPixels => Mathf.Clamp(_eraserPickRadiusPixels, 2, 40);
    public Vector2 RandomYawRange => _randomYawRange;

    private void OnValidate()
    {
        _maxFloorSlopeAngle = Mathf.Clamp(_maxFloorSlopeAngle, 0f, 89f);
        _wallDeviationAngle = Mathf.Clamp(_wallDeviationAngle, 0f, 45f);
        _nearbyWallDistance = Mathf.Max(0.01f, _nearbyWallDistance);
        _nearbyWallProbeHeight = Mathf.Max(0f, _nearbyWallProbeHeight);
        _floorProbeDistance = Mathf.Max(0.01f, _floorProbeDistance);
        _eraserPickRadiusPixels = Mathf.Clamp(_eraserPickRadiusPixels, 2, 40);

        if (_randomYawRange.x > _randomYawRange.y)
        {
            (_randomYawRange.x, _randomYawRange.y) = (_randomYawRange.y, _randomYawRange.x);
        }

        NormalizeSections();
        RemoveDuplicatePrefabs();
    }

    private void NormalizeSections()
    {
        if (_prefabs == null)
        {
            _prefabs = new List<PrefabPainterEntry>();
        }

        if (_sections == null)
        {
            _sections = new List<string>();
        }

        HashSet<string> uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _sections.Count; i++)
        {
            string sectionName = string.IsNullOrWhiteSpace(_sections[i])
                ? $"Раздел {i + 1}"
                : _sections[i].Trim();
            string uniqueName = sectionName;
            int suffix = 2;
            while (!uniqueNames.Add(uniqueName))
            {
                uniqueName = $"{sectionName} {suffix}";
                suffix++;
            }

            _sections[i] = uniqueName;
        }

        if (_sections.Count == 0)
        {
            _sections.Add(PrefabPainterEntry.DefaultSectionName);
        }

        HashSet<string> availableSections = new HashSet<string>(_sections, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _prefabs.Count; i++)
        {
            PrefabPainterEntry entry = _prefabs[i];
            if (entry == null || availableSections.Contains(entry.Section))
            {
                continue;
            }

            entry.SetSection(_sections[0]);
        }
    }

    private void RemoveDuplicatePrefabs()
    {
        if (_prefabs == null || _prefabs.Count < 2)
        {
            return;
        }

        HashSet<GameObject> uniquePrefabs = new HashSet<GameObject>();
        for (int i = 0; i < _prefabs.Count; i++)
        {
            PrefabPainterEntry entry = _prefabs[i];
            GameObject prefab = entry != null ? entry.Prefab : null;
            if (prefab == null || uniquePrefabs.Add(prefab))
            {
                continue;
            }

            _prefabs.RemoveAt(i);
            i--;
        }
    }
}
