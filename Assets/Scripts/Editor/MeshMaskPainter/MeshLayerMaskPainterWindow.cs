using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheTower.EditorTools.MeshMaskPainter
{
    internal sealed class MeshLayerMaskPainterWindow : EditorWindow
    {
        private const string PaintShaderName = "Hidden/TheTower/MeshLayerMaskPainter";
        private const int SceneControlHint = 0x4D4C4D50;
        private static readonly int[] Resolutions = { 512, 1024, 2048, 4096 };
        private static readonly string[] ResolutionLabels = { "512", "1024", "2048", "4096" };
        private static readonly string[] ToolLabels = { "Paint", "Raise", "Lower", "Smooth", "Grab" };
        private static readonly Color[] LayerColors =
        {
            new Color(0.92f, 0.92f, 0.92f),
            new Color(1f, 0.25f, 0.25f),
            new Color(0.25f, 1f, 0.35f),
            new Color(0.25f, 0.55f, 1f)
        };
        private static readonly Color[] SculptColors =
        {
            new Color(0.25f, 1f, 0.45f),
            new Color(1f, 0.3f, 0.25f),
            new Color(1f, 0.85f, 0.2f),
            new Color(0.25f, 0.85f, 1f)
        };

        private readonly MeshLayerMaskRaycaster _raycaster = new MeshLayerMaskRaycaster();
        private readonly MeshPainterCombinedHistory _history = new MeshPainterCombinedHistory();

        private GameObject _targetObject;
        private int _materialSlot;
        private Texture2D _sourceTexture;
        private MeshLayerMaskPainterSession _paintSession;
        private MeshSculptSession _sculptSession;
        private Vector2 _scroll;
        private int _resolutionIndex = 2;
        private int _selectedLayer;
        private int _selectedTool;
        private float _brushRadius = 1f;
        private float _brushHardness = 0.7f;
        private float _paintStrength = 1f;
        private float _sculptStrength = 0.25f;
        private float _brushSpacing = 0.2f;
        private float _brushMaximumAngle = 85f;
        private float _brushDepthScale = 1f;
        private bool _editMode;
        private bool _strokeActive;
        private MeshPainterHistoryKind _strokeKind;
        private Vector3 _lastStampPosition;
        private Vector3 _lastStampNormal;
        private Vector2 _lastStampUv;
        private Vector2 _lastMousePosition;
        private Plane _grabPlane;
        private Vector3 _grabStartPoint;
        private int _affectedVertexCount;
        private bool _sharedMaterialWarningConfirmed;

        [MenuItem("Tools/Art/Mesh Layer Mask Painter")]
        private static void OpenWindow()
        {
            MeshLayerMaskPainterWindow window = GetWindow<MeshLayerMaskPainterWindow>();
            window.titleContent = new GUIContent("Mesh Painter");
            window.minSize = new Vector2(440f, 680f);
            if (Selection.activeGameObject)
                window.TrySwitchTarget(Selection.activeGameObject, 0);
            window.Show();
        }

        private bool IsPaintTool => _selectedTool == 0;
        private MeshSculptTool CurrentSculptTool => (MeshSculptTool)Mathf.Clamp(_selectedTool - 1, 0, 3);

        private void OnEnable()
        {
            titleContent = new GUIContent("Mesh Painter");
            minSize = new Vector2(440f, 680f);
            saveChangesMessage = "В Mesh Painter есть несохранённые изменения маски или геометрии.";
            SceneView.duringSceneGui += OnSceneGui;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            EndStroke();
            ReleaseSessions();
        }

        public override void SaveChanges()
        {
            if (SaveAll())
                base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            ReleaseSessions();
            hasUnsavedChanges = false;
            base.DiscardChanges();
        }

        private void OnGUI()
        {
            HandleHistoryKeyboard(Event.current);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawTargetSection();
            EditorGUILayout.Space(6f);
            DrawTextureSection();
            EditorGUILayout.Space(6f);
            DrawMeshSaveSection();
            EditorGUILayout.Space(6f);
            DrawBrushSection();
            EditorGUILayout.Space(6f);
            DrawPreviewSection();
            EditorGUILayout.EndScrollView();
        }

        private void DrawTargetSection()
        {
            EditorGUILayout.LabelField("Цель", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GameObject candidate = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("Объект", "Статический меш с MeshFilter и MeshRenderer."),
                    _targetObject,
                    typeof(GameObject),
                    true);
                if (candidate != _targetObject)
                    TrySwitchTarget(candidate, 0);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Использовать выделенный", GUILayout.Width(190f)))
                        TrySwitchTarget(Selection.activeGameObject, 0);
                }

                int maximumSlot = MeshLayerMaskValidation.GetMaximumSlot(_targetObject);
                int candidateSlot = EditorGUILayout.IntSlider(
                    new GUIContent("Material Slot", "Ограничивает Paint; Sculpt работает по всему мешу."),
                    Mathf.Clamp(_materialSlot, 0, maximumSlot),
                    0,
                    maximumSlot);
                if (candidateSlot != _materialSlot)
                    TrySwitchTarget(_targetObject, candidateSlot);

                if (TryGetGeometryTarget(out MeshLayerMaskTarget geometryTarget, out string geometryMessage))
                {
                    EditorGUILayout.LabelField("Mesh", geometryTarget.Mesh.name);
                    EditorGUILayout.LabelField("Вершины", geometryTarget.Mesh.vertexCount.ToString());
                    if (TryGetPaintTarget(out MeshLayerMaskTarget paintTarget, out string paintMessage))
                    {
                        EditorGUILayout.LabelField("Материал", paintTarget.Material.name);
                        EditorGUILayout.LabelField("Слои", MeshLayerMaskUtility.GetLayerCount(paintTarget.Material).ToString());
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Paint недоступен: " + paintMessage + " Sculpt остаётся доступен.", MessageType.Info);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(geometryMessage, MessageType.Warning);
                }
            }
        }

        private void DrawTextureSection()
        {
            EditorGUILayout.LabelField("Текстура маски", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(_paintSession == null))
                {
                    _resolutionIndex = EditorGUILayout.Popup(
                        new GUIContent("Новая маска", "Разрешение создаваемой RGBA-маски."),
                        _resolutionIndex,
                        ResolutionLabels);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("New"))
                            CreateNewTexture(Resolutions[_resolutionIndex]);

                        Texture2D assigned = GetAssignedTexture();
                        using (new EditorGUI.DisabledScope(!assigned))
                        {
                            if (GUILayout.Button("Load from Material"))
                            {
                                _sourceTexture = assigned;
                                LoadTexture(assigned);
                            }
                        }
                    }

                    _sourceTexture = (Texture2D)EditorGUILayout.ObjectField(
                        new GUIContent("Исходная маска", "Можно загрузить любую Texture2D; Save As создаёт PNG в Assets."),
                        _sourceTexture,
                        typeof(Texture2D),
                        false);
                    using (new EditorGUI.DisabledScope(!_sourceTexture))
                    {
                        if (GUILayout.Button("Load Selected"))
                            LoadTexture(_sourceTexture);
                    }

                    using (new EditorGUI.DisabledScope(_paintSession?.HasTexture != true))
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Save Mask"))
                            SaveMask(saveAs: false);
                        if (GUILayout.Button("Save Mask As…"))
                            SaveMask(saveAs: true);
                    }
                }

                if (_paintSession?.HasTexture == true)
                {
                    string path = string.IsNullOrEmpty(_paintSession.AssetPath) ? "ещё не сохранена" : _paintSession.AssetPath;
                    EditorGUILayout.LabelField($"{_paintSession.Width} × {_paintSession.Height} — {path}", EditorStyles.miniLabel);
                }
            }
        }

        private void DrawMeshSaveSection()
        {
            EditorGUILayout.LabelField("Деформированный Mesh", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(_sculptSession == null))
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(_sculptSession?.IsDirty != true))
                    {
                        if (GUILayout.Button("Save Mesh"))
                            SaveMesh(saveAs: false);
                    }
                    if (GUILayout.Button("Save Mesh As…"))
                        SaveMesh(saveAs: true);
                }

                if (_sculptSession != null)
                {
                    string path = string.IsNullOrEmpty(_sculptSession.AssetPath)
                        ? "первое сохранение создаст отдельный .asset"
                        : _sculptSession.AssetPath;
                    EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
                    if (_sculptSession.ColliderSyncSkipped)
                        EditorGUILayout.HelpBox("MeshCollider использует другой меш и не был изменён.", MessageType.Warning);
                }

                using (new EditorGUI.DisabledScope(!hasUnsavedChanges))
                {
                    if (GUILayout.Button("Save All"))
                        SaveAll();
                }
            }
        }

        private void DrawBrushSection()
        {
            EditorGUILayout.LabelField("Инструмент и кисть", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                int nextTool = GUILayout.Toolbar(_selectedTool, ToolLabels);
                if (nextTool != _selectedTool)
                {
                    EndStroke();
                    _selectedTool = nextTool;
                    _affectedVertexCount = 0;
                    SceneView.RepaintAll();
                }

                if (IsPaintTool)
                    DrawPaintControls();
                else
                    DrawSculptControls();

                _brushRadius = EditorGUILayout.Slider(
                    new GUIContent("Радиус", "Размер кисти в мировых Unity units."),
                    _brushRadius,
                    0.01f,
                    100f);
                _brushHardness = EditorGUILayout.Slider(
                    new GUIContent("Жёсткость", "Доля радиуса с полным воздействием."),
                    _brushHardness,
                    0f,
                    1f);
                if (CurrentSculptTool != MeshSculptTool.Grab || IsPaintTool)
                {
                    _brushSpacing = EditorGUILayout.Slider(
                        new GUIContent("Интервал", "Расстояние между отпечатками как доля радиуса."),
                        _brushSpacing,
                        0.05f,
                        1f);
                }

                bool toolAvailable = IsPaintTool
                    ? _paintSession?.HasTexture == true
                    : _sculptSession != null;
                using (new EditorGUI.DisabledScope(!toolAvailable))
                {
                    bool nextEditMode = GUILayout.Toggle(
                        _editMode,
                        _editMode ? "Scene Editing: ON" : "Scene Editing: OFF",
                        "Button",
                        GUILayout.Height(32f));
                    if (nextEditMode != _editMode)
                    {
                        if (!nextEditMode)
                            EndStroke();
                        _editMode = nextEditMode;
                        SceneView.RepaintAll();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!_history.CanUndo))
                    {
                        if (GUILayout.Button($"Undo ({_history.UndoCount})"))
                            UndoStroke();
                    }
                    using (new EditorGUI.DisabledScope(!_history.CanRedo))
                    {
                        if (GUILayout.Button($"Redo ({_history.RedoCount})"))
                            RedoStroke();
                    }
                }

                if (!IsPaintTool)
                {
                    MessageType type = _affectedVertexCount == 0 ? MessageType.Warning : MessageType.Info;
                    string message = _affectedVertexCount == 0
                        ? "В радиусе нет вершин. Увеличьте радиус или уплотните исходную геометрию."
                        : $"Затрагивается вершин: {_affectedVertexCount}. Радиус считается вдоль связной поверхности.";
                    EditorGUILayout.HelpBox(message, type);
                }

                EditorGUILayout.HelpBox(
                    "ЛКМ применяет инструмент в Scene View. Alt оставляет управление камерой. Ctrl+Z/Ctrl+Y управляют общей историей Paint/Sculpt.",
                    MessageType.Info);
            }
        }

        private void DrawPaintControls()
        {
            int layerCount = GetLayerCount();
            _selectedLayer = Mathf.Clamp(_selectedLayer, 0, Mathf.Max(0, layerCount - 1));
            EditorGUILayout.LabelField("Слой", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int index = 0; index < layerCount; index++)
                {
                    Color previous = GUI.backgroundColor;
                    GUI.backgroundColor = LayerColors[index];
                    if (GUILayout.Toggle(_selectedLayer == index, GetLayerLabel(index), "Button"))
                        _selectedLayer = index;
                    GUI.backgroundColor = previous;
                }
            }

            _paintStrength = EditorGUILayout.Slider("Сила Paint", _paintStrength, 0.01f, 1f);
            _brushMaximumAngle = EditorGUILayout.Slider("Допуск угла", _brushMaximumAngle, 0f, 90f);
            _brushDepthScale = EditorGUILayout.Slider("Глубина проекции", _brushDepthScale, 0.05f, 2f);
            if (_paintSession == null)
                EditorGUILayout.HelpBox("Выбранный материал несовместим с Paint.", MessageType.Warning);
            else if (!_paintSession.HasTexture)
                EditorGUILayout.HelpBox("Создайте или загрузите текстурную маску.", MessageType.Info);
        }

        private void DrawSculptControls()
        {
            _sculptStrength = EditorGUILayout.Slider(
                new GUIContent("Сила Sculpt", "Raise/Lower: до 10% радиуса за отпечаток; Grab масштабирует перемещение."),
                _sculptStrength,
                0.01f,
                1f);
        }

        private void DrawPreviewSection()
        {
            if (_paintSession?.HasTexture != true)
                return;

            EditorGUILayout.LabelField("Предпросмотр RGBA", EditorStyles.boldLabel);
            float width = Mathf.Max(1f, position.width - 38f);
            float aspect = (float)_paintSession.Height / _paintSession.Width;
            Rect previewRect = GUILayoutUtility.GetRect(width, Mathf.Min(280f, width * aspect), GUILayout.ExpandWidth(true));
            EditorGUI.DrawPreviewTexture(previewRect, _paintSession.CurrentTexture, null, ScaleMode.ScaleToFit);
        }

        private void OnSceneGui(SceneView sceneView)
        {
            if (!_editMode || _sculptSession == null || (IsPaintTool && _paintSession?.HasTexture != true))
                return;

            Event current = Event.current;
            HandleHistoryKeyboard(current);
            int controlId = GUIUtility.GetControlID(SceneControlHint, FocusType.Passive);
            if (current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            Ray mouseRay = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            bool hasHit = IsPaintTool
                ? _raycaster.Raycast(mouseRay, out RaycastHit hit)
                : _raycaster.RaycastAll(mouseRay, out hit);

            if (current.type == EventType.Repaint && hasHit)
                DrawSceneBrush(hit);

            if (current.alt)
                return;

            if (current.type == EventType.MouseDown && current.button == 0 && hasHit)
            {
                GUIUtility.hotControl = controlId;
                _strokeActive = true;
                _lastStampPosition = hit.point;
                _lastStampNormal = hit.normal;
                _lastStampUv = hit.textureCoord;
                _lastMousePosition = current.mousePosition;
                _history.PrepareNewStroke(_paintSession, _sculptSession);

                if (IsPaintTool)
                {
                    _strokeKind = MeshPainterHistoryKind.Paint;
                    _paintSession.BeginStroke();
                    StampPaint(hit);
                }
                else
                {
                    _strokeKind = MeshPainterHistoryKind.Sculpt;
                    _sculptSession.BeginStroke();
                    if (CurrentSculptTool == MeshSculptTool.Grab)
                    {
                        Vector3 planeNormal = sceneView.camera ? sceneView.camera.transform.forward : mouseRay.direction;
                        _grabPlane = new Plane(planeNormal, hit.point);
                        _grabStartPoint = hit.point;
                        if (_grabPlane.Raycast(mouseRay, out float enter))
                            _grabStartPoint = mouseRay.GetPoint(enter);
                        _affectedVertexCount = _sculptSession.BeginGrab(hit, _brushRadius, _brushHardness);
                    }
                    else
                    {
                        StampSculpt(hit);
                    }
                    RefreshSculptRaycaster();
                }
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 && _strokeActive)
            {
                if (_strokeKind == MeshPainterHistoryKind.Paint)
                {
                    if (hasHit)
                        ContinuePaintStroke(hit, current.mousePosition);
                }
                else if (CurrentSculptTool == MeshSculptTool.Grab)
                {
                    if (_grabPlane.Raycast(mouseRay, out float enter))
                    {
                        _affectedVertexCount = _sculptSession.ApplyGrab(mouseRay.GetPoint(enter) - _grabStartPoint, _sculptStrength);
                        hasUnsavedChanges = true;
                        RefreshSculptRaycaster();
                    }
                }
                else if (hasHit)
                {
                    ContinueSculptStroke(hit, current.mousePosition);
                    RefreshSculptRaycaster();
                }
                current.Use();
            }
            else if ((current.rawType == EventType.MouseUp || current.type == EventType.MouseUp) && current.button == 0 && _strokeActive)
            {
                EndStroke();
                GUIUtility.hotControl = 0;
                current.Use();
            }

            if (current.type == EventType.MouseMove || current.type == EventType.MouseDrag)
                sceneView.Repaint();
        }

        private void DrawSceneBrush(RaycastHit hit)
        {
            Color previous = Handles.color;
            Handles.color = IsPaintTool
                ? LayerColors[Mathf.Clamp(_selectedLayer, 0, 3)]
                : SculptColors[Mathf.Clamp(_selectedTool - 1, 0, SculptColors.Length - 1)];
            Handles.DrawWireDisc(hit.point, hit.normal, _brushRadius, 1.5f);

            if (!IsPaintTool && !_strokeActive)
            {
                Vector3[] affected = _sculptSession.GetAffectedWorldPositions(
                    hit,
                    _brushRadius,
                    _brushHardness,
                    out _affectedVertexCount);
                foreach (Vector3 position in affected)
                {
                    float size = HandleUtility.GetHandleSize(position) * 0.025f;
                    Handles.DotHandleCap(0, position, Quaternion.identity, size, EventType.Repaint);
                }
            }
            Handles.color = previous;
        }

        private void ContinuePaintStroke(RaycastHit hit, Vector2 mousePosition)
        {
            ContinueSpacedStroke(hit, mousePosition, StampPaint, restrictToMaterial: true);
        }

        private void ContinueSculptStroke(RaycastHit hit, Vector2 mousePosition)
        {
            ContinueSpacedStroke(hit, mousePosition, StampSculpt, restrictToMaterial: false);
        }

        private void ContinueSpacedStroke(RaycastHit hit, Vector2 mousePosition, Action<RaycastHit> stamp, bool restrictToMaterial)
        {
            float interval = Mathf.Max(0.0001f, _brushRadius * _brushSpacing);
            float distance = Vector3.Distance(_lastStampPosition, hit.point);
            if (distance < interval)
                return;

            int steps = Mathf.Max(1, Mathf.FloorToInt(distance / interval));
            Vector2 startMouse = _lastMousePosition;
            for (int step = 1; step <= steps; step++)
            {
                float t = Mathf.Min(1f, step * interval / distance);
                Vector2 sampleMouse = Vector2.Lerp(startMouse, mousePosition, t);
                Ray sampleRay = HandleUtility.GUIPointToWorldRay(sampleMouse);
                bool sampleHit = restrictToMaterial
                    ? _raycaster.Raycast(sampleRay, out RaycastHit interpolatedHit)
                    : _raycaster.RaycastAll(sampleRay, out interpolatedHit);
                if (!sampleHit)
                    continue;

                stamp(interpolatedHit);
                _lastStampPosition = interpolatedHit.point;
                _lastStampNormal = interpolatedHit.normal;
                _lastStampUv = interpolatedHit.textureCoord;
                _lastMousePosition = sampleMouse;
            }
        }

        private void StampPaint(RaycastHit hit)
        {
            _paintSession.Stamp(
                hit.point,
                hit.normal,
                hit.textureCoord,
                _selectedLayer,
                _brushRadius,
                _brushHardness,
                _paintStrength,
                _brushMaximumAngle,
                _brushDepthScale);
            hasUnsavedChanges = true;
            Repaint();
        }

        private void StampSculpt(RaycastHit hit)
        {
            _affectedVertexCount = _sculptSession.Stamp(
                hit,
                CurrentSculptTool,
                _brushRadius,
                _brushHardness,
                _sculptStrength);
            if (_affectedVertexCount > 0)
                hasUnsavedChanges = true;
            Repaint();
        }

        private void EndStroke()
        {
            if (!_strokeActive)
                return;

            _strokeActive = false;
            bool changed = _strokeKind == MeshPainterHistoryKind.Paint
                ? _paintSession?.EndStroke() == true
                : _sculptSession?.EndStroke() == true;
            if (changed)
                _history.RegisterCompleted(_strokeKind, _paintSession, _sculptSession);
            if (_strokeKind == MeshPainterHistoryKind.Sculpt)
                RefreshSculptRaycaster();
            SyncDirtyState();
            Repaint();
        }

        private void UndoStroke()
        {
            EndStroke();
            if (_history.Undo(_paintSession, _sculptSession))
            {
                RefreshSculptRaycaster();
                SyncDirtyState();
                Repaint();
            }
        }

        private void RedoStroke()
        {
            EndStroke();
            if (_history.Redo(_paintSession, _sculptSession))
            {
                RefreshSculptRaycaster();
                SyncDirtyState();
                Repaint();
            }
        }

        private void HandleHistoryKeyboard(Event current)
        {
            if (current == null || current.type != EventType.KeyDown || !(current.control || current.command))
                return;

            if (current.keyCode == KeyCode.Z)
            {
                if (current.shift)
                    RedoStroke();
                else
                    UndoStroke();
                current.Use();
            }
            else if (current.keyCode == KeyCode.Y)
            {
                RedoStroke();
                current.Use();
            }
        }

        private void CreateNewTexture(int resolution)
        {
            if (!ConfirmAbandonUnsavedChanges() || !RecreateSessions() || _paintSession == null)
                return;
            _paintSession.InitializeNew(resolution);
            _editMode = false;
            SyncDirtyState();
            Repaint();
        }

        private void LoadTexture(Texture2D texture)
        {
            if (!texture || !ConfirmAbandonUnsavedChanges() || !RecreateSessions() || _paintSession == null)
                return;
            _paintSession.Load(texture);
            _editMode = false;
            SyncDirtyState();
            Repaint();
        }

        private bool RecreateSessions()
        {
            ReleaseSessions();
            if (!TryGetGeometryTarget(out MeshLayerMaskTarget geometryTarget, out string message))
            {
                EditorUtility.DisplayDialog("Mesh Painter", message, "OK");
                return false;
            }

            _sculptSession = new MeshSculptSession(geometryTarget);
            _raycaster.Build(geometryTarget);

            if (TryGetPaintTarget(out MeshLayerMaskTarget paintTarget, out _))
            {
                Shader shader = Shader.Find(PaintShaderName);
                if (shader)
                {
                    _paintSession = new MeshLayerMaskPainterSession(paintTarget, shader);
                    _selectedLayer = Mathf.Clamp(_selectedLayer, 0, MeshLayerMaskUtility.GetLayerCount(paintTarget.Material) - 1);
                }
            }
            return true;
        }

        private bool SaveAll()
        {
            EndStroke();
            if (_sculptSession?.IsDirty == true && !SaveMesh(saveAs: false))
                return false;
            if (_paintSession?.IsDirty == true && !SaveMask(saveAs: false))
                return false;
            SyncDirtyState();
            return true;
        }

        private bool SaveMask(bool saveAs)
        {
            EndStroke();
            if (_paintSession?.HasTexture != true)
                return false;

            if (!_sharedMaterialWarningConfirmed)
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Изменение shared material",
                    "PNG будет назначена в общий material asset. Маска изменится на всех объектах, использующих этот материал.",
                    "Продолжить",
                    "Отмена");
                if (!confirmed)
                    return false;
                _sharedMaterialWarningConfirmed = true;
            }

            string assetPath = _paintSession.AssetPath;
            if (saveAs || string.IsNullOrEmpty(assetPath))
            {
                assetPath = EditorUtility.SaveFilePanelInProject(
                    "Сохранить Layer Mask",
                    GetDefaultTextureName(),
                    "png",
                    "Выберите путь внутри Assets.",
                    GetDefaultTextureDirectory());
                if (string.IsNullOrEmpty(assetPath))
                    return false;
            }

            try
            {
                _sourceTexture = _paintSession.SavePngAndAssign(assetPath);
                SyncDirtyState();
                Repaint();
                return true;
            }
            catch (Exception exception)
            {
                return ReportSaveException(exception);
            }
        }

        private bool SaveMesh(bool saveAs)
        {
            EndStroke();
            if (_sculptSession == null)
                return false;

            string assetPath = _sculptSession.AssetPath;
            if (saveAs || string.IsNullOrEmpty(assetPath))
            {
                assetPath = EditorUtility.SaveFilePanelInProject(
                    "Сохранить деформированный Mesh",
                    GetDefaultMeshName(),
                    "asset",
                    "Исходный FBX не изменяется. Выберите путь для отдельного Mesh asset.",
                    GetDefaultMeshDirectory());
                if (string.IsNullOrEmpty(assetPath))
                    return false;
            }

            try
            {
                _sculptSession.SaveMeshAssetAndAssign(assetPath);
                RefreshSculptRaycaster();
                SyncDirtyState();
                Repaint();
                return true;
            }
            catch (Exception exception)
            {
                return ReportSaveException(exception);
            }
        }

        private static bool ReportSaveException(Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Ошибка сохранения", exception.Message, "OK");
            return false;
        }

        private bool ConfirmAbandonUnsavedChanges()
        {
            EndStroke();
            if ((_paintSession?.IsDirty != true) && (_sculptSession?.IsDirty != true))
                return true;

            int choice = EditorUtility.DisplayDialogComplex(
                "Несохранённые изменения Mesh Painter",
                "Сохранить изменения маски и геометрии перед продолжением?",
                "Save All",
                "Cancel",
                "Discard");
            return choice switch
            {
                0 => SaveAll(),
                1 => false,
                _ => true
            };
        }

        private bool TrySwitchTarget(GameObject candidate, int materialSlot)
        {
            if (candidate == _targetObject && materialSlot == _materialSlot)
                return true;
            if (!ConfirmAbandonUnsavedChanges())
                return false;

            ReleaseSessions();
            _targetObject = candidate;
            _materialSlot = Mathf.Clamp(materialSlot, 0, MeshLayerMaskValidation.GetMaximumSlot(candidate));
            _sourceTexture = GetAssignedTexture();
            _sharedMaterialWarningConfirmed = false;
            hasUnsavedChanges = false;
            if (candidate)
                RecreateSessions();
            Repaint();
            return true;
        }

        private bool TryGetGeometryTarget(out MeshLayerMaskTarget target, out string message)
        {
            return MeshLayerMaskValidation.TryCreateGeometryTarget(_targetObject, _materialSlot, out target, out message);
        }

        private bool TryGetPaintTarget(out MeshLayerMaskTarget target, out string message)
        {
            return MeshLayerMaskValidation.TryCreateTarget(_targetObject, _materialSlot, out target, out message);
        }

        private int GetLayerCount()
        {
            return TryGetPaintTarget(out MeshLayerMaskTarget target, out _)
                ? MeshLayerMaskUtility.GetLayerCount(target.Material)
                : 0;
        }

        private Texture2D GetAssignedTexture()
        {
            return TryGetPaintTarget(out MeshLayerMaskTarget target, out _)
                ? target.Material.GetTexture(MeshLayerMaskUtility.LayerMaskProperty) as Texture2D
                : null;
        }

        private string GetDefaultTextureDirectory()
        {
            if (!TryGetPaintTarget(out MeshLayerMaskTarget target, out _))
                return "Assets";
            return GetAssetDirectory(AssetDatabase.GetAssetPath(target.Material));
        }

        private string GetDefaultMeshDirectory()
        {
            if (_sculptSession != null)
            {
                string path = string.IsNullOrEmpty(_sculptSession.AssetPath)
                    ? _sculptSession.SourceAssetPath
                    : _sculptSession.AssetPath;
                return GetAssetDirectory(path);
            }
            return TryGetGeometryTarget(out MeshLayerMaskTarget target, out _)
                ? GetAssetDirectory(AssetDatabase.GetAssetPath(target.Mesh))
                : "Assets";
        }

        private static string GetAssetDirectory(string path)
        {
            string directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
            return string.IsNullOrEmpty(directory) || !directory.StartsWith("Assets", StringComparison.Ordinal)
                ? "Assets"
                : directory;
        }

        private string GetDefaultTextureName()
        {
            return TryGetPaintTarget(out MeshLayerMaskTarget target, out _)
                ? target.Material.name + "_LayerMask"
                : "LayerMask";
        }

        private string GetDefaultMeshName()
        {
            return _sculptSession != null
                ? _sculptSession.SourceMeshName + "_Sculpted"
                : TryGetGeometryTarget(out MeshLayerMaskTarget target, out _)
                    ? target.Mesh.name + "_Sculpted"
                    : "SculptedMesh";
        }

        private void RefreshSculptRaycaster()
        {
            if (_sculptSession?.ActiveMesh)
                _raycaster.UpdateMesh(_sculptSession.ActiveMesh);
        }

        private void SyncDirtyState()
        {
            hasUnsavedChanges = (_paintSession?.IsDirty == true) || (_sculptSession?.IsDirty == true);
        }

        private void ReleaseSessions()
        {
            _editMode = false;
            _strokeActive = false;
            _paintSession?.Dispose();
            _paintSession = null;
            _sculptSession?.Dispose();
            _sculptSession = null;
            _raycaster.Dispose();
            _history.Clear();
            _affectedVertexCount = 0;
        }

        private static string GetLayerLabel(int layer)
        {
            return layer switch
            {
                0 => "Layer 0 (A)",
                1 => "Layer 1 (R)",
                2 => "Layer 2 (G)",
                3 => "Layer 3 (B)",
                _ => $"Layer {layer}"
            };
        }
    }
}
