using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace CORE.Editor.Tools
{
    public sealed class PrefabPainterWindow : EditorWindow
    {
        private const int SceneControlHint = 0x50465054;
        private const float CardWidth = 132f;
        private const float CardHeight = 148f;
        private const float MarkerRadius = 0.14f;
        private const float DirectionalDragThresholdPixels = 5f;

        private static readonly Color PanelColor = new Color(0.105f, 0.12f, 0.145f, 1f);
        private static readonly Color PanelBorderColor = new Color(0.19f, 0.23f, 0.28f, 1f);
        private static readonly Color AccentColor = new Color(0.16f, 0.78f, 0.9f, 1f);
        private static readonly Color AccentSoftColor = new Color(0.12f, 0.32f, 0.38f, 1f);
        private static readonly Color InvalidColor = new Color(0.95f, 0.34f, 0.3f, 1f);
        private static readonly Color MutedTextColor = new Color(0.68f, 0.72f, 0.78f, 1f);

        [SerializeField] private PrefabPainterConfig _config;
        [SerializeField] private Transform _root;
        [SerializeField] private int _selectedIndex = -1;
        [SerializeField] private int _selectedSectionIndex;
        [SerializeField] private bool _showConfigEditor = true;
        [SerializeField] private bool _directionalDragRotation;
        [SerializeField] private float _cardScale = 1f;
        [SerializeField] private int _cardRowCount = 1;
        [SerializeField] private List<GameObject> _selectedPrefabs = new List<GameObject>();
        [SerializeField] private GameObject _selectionAnchorPrefab;

        private readonly List<VisualElement> _cards = new List<VisualElement>();
        private readonly List<int> _cardEntryIndices = new List<int>();
        private readonly Dictionary<int, Image> _previewImages = new Dictionary<int, Image>();

        private PrefabPainterPlacementController _placementController;
        private System.Random _random;
        private bool _isPainting;
        private bool _gestureActive;
        private bool _eraseGestureActive;
        private bool _wallSnapTemporarilyDisabled;
        private float _gestureRandomYaw;
        private Vector2 _gestureStartMousePosition;
        private Vector3 _gestureSurfacePoint;
        private Vector3 _gestureSurfaceNormal;
        private Vector3 _gestureDirectionPoint;
        private bool _hasGestureSurface;
        private bool _hasGestureDirection;
        private Vector2 _lastMousePosition;
        private int _sceneControlId;
        private int _lastConfigHash;
        private PrefabPainterPlacementFeedback _feedback;
        private GameObject _eraseTarget;

        private ObjectField _configField;
        private ObjectField _rootField;
        private ObjectField _addPrefabField;
        private Button _paintButton;
        private Toggle _directionalDragToggle;
        private Button _focusPrefabButton;
        private Button _removePrefabButton;
        private Button _removeSectionButton;
        private Label _statusLabel;
        private Label _selectionLabel;
        private TextField _sectionNameField;
        private VisualElement _sectionContainer;
        private Label _cardScaleLabel;
        private Slider _cardScaleSlider;
        private Label _cardRowCountLabel;
        private SliderInt _cardRowCountSlider;
        private ScrollView _cardScroll;
        private VisualElement _cardContainer;
        private VisualElement _inspectorHost;
        private VisualElement _selectionEditorHost;
        private Foldout _configFoldout;
        private Foldout _selectionFoldout;

        [MenuItem("Tools/Prefab Painter")]
        private static void Open()
        {
            PrefabPainterWindow window = GetWindow<PrefabPainterWindow>("Prefab Painter");
            window.minSize = new Vector2(700f, 560f);
        }

        private void OnEnable()
        {
            _selectedPrefabs ??= new List<GameObject>();
            _random = new System.Random(Environment.TickCount);
            _placementController = new PrefabPainterPlacementController();
            _isPainting = false;

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.projectChanged += OnProjectChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            StopPainting();

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.projectChanged -= OnProjectChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;

            _placementController?.Dispose();
            _placementController = null;
        }

        public void CreateGUI()
        {
            VisualElement rootElement = rootVisualElement;
            rootElement.Clear();
            rootElement.style.backgroundColor = new Color(0.075f, 0.085f, 0.105f, 1f);

            ScrollView mainScroll = new ScrollView(ScrollViewMode.Vertical);
            mainScroll.style.flexGrow = 1f;
            mainScroll.contentContainer.style.paddingLeft = 12f;
            mainScroll.contentContainer.style.paddingRight = 12f;
            mainScroll.contentContainer.style.paddingTop = 12f;
            mainScroll.contentContainer.style.paddingBottom = 14f;
            rootElement.Add(mainScroll);

            BuildHeader(mainScroll);
            BuildReferencesPanel(mainScroll);
            BuildLibraryPanel(mainScroll);
            BuildPaintButton(mainScroll);
            BuildConfigInspector(mainScroll);

            EnsureValidSelection();
            RefreshCards();
            RefreshConfigInspector();
            UpdateWindowState();

            _lastConfigHash = ComputeConfigHash();
            rootElement.schedule.Execute(PollConfigChanges).Every(250);
            rootElement.schedule.Execute(RefreshAssetPreviews).Every(350);
        }

        private void BuildHeader(VisualElement parent)
        {
            Label title = new Label("Prefab Painter");
            title.style.fontSize = 22f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Color.white;
            parent.Add(title);

            Label subtitle = new Label(
                "Точное размещение prefab-объектов с живым preview, ориентацией по стенам и контролем высоты.");
            subtitle.style.color = MutedTextColor;
            subtitle.style.fontSize = 12f;
            subtitle.style.marginTop = 2f;
            subtitle.style.marginBottom = 10f;
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(subtitle);
        }

        private void BuildReferencesPanel(VisualElement parent)
        {
            VisualElement panel = CreatePanel();
            parent.Add(panel);

            Label heading = CreateSectionHeading("Рабочее пространство");
            panel.Add(heading);

            VisualElement configRow = CreateRow();
            _configField = new ObjectField("Конфиг")
            {
                objectType = typeof(PrefabPainterConfig),
                allowSceneObjects = false
            };
            _configField.tooltip = "ScriptableObject с библиотекой префабов и общими параметрами размещения.";
            _configField.style.flexGrow = 1f;
            _configField.SetValueWithoutNotify(_config);
            _configField.RegisterValueChangedCallback(OnConfigChanged);
            configRow.Add(_configField);

            Button createConfigButton = new Button(CreateConfigAsset) { text = "Создать" };
            createConfigButton.tooltip = "Создать новый PrefabPainterConfig в Project.";
            StyleSmallButton(createConfigButton);
            configRow.Add(createConfigButton);
            panel.Add(configRow);

            VisualElement rootRow = CreateRow();
            _rootField = new ObjectField("Root")
            {
                objectType = typeof(Transform),
                allowSceneObjects = true
            };
            _rootField.tooltip = "Отдельный объект открытой сцены, под которым будут храниться созданные экземпляры.";
            _rootField.style.flexGrow = 1f;
            _rootField.SetValueWithoutNotify(_root);
            _rootField.RegisterValueChangedCallback(OnRootChanged);
            rootRow.Add(_rootField);

            Button createRootButton = new Button(CreateSceneRoot) { text = "Создать Root" };
            createRootButton.tooltip = "Создать отдельный контейнер Prefab Painter Root в активной сцене.";
            StyleSmallButton(createRootButton);
            rootRow.Add(createRootButton);
            panel.Add(rootRow);

            _statusLabel = new Label();
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop = 7f;
            _statusLabel.style.paddingLeft = 8f;
            _statusLabel.style.paddingRight = 8f;
            _statusLabel.style.paddingTop = 7f;
            _statusLabel.style.paddingBottom = 7f;
            _statusLabel.style.borderTopLeftRadius = 5f;
            _statusLabel.style.borderTopRightRadius = 5f;
            _statusLabel.style.borderBottomLeftRadius = 5f;
            _statusLabel.style.borderBottomRightRadius = 5f;
            panel.Add(_statusLabel);
        }

        private void BuildLibraryPanel(VisualElement parent)
        {
            VisualElement panel = CreatePanel();
            panel.style.marginTop = 10f;
            parent.Add(panel);

            VisualElement headerRow = CreateRow();
            Label heading = CreateSectionHeading("Библиотека префабов");
            heading.style.flexGrow = 1f;
            headerRow.Add(heading);

            _selectionLabel = new Label("Ничего не выбрано");
            _selectionLabel.style.color = MutedTextColor;
            _selectionLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _selectionLabel.style.marginRight = 6f;
            headerRow.Add(_selectionLabel);

            _focusPrefabButton = new Button(FocusSelectedPrefab) { text = "Найти" };
            _focusPrefabButton.tooltip = "Выделить выбранный prefab-ассет в окне Project.";
            StyleSmallButton(_focusPrefabButton);
            headerRow.Add(_focusPrefabButton);

            _removePrefabButton = new Button(RemoveSelectedPrefab) { text = "Удалить" };
            _removePrefabButton.tooltip = "Удалить выбранный prefab из коллекции. Действие поддерживает Undo.";
            StyleSmallButton(_removePrefabButton);
            headerRow.Add(_removePrefabButton);
            panel.Add(headerRow);

            VisualElement sectionManageRow = CreateRow();
            _sectionNameField = new TextField("Название раздела")
            {
                isDelayed = true
            };
            _sectionNameField.tooltip = "Название активного раздела библиотеки.";
            _sectionNameField.style.flexGrow = 1f;
            _sectionNameField.RegisterValueChangedCallback(OnSectionNameChanged);
            sectionManageRow.Add(_sectionNameField);

            Button addSectionButton = new Button(CreateSection) { text = "+ Раздел" };
            addSectionButton.tooltip = "Создать новый раздел библиотеки.";
            StyleSmallButton(addSectionButton);
            sectionManageRow.Add(addSectionButton);

            _removeSectionButton = new Button(RemoveActiveSection) { text = "Удалить раздел" };
            _removeSectionButton.tooltip = "Удалить активный раздел. Его префабы будут перенесены в соседний раздел.";
            StyleSmallButton(_removeSectionButton);
            sectionManageRow.Add(_removeSectionButton);
            panel.Add(sectionManageRow);

            ScrollView sectionScroll = new ScrollView(ScrollViewMode.Horizontal);
            sectionScroll.style.height = 34f;
            sectionScroll.style.marginTop = 3f;
            sectionScroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            sectionScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _sectionContainer = sectionScroll.contentContainer;
            _sectionContainer.style.flexDirection = FlexDirection.Row;
            panel.Add(sectionScroll);

            VisualElement scaleRow = CreateRow();
            scaleRow.style.justifyContent = Justify.FlexEnd;
            scaleRow.style.marginTop = 2f;

            _cardScaleLabel = new Label();
            _cardScaleLabel.style.color = MutedTextColor;
            _cardScaleLabel.style.width = 122f;
            _cardScaleLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            scaleRow.Add(_cardScaleLabel);

            _cardScaleSlider = new Slider(0.25f, 1.25f);
            _cardScaleSlider.tooltip = "Изменяет размер карточек и превью, чтобы показать больше или меньше префабов.";
            _cardScaleSlider.style.width = 180f;
            _cardScaleSlider.SetValueWithoutNotify(Mathf.Clamp(_cardScale, 0.25f, 1.25f));
            _cardScaleSlider.RegisterValueChangedCallback(OnCardScaleChanged);
            scaleRow.Add(_cardScaleSlider);

            _cardRowCountLabel = new Label();
            _cardRowCountLabel.style.color = MutedTextColor;
            _cardRowCountLabel.style.width = 76f;
            _cardRowCountLabel.style.marginLeft = 14f;
            _cardRowCountLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            scaleRow.Add(_cardRowCountLabel);

            _cardRowCountSlider = new SliderInt(1, 6);
            _cardRowCountSlider.tooltip = "Количество горизонтальных рядов карточек в библиотеке.";
            _cardRowCountSlider.style.width = 110f;
            _cardRowCountSlider.SetValueWithoutNotify(Mathf.Clamp(_cardRowCount, 1, 6));
            _cardRowCountSlider.RegisterValueChangedCallback(OnCardRowCountChanged);
            scaleRow.Add(_cardRowCountSlider);
            panel.Add(scaleRow);

            _cardScroll = new ScrollView(ScrollViewMode.Horizontal);
            _cardScroll.style.height = GetCardGridHeight() + 22f;
            _cardScroll.style.marginTop = 5f;
            _cardScroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            _cardScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _cardContainer = _cardScroll.contentContainer;
            _cardContainer.style.flexDirection = FlexDirection.Row;
            panel.Add(_cardScroll);

            VisualElement addRow = CreateRow();
            addRow.style.marginTop = 7f;
            _addPrefabField = new ObjectField("Добавить prefab")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false
            };
            _addPrefabField.tooltip = "Добавить prefab-ассет в текущий конфиг. Дубликаты игнорируются.";
            _addPrefabField.style.flexGrow = 1f;
            _addPrefabField.RegisterValueChangedCallback(OnAddPrefabChanged);
            addRow.Add(_addPrefabField);

            Label dropLabel = new Label("или перетащите сюда");
            dropLabel.tooltip = "Перетащите один или несколько prefab-ассетов из Project.";
            dropLabel.style.color = MutedTextColor;
            dropLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            dropLabel.style.paddingLeft = 12f;
            dropLabel.style.paddingRight = 12f;
            dropLabel.style.height = 28f;
            dropLabel.style.borderLeftWidth = 1f;
            dropLabel.style.borderRightWidth = 1f;
            dropLabel.style.borderTopWidth = 1f;
            dropLabel.style.borderBottomWidth = 1f;
            dropLabel.style.borderLeftColor = PanelBorderColor;
            dropLabel.style.borderRightColor = PanelBorderColor;
            dropLabel.style.borderTopColor = PanelBorderColor;
            dropLabel.style.borderBottomColor = PanelBorderColor;
            dropLabel.style.borderTopLeftRadius = 4f;
            dropLabel.style.borderTopRightRadius = 4f;
            dropLabel.style.borderBottomLeftRadius = 4f;
            dropLabel.style.borderBottomRightRadius = 4f;
            RegisterPrefabDropArea(dropLabel);
            addRow.Add(dropLabel);
            panel.Add(addRow);
        }

        private void BuildPaintButton(VisualElement parent)
        {
            _directionalDragToggle = new Toggle("Поворот протягиванием");
            _directionalDragToggle.tooltip =
                "Первое нажатие фиксирует позицию, а протягивание задаёт направление свободного напольного prefab-а.";
            _directionalDragToggle.style.marginTop = 10f;
            _directionalDragToggle.SetValueWithoutNotify(_directionalDragRotation);
            _directionalDragToggle.RegisterValueChangedCallback(evt =>
            {
                _directionalDragRotation = evt.newValue;
                if (_gestureActive)
                {
                    CancelGesture();
                }

                UpdateWindowState();
                SceneView.RepaintAll();
            });
            parent.Add(_directionalDragToggle);

            _paintButton = new Button(TogglePainting);
            _paintButton.style.height = 42f;
            _paintButton.style.marginTop = 5f;
            _paintButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _paintButton.style.fontSize = 13f;
            _paintButton.style.borderTopLeftRadius = 7f;
            _paintButton.style.borderTopRightRadius = 7f;
            _paintButton.style.borderBottomLeftRadius = 7f;
            _paintButton.style.borderBottomRightRadius = 7f;
            parent.Add(_paintButton);
        }

        private void BuildConfigInspector(VisualElement parent)
        {
            _configFoldout = new Foldout
            {
                text = "Общие настройки",
                value = _showConfigEditor
            };
            _configFoldout.style.marginTop = 10f;
            _configFoldout.RegisterValueChangedCallback(evt => _showConfigEditor = evt.newValue);
            parent.Add(_configFoldout);

            _inspectorHost = CreatePanel();
            _inspectorHost.style.marginTop = 5f;
            _configFoldout.Add(_inspectorHost);

            _selectionFoldout = new Foldout
            {
                text = "Настройки выбранных префабов",
                value = true
            };
            _selectionFoldout.style.marginTop = 8f;
            parent.Add(_selectionFoldout);

            _selectionEditorHost = CreatePanel();
            _selectionEditorHost.style.marginTop = 5f;
            _selectionFoldout.Add(_selectionEditorHost);
        }

        private void OnConfigChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            StopPainting();
            _config = evt.newValue as PrefabPainterConfig;
            _selectedIndex = -1;
            _selectedSectionIndex = 0;
            _selectedPrefabs.Clear();
            _selectionAnchorPrefab = null;
            EnsureValidSelection();
            RefreshCards();
            RefreshConfigInspector();
            UpdateWindowState();
            SceneView.RepaintAll();
        }

        private void OnRootChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            StopPainting();
            _root = evt.newValue as Transform;
            UpdateWindowState();
            SceneView.RepaintAll();
        }

        private void OnAddPrefabChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            GameObject prefab = evt.newValue as GameObject;
            _addPrefabField.SetValueWithoutNotify(null);
            if (prefab != null)
            {
                AddPrefabToConfig(prefab);
            }
        }

        private void OnCardScaleChanged(ChangeEvent<float> evt)
        {
            float snappedScale = Mathf.Round(Mathf.Clamp(evt.newValue, 0.25f, 1.25f) * 20f) / 20f;
            if (Mathf.Approximately(_cardScale, snappedScale))
            {
                return;
            }

            _cardScale = snappedScale;
            _cardScaleSlider.SetValueWithoutNotify(_cardScale);
            RefreshCards();
        }

        private void OnCardRowCountChanged(ChangeEvent<int> evt)
        {
            int rowCount = Mathf.Clamp(evt.newValue, 1, 6);
            if (_cardRowCount == rowCount)
            {
                return;
            }

            _cardRowCount = rowCount;
            _cardRowCountSlider.SetValueWithoutNotify(_cardRowCount);
            RefreshCards();
        }

        private void OnSectionNameChanged(ChangeEvent<string> evt)
        {
            if (_config == null || _selectedSectionIndex < 0 ||
                _selectedSectionIndex >= _config.Sections.Count)
            {
                return;
            }

            string requestedName = evt.newValue?.Trim();
            string currentName = _config.Sections[_selectedSectionIndex];
            if (string.IsNullOrWhiteSpace(requestedName) || requestedName == currentName)
            {
                _sectionNameField.SetValueWithoutNotify(currentName);
                return;
            }

            for (int i = 0; i < _config.Sections.Count; i++)
            {
                if (i != _selectedSectionIndex &&
                    string.Equals(_config.Sections[i], requestedName, StringComparison.OrdinalIgnoreCase))
                {
                    ShowNotification(new GUIContent($"Раздел '{requestedName}' уже существует."));
                    _sectionNameField.SetValueWithoutNotify(currentName);
                    return;
                }
            }

            StopPainting();
            Undo.RecordObject(_config, "Переименовать раздел Prefab Painter");
            SerializedObject serializedConfig = new SerializedObject(_config);
            SerializedProperty sectionsProperty = serializedConfig.FindProperty("_sections");
            sectionsProperty.GetArrayElementAtIndex(_selectedSectionIndex).stringValue = requestedName;

            SerializedProperty prefabsProperty = serializedConfig.FindProperty("_prefabs");
            for (int i = 0; i < prefabsProperty.arraySize; i++)
            {
                SerializedProperty sectionProperty = prefabsProperty
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("_section");
                if (string.Equals(sectionProperty.stringValue, currentName, StringComparison.OrdinalIgnoreCase))
                {
                    sectionProperty.stringValue = requestedName;
                }
            }

            serializedConfig.ApplyModifiedProperties();
            EditorUtility.SetDirty(_config);
            RefreshCards();
            RefreshConfigInspector();
            UpdateWindowState();
        }

        private void CreateSection()
        {
            if (_config == null)
            {
                ShowNotification(new GUIContent("Сначала назначьте или создайте конфиг."));
                return;
            }

            StopPainting();
            string sectionName = CreateUniqueSectionName("Новый раздел");
            Undo.RecordObject(_config, "Создать раздел Prefab Painter");

            SerializedObject serializedConfig = new SerializedObject(_config);
            SerializedProperty sectionsProperty = serializedConfig.FindProperty("_sections");
            int newIndex = sectionsProperty.arraySize;
            sectionsProperty.arraySize++;
            sectionsProperty.GetArrayElementAtIndex(newIndex).stringValue = sectionName;
            serializedConfig.ApplyModifiedProperties();
            EditorUtility.SetDirty(_config);

            _selectedSectionIndex = newIndex;
            _selectedIndex = -1;
            RefreshCards();
            RefreshConfigInspector();
            UpdateWindowState();
            _sectionNameField?.Focus();
        }

        private void RemoveActiveSection()
        {
            if (_config == null || _config.Sections.Count <= 1 ||
                _selectedSectionIndex < 0 || _selectedSectionIndex >= _config.Sections.Count)
            {
                return;
            }

            string removedSection = _config.Sections[_selectedSectionIndex];
            int fallbackIndex = _selectedSectionIndex == 0 ? 1 : 0;
            string fallbackSection = _config.Sections[fallbackIndex];
            int movedCount = CountEntriesInSection(removedSection);

            bool confirmed = EditorUtility.DisplayDialog(
                "Удаление раздела",
                movedCount > 0
                    ? $"Удалить раздел '{removedSection}'? Его префабы ({movedCount}) будут перенесены в '{fallbackSection}'."
                    : $"Удалить пустой раздел '{removedSection}'?",
                "Удалить",
                "Отмена");
            if (!confirmed)
            {
                return;
            }

            StopPainting();
            Undo.RecordObject(_config, "Удалить раздел Prefab Painter");
            SerializedObject serializedConfig = new SerializedObject(_config);
            SerializedProperty prefabsProperty = serializedConfig.FindProperty("_prefabs");
            for (int i = 0; i < prefabsProperty.arraySize; i++)
            {
                SerializedProperty sectionProperty = prefabsProperty
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("_section");
                if (string.Equals(sectionProperty.stringValue, removedSection, StringComparison.OrdinalIgnoreCase))
                {
                    sectionProperty.stringValue = fallbackSection;
                }
            }

            SerializedProperty sectionsProperty = serializedConfig.FindProperty("_sections");
            sectionsProperty.DeleteArrayElementAtIndex(_selectedSectionIndex);
            serializedConfig.ApplyModifiedProperties();
            EditorUtility.SetDirty(_config);

            _selectedSectionIndex = fallbackIndex > _selectedSectionIndex
                ? fallbackIndex - 1
                : fallbackIndex;
            _selectedIndex = -1;
            RefreshCards();
            RefreshConfigInspector();
            UpdateWindowState();
        }

        private void RemoveSelectedPrefab()
        {
            if (_config == null || _selectedPrefabs == null || _selectedPrefabs.Count == 0)
            {
                return;
            }

            List<int> selectedIndices = new List<int>();
            for (int i = 0; i < _config.Prefabs.Count; i++)
            {
                PrefabPainterEntry entry = _config.Prefabs[i];
                if (entry != null && entry.Prefab != null && _selectedPrefabs.Contains(entry.Prefab))
                {
                    selectedIndices.Add(i);
                }
            }

            if (selectedIndices.Count == 0)
            {
                return;
            }

            if (selectedIndices.Count > 1 && !EditorUtility.DisplayDialog(
                    "Удаление префабов",
                    $"Удалить выбранные записи ({selectedIndices.Count}) из коллекции? Действие можно отменить через Undo.",
                    "Удалить",
                    "Отмена"))
            {
                return;
            }

            StopPainting();
            Undo.RecordObject(_config, "Удалить префабы из Prefab Painter");
            SerializedObject serializedConfig = new SerializedObject(_config);
            SerializedProperty prefabsProperty = serializedConfig.FindProperty("_prefabs");
            for (int i = selectedIndices.Count - 1; i >= 0; i--)
            {
                prefabsProperty.DeleteArrayElementAtIndex(selectedIndices[i]);
            }

            serializedConfig.ApplyModifiedProperties();
            EditorUtility.SetDirty(_config);

            _selectedIndex = -1;
            _selectedPrefabs.Clear();
            _selectionAnchorPrefab = null;
            EnsureValidSelection();
            RefreshCards();
            RefreshConfigInspector();
            UpdateWindowState();
        }

        private void RegisterPrefabDropArea(VisualElement dropArea)
        {
            dropArea.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (_config == null || !ContainsPrefabAsset(DragAndDrop.objectReferences))
                {
                    return;
                }

                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            });

            dropArea.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (_config == null)
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences;
                for (int i = 0; i < draggedObjects.Length; i++)
                {
                    if (draggedObjects[i] is GameObject prefab)
                    {
                        AddPrefabToConfig(prefab);
                    }
                }

                evt.StopPropagation();
            });
        }

        private void AddPrefabToConfig(GameObject prefab)
        {
            if (_config == null || prefab == null ||
                !EditorUtility.IsPersistent(prefab) ||
                !PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                ShowNotification(new GUIContent("Можно добавить только prefab-ассет из Project."));
                return;
            }

            IReadOnlyList<PrefabPainterEntry> entries = _config.Prefabs;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].Prefab == prefab)
                {
                    ShowNotification(new GUIContent($"'{prefab.name}' уже есть в конфиге."));
                    SelectSectionByName(entries[i].Section);
                    SelectIndex(i);
                    return;
                }
            }

            StopPainting();
            Undo.RecordObject(_config, "Добавить prefab в Prefab Painter");

            SerializedObject serializedConfig = new SerializedObject(_config);
            SerializedProperty prefabsProperty = serializedConfig.FindProperty("_prefabs");
            int newIndex = prefabsProperty.arraySize;
            prefabsProperty.arraySize++;

            SerializedProperty entryProperty = prefabsProperty.GetArrayElementAtIndex(newIndex);
            entryProperty.FindPropertyRelative("_prefab").objectReferenceValue = prefab;
            entryProperty.FindPropertyRelative("_wallOnly").boolValue = false;
            entryProperty.FindPropertyRelative("_attachmentSide").enumValueIndex =
                (int)PrefabPainterAttachmentSide.Back;
            entryProperty.FindPropertyRelative("_localOffset").vector3Value = Vector3.zero;
            entryProperty.FindPropertyRelative("_startRotationEuler").vector3Value = Vector3.zero;
            entryProperty.FindPropertyRelative("_section").stringValue = GetActiveSectionName();

            serializedConfig.ApplyModifiedProperties();
            EditorUtility.SetDirty(_config);

            SetSingleSelection(newIndex);
            RefreshCards();
            RefreshConfigInspector();
            UpdateWindowState();
        }

        private void CreateConfigAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Создать Prefab Painter Config",
                nameof(PrefabPainterConfig),
                "asset",
                "Выберите расположение нового конфига.",
                "Assets/CORE/Configs/Rendering");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            PrefabPainterConfig config = CreateInstance<PrefabPainterConfig>();
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);

            _config = config;
            _configField.SetValueWithoutNotify(config);
            _selectedIndex = -1;
            _selectedSectionIndex = 0;
            _selectedPrefabs.Clear();
            _selectionAnchorPrefab = null;
            RefreshCards();
            RefreshConfigInspector();
            UpdateWindowState();
        }

        private void CreateSceneRoot()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                ShowNotification(new GUIContent("Нет активной загруженной сцены."));
                return;
            }

            StopPainting();
            GameObject rootObject = new GameObject("Prefab Painter Root");
            SceneManager.MoveGameObjectToScene(rootObject, activeScene);
            Undo.RegisterCreatedObjectUndo(rootObject, "Создать Prefab Painter Root");

            _root = rootObject.transform;
            _rootField.SetValueWithoutNotify(_root);
            Selection.activeGameObject = rootObject;
            UpdateWindowState();
        }

        private void RefreshConfigInspector()
        {
            if (_inspectorHost == null)
            {
                return;
            }

            _inspectorHost.Unbind();
            _inspectorHost.Clear();
            if (_config == null)
            {
                Label emptyLabel = new Label("Назначьте или создайте конфиг, чтобы отредактировать параметры.");
                emptyLabel.style.color = MutedTextColor;
                emptyLabel.style.whiteSpace = WhiteSpace.Normal;
                _inspectorHost.Add(emptyLabel);
                RefreshSelectedEntriesEditor();
                return;
            }

            SerializedObject serializedConfig = new SerializedObject(_config);
            AddGlobalConfigField(serializedConfig, "_surfaceMask", "Маска поверхностей");
            AddGlobalConfigField(serializedConfig, "_maxFloorSlopeAngle", "Максимальный склон пола");
            AddGlobalConfigField(serializedConfig, "_wallDeviationAngle", "Отклонение стены");
            AddGlobalConfigField(serializedConfig, "_surfaceOffset", "Отступ от поверхности");
            AddGlobalConfigField(serializedConfig, "_nearbyWallDistance", "Дистанция поиска стены");
            AddGlobalConfigField(serializedConfig, "_nearbyWallProbeHeight", "Высота поиска стены");
            AddGlobalConfigField(serializedConfig, "_floorProbeDistance", "Дистанция поиска пола");
            AddGlobalConfigField(serializedConfig, "_eraserPickRadiusPixels", "Радиус стирания, px");
            AddGlobalConfigField(serializedConfig, "_randomYawRange", "Случайный YAW");
            _inspectorHost.Bind(serializedConfig);
            RefreshSelectedEntriesEditor();
        }

        private void AddGlobalConfigField(
            SerializedObject serializedConfig,
            string propertyName,
            string label)
        {
            SerializedProperty property = serializedConfig.FindProperty(propertyName);
            if (property != null)
            {
                _inspectorHost.Add(new PropertyField(property, label));
            }
        }

        private void RefreshSelectedEntriesEditor()
        {
            if (_selectionEditorHost == null)
            {
                return;
            }

            _selectionEditorHost.Clear();
            List<PrefabPainterEntry> selectedEntries = GetSelectedEntries();
            if (_config == null || selectedEntries.Count == 0)
            {
                Label emptyLabel = new Label("Выберите одну или несколько карточек в активном разделе.");
                emptyLabel.style.color = MutedTextColor;
                emptyLabel.style.whiteSpace = WhiteSpace.Normal;
                _selectionEditorHost.Add(emptyLabel);
                return;
            }

            Label summaryLabel = new Label(
                selectedEntries.Count == 1
                    ? selectedEntries[0].Prefab.name
                    : $"Выбрано префабов: {selectedEntries.Count}");
            summaryLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            summaryLabel.style.color = Color.white;
            summaryLabel.style.marginBottom = 6f;
            _selectionEditorHost.Add(summaryLabel);

            if (selectedEntries.Count == 1)
            {
                ObjectField prefabField = new ObjectField("Prefab")
                {
                    objectType = typeof(GameObject),
                    allowSceneObjects = false
                };
                prefabField.SetValueWithoutNotify(selectedEntries[0].Prefab);
                prefabField.RegisterValueChangedCallback(OnSelectedPrefabReferenceChanged);
                _selectionEditorHost.Add(prefabField);
            }

            PrefabPainterEntry firstEntry = selectedEntries[0];
            List<string> sectionChoices = new List<string>(_config.Sections);
            int sectionIndex = Mathf.Max(0, sectionChoices.IndexOf(firstEntry.Section));
            PopupField<string> sectionField = new PopupField<string>("Раздел", sectionChoices, sectionIndex);
            sectionField.showMixedValue = HasMixedValue(selectedEntries, entry => entry.Section);
            sectionField.RegisterValueChangedCallback(evt => ApplySectionToSelected(evt.newValue));
            _selectionEditorHost.Add(sectionField);

            Toggle wallOnlyField = new Toggle("Только для стен");
            wallOnlyField.SetValueWithoutNotify(firstEntry.WallOnly);
            wallOnlyField.showMixedValue = HasMixedValue(selectedEntries, entry => entry.WallOnly);
            wallOnlyField.RegisterValueChangedCallback(evt =>
                ApplyValueToSelected(
                    "_wallOnly",
                    property => property.boolValue = evt.newValue,
                    "Изменить тип поверхности префабов"));
            _selectionEditorHost.Add(wallOnlyField);

            EnumField attachmentSideField = new EnumField("Сторона к стене", firstEntry.AttachmentSide);
            attachmentSideField.showMixedValue = HasMixedValue(selectedEntries, entry => entry.AttachmentSide);
            attachmentSideField.RegisterValueChangedCallback(evt =>
                ApplyValueToSelected(
                    "_attachmentSide",
                    property => property.enumValueIndex = Convert.ToInt32(evt.newValue),
                    "Изменить сторону крепления префабов"));
            _selectionEditorHost.Add(attachmentSideField);

            Vector3Field localOffsetField = new Vector3Field("Локальное смещение");
            localOffsetField.SetValueWithoutNotify(firstEntry.LocalOffset);
            localOffsetField.showMixedValue = HasMixedValue(selectedEntries, entry => entry.LocalOffset);
            localOffsetField.RegisterValueChangedCallback(evt =>
                ApplyValueToSelected(
                    "_localOffset",
                    property => property.vector3Value = evt.newValue,
                    "Изменить смещение префабов"));
            _selectionEditorHost.Add(localOffsetField);

            Vector3Field startRotationField = new Vector3Field("Стартовый поворот");
            startRotationField.SetValueWithoutNotify(firstEntry.StartRotationEuler);
            startRotationField.showMixedValue = HasMixedValue(selectedEntries, entry => entry.StartRotationEuler);
            startRotationField.RegisterValueChangedCallback(evt =>
                ApplyValueToSelected(
                    "_startRotationEuler",
                    property => property.vector3Value = evt.newValue,
                    "Изменить поворот префабов"));
            _selectionEditorHost.Add(startRotationField);

            Label hintLabel = new Label(
                "При mixed value изменяется только отредактированное поле; остальные параметры выделенных записей сохраняются.");
            hintLabel.style.color = MutedTextColor;
            hintLabel.style.fontSize = 10f;
            hintLabel.style.marginTop = 6f;
            hintLabel.style.whiteSpace = WhiteSpace.Normal;
            _selectionEditorHost.Add(hintLabel);
        }

        private void OnSelectedPrefabReferenceChanged(ChangeEvent<UnityEngine.Object> evt)
        {
            PrefabPainterEntry selectedEntry = GetSelectedEntry();
            GameObject previousPrefab = selectedEntry != null ? selectedEntry.Prefab : null;
            GameObject newPrefab = evt.newValue as GameObject;
            if (previousPrefab == null || newPrefab == null || newPrefab == previousPrefab)
            {
                RefreshSelectedEntriesEditor();
                return;
            }

            if (!EditorUtility.IsPersistent(newPrefab) || !PrefabUtility.IsPartOfPrefabAsset(newPrefab))
            {
                ShowNotification(new GUIContent("Можно назначить только prefab-ассет из Project."));
                RefreshSelectedEntriesEditor();
                return;
            }

            for (int i = 0; i < _config.Prefabs.Count; i++)
            {
                PrefabPainterEntry entry = _config.Prefabs[i];
                if (entry != selectedEntry && entry != null && entry.Prefab == newPrefab)
                {
                    ShowNotification(new GUIContent($"'{newPrefab.name}' уже есть в конфиге."));
                    RefreshSelectedEntriesEditor();
                    return;
                }
            }

            ApplyValueToSelected(
                "_prefab",
                property => property.objectReferenceValue = newPrefab,
                "Заменить prefab в Prefab Painter",
                false);

            _selectedPrefabs.Clear();
            _selectedPrefabs.Add(newPrefab);
            _selectionAnchorPrefab = newPrefab;
            RefreshCards();
            UpdateWindowState();
        }

        private void ApplySectionToSelected(string sectionName)
        {
            if (_config == null || string.IsNullOrWhiteSpace(sectionName))
            {
                return;
            }

            GameObject primaryPrefab = GetSelectedEntry()?.Prefab;
            ApplyValueToSelected(
                "_section",
                property => property.stringValue = sectionName,
                "Переместить префабы в раздел",
                false);

            for (int i = 0; i < _config.Sections.Count; i++)
            {
                if (string.Equals(_config.Sections[i], sectionName, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedSectionIndex = i;
                    break;
                }
            }

            _selectedIndex = FindEntryIndex(primaryPrefab);
            RefreshCards();
            UpdateWindowState();
            SceneView.RepaintAll();
        }

        private void ApplyValueToSelected(
            string propertyName,
            Action<SerializedProperty> applyValue,
            string undoName,
            bool refresh = true)
        {
            if (_config == null || _selectedPrefabs == null || _selectedPrefabs.Count == 0)
            {
                return;
            }

            CancelGesture();
            Undo.RecordObject(_config, undoName);
            SerializedObject serializedConfig = new SerializedObject(_config);
            SerializedProperty prefabsProperty = serializedConfig.FindProperty("_prefabs");
            for (int i = 0; i < prefabsProperty.arraySize; i++)
            {
                SerializedProperty entryProperty = prefabsProperty.GetArrayElementAtIndex(i);
                GameObject prefab = entryProperty.FindPropertyRelative("_prefab").objectReferenceValue as GameObject;
                if (prefab == null || !_selectedPrefabs.Contains(prefab))
                {
                    continue;
                }

                SerializedProperty targetProperty = entryProperty.FindPropertyRelative(propertyName);
                if (targetProperty != null)
                {
                    applyValue(targetProperty);
                }
            }

            serializedConfig.ApplyModifiedProperties();
            EditorUtility.SetDirty(_config);

            if (refresh)
            {
                RefreshCards();
                UpdateWindowState();
                SceneView.RepaintAll();
            }
        }

        private List<PrefabPainterEntry> GetSelectedEntries()
        {
            List<PrefabPainterEntry> result = new List<PrefabPainterEntry>();
            if (_config == null || _selectedPrefabs == null)
            {
                return result;
            }

            PruneSelectedPrefabs();
            PrefabPainterEntry primaryEntry = GetSelectedEntry();
            if (primaryEntry != null &&
                primaryEntry.Prefab != null &&
                _selectedPrefabs.Contains(primaryEntry.Prefab))
            {
                result.Add(primaryEntry);
            }

            for (int i = 0; i < _config.Prefabs.Count; i++)
            {
                PrefabPainterEntry entry = _config.Prefabs[i];
                if (entry != null &&
                    entry != primaryEntry &&
                    entry.Prefab != null &&
                    _selectedPrefabs.Contains(entry.Prefab))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        private static bool HasMixedValue<T>(
            IReadOnlyList<PrefabPainterEntry> entries,
            Func<PrefabPainterEntry, T> selector)
        {
            if (entries.Count < 2)
            {
                return false;
            }

            T firstValue = selector(entries[0]);
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = 1; i < entries.Count; i++)
            {
                if (!comparer.Equals(firstValue, selector(entries[i])))
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshCards()
        {
            if (_cardContainer == null)
            {
                return;
            }

            _cardContainer.Clear();
            _cards.Clear();
            _cardEntryIndices.Clear();
            _previewImages.Clear();
            _cardScale = Mathf.Clamp(_cardScale, 0.25f, 1.25f);
            _cardRowCount = Mathf.Clamp(_cardRowCount, 1, 6);
            _cardScroll.style.height = GetCardGridHeight() + 22f;
            UpdateCardScaleLabel();

            EnsureValidSection();
            EnsureValidSelection();
            RefreshSectionControls();

            if (_config == null || CountEntriesInSection(GetActiveSectionName()) == 0)
            {
                Label emptyLabel = new Label(
                    _config == null
                        ? "Назначьте или создайте конфиг"
                        : "В активном разделе пока нет префабов");
                emptyLabel.style.color = MutedTextColor;
                emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                emptyLabel.style.width = 360f * _cardScale;
                emptyLabel.style.height = GetCardGridHeight();
                _cardContainer.Add(emptyLabel);
                UpdateSelectionLabel();
                RefreshSelectedEntriesEditor();
                _lastConfigHash = ComputeConfigHash();
                return;
            }

            IReadOnlyList<PrefabPainterEntry> entries = _config.Prefabs;
            VisualElement currentColumn = null;
            int visibleCardIndex = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (!IsEntryInActiveSection(entries[i]))
                {
                    continue;
                }

                int cardIndex = i;
                PrefabPainterEntry entry = entries[i];
                bool isValid = PrefabPainterPlacementController.IsEntryValid(entry, out string invalidReason);
                VisualElement card = CreatePrefabCard(entry, i, isValid, invalidReason);
                card.RegisterCallback<ClickEvent>(evt => HandleCardClick(cardIndex, evt));

                if (visibleCardIndex % _cardRowCount == 0)
                {
                    currentColumn = new VisualElement();
                    currentColumn.style.flexDirection = FlexDirection.Column;
                    currentColumn.style.flexShrink = 0f;
                    _cardContainer.Add(currentColumn);
                }

                _cards.Add(card);
                _cardEntryIndices.Add(i);
                currentColumn.Add(card);
                visibleCardIndex++;
            }

            RefreshCardSelection();
            RefreshAssetPreviews();
            UpdateSelectionLabel();
            RefreshSelectedEntriesEditor();
            _lastConfigHash = ComputeConfigHash();
        }

        private VisualElement CreatePrefabCard(
            PrefabPainterEntry entry,
            int index,
            bool isValid,
            string invalidReason)
        {
            float scale = Mathf.Clamp(_cardScale, 0.25f, 1.25f);
            bool thumbnailOnly = scale < 0.5f;
            VisualElement card = new VisualElement();
            card.style.width = CardWidth * scale;
            card.style.minWidth = CardWidth * scale;
            card.style.height = CardHeight * scale;
            card.style.marginRight = 8f * scale;
            card.style.marginBottom = 8f * scale;
            card.style.paddingLeft = 6f * scale;
            card.style.paddingRight = 6f * scale;
            card.style.paddingTop = 6f * scale;
            card.style.paddingBottom = 6f * scale;
            card.style.borderLeftWidth = 2f;
            card.style.borderRightWidth = 2f;
            card.style.borderTopWidth = 2f;
            card.style.borderBottomWidth = 2f;
            card.style.borderTopLeftRadius = 7f;
            card.style.borderTopRightRadius = 7f;
            card.style.borderBottomLeftRadius = 7f;
            card.style.borderBottomRightRadius = 7f;
            card.style.opacity = isValid ? 1f : 0.48f;
            string tooltipName = entry != null && entry.Prefab != null ? entry.Prefab.name : "Пустая запись";
            string tooltipMode = entry != null && entry.WallOnly ? "Только стены" : "Пол и склоны";
            card.tooltip = isValid
                ? $"{tooltipName}\n{tooltipMode}\nКлик — выбрать, Ctrl — добавить, Shift — диапазон."
                : invalidReason;

            Image previewImage = new Image
            {
                scaleMode = ScaleMode.ScaleToFit
            };
            previewImage.style.height = 92f * scale;
            previewImage.style.backgroundColor = new Color(0.055f, 0.063f, 0.078f, 1f);
            previewImage.style.borderTopLeftRadius = 5f;
            previewImage.style.borderTopRightRadius = 5f;
            previewImage.style.borderBottomLeftRadius = 5f;
            previewImage.style.borderBottomRightRadius = 5f;

            GameObject prefab = entry != null ? entry.Prefab : null;
            if (prefab != null)
            {
                previewImage.image = AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab);
                _previewImages[prefab.GetInstanceID()] = previewImage;
            }
            else
            {
                previewImage.image = EditorGUIUtility.IconContent("console.erroricon").image;
            }

            card.Add(previewImage);

            Label nameLabel = new Label(prefab != null ? prefab.name : "Пустая запись");
            nameLabel.style.height = 22f * scale;
            nameLabel.style.marginTop = 3f * scale;
            nameLabel.style.fontSize = Mathf.Max(8f, 11f * scale);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            nameLabel.style.display = thumbnailOnly ? DisplayStyle.None : DisplayStyle.Flex;
            card.Add(nameLabel);

            Label badge = new Label(entry != null && entry.WallOnly ? "СТЕНА" : "ПОЛ / СКЛОН");
            badge.style.height = 17f * scale;
            badge.style.fontSize = Mathf.Max(7f, 9f * scale);
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            badge.style.color = entry != null && entry.WallOnly
                ? new Color(1f, 0.72f, 0.31f, 1f)
                : new Color(0.45f, 0.88f, 0.97f, 1f);
            badge.style.display = thumbnailOnly ? DisplayStyle.None : DisplayStyle.Flex;
            card.Add(badge);

            ApplyCardSelectionStyle(
                card,
                index == _selectedIndex,
                entry != null && entry.Prefab != null && _selectedPrefabs.Contains(entry.Prefab));
            return card;
        }

        private void RefreshSectionControls()
        {
            if (_sectionContainer == null)
            {
                return;
            }

            _sectionContainer.Clear();
            EnsureValidSection();

            if (_config == null)
            {
                _sectionNameField?.SetValueWithoutNotify(string.Empty);
                _removeSectionButton?.SetEnabled(false);
                return;
            }

            for (int i = 0; i < _config.Sections.Count; i++)
            {
                int sectionIndex = i;
                string sectionName = _config.Sections[i];
                int entryCount = CountEntriesInSection(sectionName);
                Button sectionButton = new Button(() => SelectSection(sectionIndex))
                {
                    text = $"{sectionName}  {entryCount}"
                };
                sectionButton.tooltip = $"Показать раздел '{sectionName}'.";
                sectionButton.style.height = 25f;
                sectionButton.style.marginRight = 5f;
                sectionButton.style.paddingLeft = 10f;
                sectionButton.style.paddingRight = 10f;
                sectionButton.style.unityFontStyleAndWeight =
                    i == _selectedSectionIndex ? FontStyle.Bold : FontStyle.Normal;
                sectionButton.style.backgroundColor = i == _selectedSectionIndex
                    ? AccentSoftColor
                    : new Color(0.15f, 0.17f, 0.2f, 1f);
                sectionButton.style.borderLeftColor = i == _selectedSectionIndex ? AccentColor : PanelBorderColor;
                sectionButton.style.borderRightColor = i == _selectedSectionIndex ? AccentColor : PanelBorderColor;
                sectionButton.style.borderTopColor = i == _selectedSectionIndex ? AccentColor : PanelBorderColor;
                sectionButton.style.borderBottomColor = i == _selectedSectionIndex ? AccentColor : PanelBorderColor;
                _sectionContainer.Add(sectionButton);
            }

            _sectionNameField?.SetValueWithoutNotify(GetActiveSectionName());
            _removeSectionButton?.SetEnabled(_config.Sections.Count > 1);
        }

        private void SelectSection(int sectionIndex)
        {
            if (_config == null || sectionIndex < 0 || sectionIndex >= _config.Sections.Count ||
                sectionIndex == _selectedSectionIndex)
            {
                return;
            }

            StopPainting();
            _selectedSectionIndex = sectionIndex;
            _selectedIndex = -1;
            EnsureValidSelection();
            RefreshCards();
            UpdateWindowState();
            SceneView.RepaintAll();
        }

        private void SelectSectionByName(string sectionName)
        {
            if (_config == null)
            {
                return;
            }

            for (int i = 0; i < _config.Sections.Count; i++)
            {
                if (string.Equals(_config.Sections[i], sectionName, StringComparison.OrdinalIgnoreCase))
                {
                    if (_selectedSectionIndex != i)
                    {
                        _selectedSectionIndex = i;
                        _selectedIndex = -1;
                        RefreshCards();
                    }

                    return;
                }
            }
        }

        private void EnsureValidSection()
        {
            if (_config == null || _config.Sections.Count == 0)
            {
                _selectedSectionIndex = 0;
                return;
            }

            _selectedSectionIndex = Mathf.Clamp(_selectedSectionIndex, 0, _config.Sections.Count - 1);
        }

        private string GetActiveSectionName()
        {
            EnsureValidSection();
            return _config != null && _config.Sections.Count > 0
                ? _config.Sections[_selectedSectionIndex]
                : PrefabPainterEntry.DefaultSectionName;
        }

        private bool IsEntryInActiveSection(PrefabPainterEntry entry)
        {
            return entry != null &&
                   string.Equals(entry.Section, GetActiveSectionName(), StringComparison.OrdinalIgnoreCase);
        }

        private int CountEntriesInSection(string sectionName)
        {
            if (_config == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _config.Prefabs.Count; i++)
            {
                PrefabPainterEntry entry = _config.Prefabs[i];
                if (entry != null &&
                    string.Equals(entry.Section, sectionName, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        private string CreateUniqueSectionName(string baseName)
        {
            string candidate = baseName;
            int suffix = 2;
            while (SectionNameExists(candidate))
            {
                candidate = $"{baseName} {suffix}";
                suffix++;
            }

            return candidate;
        }

        private bool SectionNameExists(string sectionName)
        {
            if (_config == null)
            {
                return false;
            }

            for (int i = 0; i < _config.Sections.Count; i++)
            {
                if (string.Equals(_config.Sections[i], sectionName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void HandleCardClick(int index, ClickEvent evt)
        {
            if (_config == null || index < 0 || index >= _config.Prefabs.Count)
            {
                return;
            }

            PrefabPainterEntry entry = _config.Prefabs[index];
            if (entry == null || entry.Prefab == null || !IsEntryInActiveSection(entry))
            {
                return;
            }

            bool additive = evt.ctrlKey || evt.commandKey;
            if (evt.shiftKey)
            {
                SelectCardRange(index, additive);
            }
            else if (additive)
            {
                if (_selectedPrefabs.Contains(entry.Prefab))
                {
                    _selectedPrefabs.Remove(entry.Prefab);
                    if (_selectedIndex == index)
                    {
                        _selectedIndex = FindLastSelectedEntryIndex();
                    }
                }
                else
                {
                    _selectedPrefabs.Add(entry.Prefab);
                    _selectedIndex = index;
                }

                _selectionAnchorPrefab = entry.Prefab;
            }
            else
            {
                SetSingleSelection(index);
            }

            FinalizeCardSelectionChange();
        }

        private void SelectCardRange(int clickedIndex, bool additive)
        {
            int clickedVisibleIndex = _cardEntryIndices.IndexOf(clickedIndex);
            int anchorIndex = FindEntryIndex(_selectionAnchorPrefab);
            int anchorVisibleIndex = _cardEntryIndices.IndexOf(anchorIndex);
            if (clickedVisibleIndex < 0)
            {
                return;
            }

            if (anchorVisibleIndex < 0)
            {
                anchorVisibleIndex = clickedVisibleIndex;
            }

            if (!additive)
            {
                _selectedPrefabs.Clear();
            }

            int from = Mathf.Min(anchorVisibleIndex, clickedVisibleIndex);
            int to = Mathf.Max(anchorVisibleIndex, clickedVisibleIndex);
            for (int i = from; i <= to; i++)
            {
                PrefabPainterEntry entry = _config.Prefabs[_cardEntryIndices[i]];
                if (entry != null && entry.Prefab != null && !_selectedPrefabs.Contains(entry.Prefab))
                {
                    _selectedPrefabs.Add(entry.Prefab);
                }
            }

            _selectedIndex = clickedIndex;
        }

        private void SelectIndex(int index)
        {
            if (_config == null || index < 0 || index >= _config.Prefabs.Count)
            {
                return;
            }

            PrefabPainterEntry entry = _config.Prefabs[index];
            if (entry == null || entry.Prefab == null)
            {
                return;
            }

            if (!IsEntryInActiveSection(entry))
            {
                SelectSectionByName(entry.Section);
            }

            SetSingleSelection(index);
            FinalizeCardSelectionChange();
        }

        private void SetSingleSelection(int index)
        {
            PrefabPainterEntry entry = _config.Prefabs[index];
            _selectedPrefabs.Clear();
            _selectedPrefabs.Add(entry.Prefab);
            _selectedIndex = index;
            _selectionAnchorPrefab = entry.Prefab;
        }

        private void FinalizeCardSelectionChange()
        {
            RefreshCardSelection();
            RefreshSelectedEntriesEditor();
            UpdateWindowState();

            if (_gestureActive)
            {
                _placementController.CancelPreview();
                if (PrefabPainterPlacementController.IsEntryValid(GetSelectedEntry(), out _))
                {
                    UpdateActivePreview(_lastMousePosition);
                }
                else
                {
                    CancelGesture();
                }
            }

            SceneView.RepaintAll();
        }

        private void SelectRelative(int direction)
        {
            if (_config == null || _config.Prefabs.Count == 0 || direction == 0)
            {
                return;
            }

            List<int> validIndices = new List<int>();
            for (int i = 0; i < _config.Prefabs.Count; i++)
            {
                PrefabPainterEntry entry = _config.Prefabs[i];
                if (IsEntryInActiveSection(entry) &&
                    PrefabPainterPlacementController.IsEntryValid(entry, out _))
                {
                    validIndices.Add(i);
                }
            }

            if (validIndices.Count == 0)
            {
                return;
            }

            int currentPosition = validIndices.IndexOf(_selectedIndex);
            if (currentPosition < 0)
            {
                currentPosition = direction > 0 ? -1 : 0;
            }

            int nextPosition = (currentPosition + direction + validIndices.Count) % validIndices.Count;
            SelectIndex(validIndices[nextPosition]);
        }

        private void EnsureValidSelection()
        {
            _selectedPrefabs ??= new List<GameObject>();
            if (_config == null || _config.Prefabs.Count == 0)
            {
                _selectedIndex = -1;
                _selectedPrefabs.Clear();
                _selectionAnchorPrefab = null;
                return;
            }

            PruneSelectedPrefabs();

            if (_selectedIndex >= 0 &&
                _selectedIndex < _config.Prefabs.Count &&
                IsEntryInActiveSection(_config.Prefabs[_selectedIndex]) &&
                _config.Prefabs[_selectedIndex] != null &&
                _config.Prefabs[_selectedIndex].Prefab != null &&
                _selectedPrefabs.Contains(_config.Prefabs[_selectedIndex].Prefab))
            {
                return;
            }

            int selectedEntryIndex = FindLastSelectedEntryIndex();
            if (selectedEntryIndex >= 0)
            {
                _selectedIndex = selectedEntryIndex;
                return;
            }

            _selectedIndex = -1;
            for (int i = 0; i < _config.Prefabs.Count; i++)
            {
                if (IsEntryInActiveSection(_config.Prefabs[i]) &&
                    PrefabPainterPlacementController.IsEntryValid(_config.Prefabs[i], out _))
                {
                    SetSingleSelection(i);
                    return;
                }
            }
        }

        private void PruneSelectedPrefabs()
        {
            if (_selectedPrefabs == null)
            {
                _selectedPrefabs = new List<GameObject>();
                return;
            }

            for (int i = _selectedPrefabs.Count - 1; i >= 0; i--)
            {
                int entryIndex = FindEntryIndex(_selectedPrefabs[i]);
                if (entryIndex < 0 || !IsEntryInActiveSection(_config.Prefabs[entryIndex]))
                {
                    _selectedPrefabs.RemoveAt(i);
                }
            }

            if (_selectionAnchorPrefab != null && !_selectedPrefabs.Contains(_selectionAnchorPrefab))
            {
                _selectionAnchorPrefab = _selectedPrefabs.Count > 0
                    ? _selectedPrefabs[_selectedPrefabs.Count - 1]
                    : null;
            }
        }

        private int FindEntryIndex(GameObject prefab)
        {
            if (_config == null || prefab == null)
            {
                return -1;
            }

            for (int i = 0; i < _config.Prefabs.Count; i++)
            {
                if (_config.Prefabs[i] != null && _config.Prefabs[i].Prefab == prefab)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindLastSelectedEntryIndex()
        {
            for (int i = _selectedPrefabs.Count - 1; i >= 0; i--)
            {
                int entryIndex = FindEntryIndex(_selectedPrefabs[i]);
                if (entryIndex >= 0 && IsEntryInActiveSection(_config.Prefabs[entryIndex]))
                {
                    return entryIndex;
                }
            }

            return -1;
        }

        private void RefreshCardSelection()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                PrefabPainterEntry entry = _config.Prefabs[_cardEntryIndices[i]];
                bool isBatchSelected = entry != null &&
                                       entry.Prefab != null &&
                                       _selectedPrefabs.Contains(entry.Prefab);
                ApplyCardSelectionStyle(
                    _cards[i],
                    _cardEntryIndices[i] == _selectedIndex,
                    isBatchSelected);
            }

            int visibleIndex = _cardEntryIndices.IndexOf(_selectedIndex);
            if (visibleIndex >= 0 && visibleIndex < _cards.Count)
            {
                _cardScroll?.ScrollTo(_cards[visibleIndex]);
            }

            UpdateSelectionLabel();
        }

        private void RefreshAssetPreviews()
        {
            foreach (KeyValuePair<int, Image> pair in _previewImages)
            {
                if (pair.Value == null || pair.Value.panel == null)
                {
                    continue;
                }

                GameObject prefab = EditorUtility.InstanceIDToObject(pair.Key) as GameObject;
                if (prefab == null)
                {
                    continue;
                }

                Texture2D preview = AssetPreview.GetAssetPreview(prefab);
                if (preview != null)
                {
                    pair.Value.image = preview;
                }
            }
        }

        private void FocusSelectedPrefab()
        {
            PrefabPainterEntry entry = GetSelectedEntry();
            if (entry == null || entry.Prefab == null)
            {
                return;
            }

            Selection.activeObject = entry.Prefab;
            EditorGUIUtility.PingObject(entry.Prefab);
        }

        private void TogglePainting()
        {
            if (_isPainting)
            {
                StopPainting();
            }
            else
            {
                StartPainting();
            }
        }

        private void StartPainting()
        {
            if (!CanPaint(out string reason))
            {
                ShowNotification(new GUIContent(reason));
                UpdateWindowState();
                return;
            }

            _isPainting = true;
            _feedback = default;
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            UpdateWindowState();
            SceneView.RepaintAll();
        }

        private void StopPainting()
        {
            CancelGesture();
            _isPainting = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            UpdateWindowState();
            SceneView.RepaintAll();
        }

        private void CancelGesture()
        {
            _placementController?.CancelPreview();
            _gestureActive = false;
            _eraseGestureActive = false;
            _wallSnapTemporarilyDisabled = false;
            _hasGestureSurface = false;
            _hasGestureDirection = false;
            _eraseTarget = null;
            _feedback = default;

            if (_sceneControlId != 0 && GUIUtility.hotControl == _sceneControlId)
            {
                GUIUtility.hotControl = 0;
            }

            _sceneControlId = 0;
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_isPainting || !CanPaint(out _))
            {
                StopPainting();
                return;
            }

            Event currentEvent = Event.current;
            if (currentEvent == null)
            {
                return;
            }

            _sceneControlId = GUIUtility.GetControlID(SceneControlHint, FocusType.Passive);

            if (_eraseGestureActive && currentEvent.rawType == EventType.MouseUp && currentEvent.button == 0)
            {
                _lastMousePosition = currentEvent.mousePosition;
                UpdateEraseTarget(_lastMousePosition);
                EraseCurrentTarget();
                _eraseGestureActive = false;
                _eraseTarget = null;
                GUIUtility.hotControl = 0;
                currentEvent.Use();
                sceneView.Repaint();
                return;
            }

            if (_gestureActive && currentEvent.rawType == EventType.MouseUp && currentEvent.button == 0)
            {
                _lastMousePosition = currentEvent.mousePosition;
                if (!currentEvent.alt)
                {
                    _wallSnapTemporarilyDisabled = currentEvent.shift;
                    UpdateActivePreview(_lastMousePosition);
                }

                if (!currentEvent.alt && _feedback.IsValid && _placementController.HasPreview)
                {
                    _placementController.CommitPreview();
                }
                else
                {
                    _placementController.CancelPreview();
                }

                _gestureActive = false;
                _wallSnapTemporarilyDisabled = false;
                GUIUtility.hotControl = 0;
                _feedback = PrefabPainterPlacementController.EvaluateSurface(
                    GetSelectedEntry(),
                    _config,
                    _root,
                    _lastMousePosition);
                currentEvent.Use();
                sceneView.Repaint();
                return;
            }

            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
            {
                if (_gestureActive || _eraseGestureActive)
                {
                    CancelGesture();
                    currentEvent.Use();
                    sceneView.Repaint();
                }

                return;
            }

            if (_gestureActive &&
                (currentEvent.type == EventType.KeyDown || currentEvent.type == EventType.KeyUp) &&
                (currentEvent.keyCode == KeyCode.LeftShift || currentEvent.keyCode == KeyCode.RightShift))
            {
                _wallSnapTemporarilyDisabled = currentEvent.type == EventType.KeyDown;
                UpdateActivePreview(_lastMousePosition);
                currentEvent.Use();
                sceneView.Repaint();
                return;
            }

            if (_gestureActive && currentEvent.type == EventType.MouseDown && currentEvent.button == 1)
            {
                CancelGesture();
                currentEvent.Use();
                sceneView.Repaint();
                return;
            }

            if (currentEvent.alt)
            {
                if (_gestureActive)
                {
                    _placementController.CancelPreview();
                    _gestureActive = false;
                    _feedback = default;

                    if (GUIUtility.hotControl == _sceneControlId)
                    {
                        GUIUtility.hotControl = 0;
                    }
                }

                if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
                {
                    _eraseGestureActive = true;
                    _lastMousePosition = currentEvent.mousePosition;
                    UpdateEraseTarget(_lastMousePosition);
                    GUIUtility.hotControl = _sceneControlId;
                    currentEvent.Use();
                    sceneView.Repaint();
                    return;
                }

                if (_eraseGestureActive && currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
                {
                    _lastMousePosition = currentEvent.mousePosition;
                    UpdateEraseTarget(_lastMousePosition);
                    currentEvent.Use();
                    sceneView.Repaint();
                    return;
                }

                if (currentEvent.type == EventType.MouseMove || currentEvent.type == EventType.KeyDown)
                {
                    _lastMousePosition = currentEvent.mousePosition;
                    UpdateEraseTarget(_lastMousePosition);
                    sceneView.Repaint();
                    return;
                }

                if (currentEvent.type == EventType.Repaint)
                {
                    DrawEraseVisuals();
                }

                return;
            }

            if (currentEvent.type == EventType.ScrollWheel)
            {
                int direction = currentEvent.delta.y > 0f ? 1 : -1;
                SelectRelative(direction);
                currentEvent.Use();
                sceneView.Repaint();
                return;
            }

            if (currentEvent.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(_sceneControlId);
                return;
            }

            if (currentEvent.type == EventType.Repaint)
            {
                DrawSceneVisuals();
                return;
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                _gestureActive = true;
                _lastMousePosition = currentEvent.mousePosition;
                _gestureStartMousePosition = currentEvent.mousePosition;
                _gestureRandomYaw = GenerateRandomYaw();
                _wallSnapTemporarilyDisabled = currentEvent.shift;
                _hasGestureDirection = false;

                PrefabPainterPlacementFeedback initialFeedback =
                    PrefabPainterPlacementController.EvaluateSurface(
                        GetSelectedEntry(),
                        _config,
                        _root,
                        _gestureStartMousePosition);
                _hasGestureSurface = initialFeedback.IsValid;
                if (_hasGestureSurface)
                {
                    _gestureSurfacePoint = initialFeedback.SurfacePoint;
                    _gestureSurfaceNormal = initialFeedback.SurfaceNormal;
                    _gestureDirectionPoint = _gestureSurfacePoint;
                }

                GUIUtility.hotControl = _sceneControlId;
                UpdateActivePreview(_lastMousePosition);
                currentEvent.Use();
                sceneView.Repaint();
                return;
            }

            if (_gestureActive && currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
            {
                _lastMousePosition = currentEvent.mousePosition;
                _wallSnapTemporarilyDisabled = currentEvent.shift;
                UpdateActivePreview(_lastMousePosition);
                currentEvent.Use();
                sceneView.Repaint();
                return;
            }

            if (!_gestureActive && currentEvent.type == EventType.MouseMove)
            {
                _lastMousePosition = currentEvent.mousePosition;
                _feedback = PrefabPainterPlacementController.EvaluateSurface(
                    GetSelectedEntry(),
                    _config,
                    _root,
                    _lastMousePosition);
                sceneView.Repaint();
            }
        }

        private void UpdateActivePreview(Vector2 mousePosition)
        {
            PrefabPainterEntry entry = GetSelectedEntry();
            Vector2 placementMousePosition = mousePosition;
            float? directionalYaw = null;
            _hasGestureDirection = false;

            if (_directionalDragRotation && _gestureActive)
            {
                placementMousePosition = _gestureStartMousePosition;
                if (_hasGestureSurface)
                {
                    Ray pointerRay = HandleUtility.GUIPointToWorldRay(mousePosition);
                    if (PrefabPainterDragRotation.TryCalculateYaw(
                            _gestureStartMousePosition,
                            mousePosition,
                            _gestureSurfacePoint,
                            _gestureSurfaceNormal,
                            pointerRay,
                            DirectionalDragThresholdPixels,
                            out float calculatedYaw,
                            out Vector3 directionPoint))
                    {
                        directionalYaw = calculatedYaw;
                        _gestureDirectionPoint = directionPoint;
                        _hasGestureDirection = true;
                    }
                }
            }

            _placementController.UpdatePreview(
                entry,
                _config,
                _root,
                placementMousePosition,
                _gestureRandomYaw,
                directionalYaw,
                _wallSnapTemporarilyDisabled,
                out _feedback);
        }

        private void DrawSceneVisuals()
        {
            if (_feedback.HasSurfaceHit)
            {
                Color markerColor = _feedback.IsValid ? AccentColor : InvalidColor;
                float handleScale = HandleUtility.GetHandleSize(_feedback.SurfacePoint);
                float radius = MarkerRadius * handleScale;

                Handles.color = markerColor;
                Handles.DrawWireDisc(_feedback.SurfacePoint, _feedback.SurfaceNormal, radius);
                Handles.DrawLine(
                    _feedback.SurfacePoint,
                    _feedback.SurfacePoint + _feedback.SurfaceNormal * radius * 1.8f,
                    2f);
            }

            if (_feedback.IsValid && _placementController.HasPreview)
            {
                _placementController.DrawPreviewBounds(new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.9f));
            }

            if (_feedback.IsWallPlacement && _feedback.IsValid)
            {
                DrawHeightGuide();
            }

            if (_directionalDragRotation &&
                _gestureActive &&
                _hasGestureDirection &&
                _feedback.IsValid &&
                !_feedback.IsWallPlacement &&
                !_feedback.HasNearbyWall)
            {
                Handles.color = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.95f);
                Handles.DrawLine(_gestureSurfacePoint, _gestureDirectionPoint, 3f);
                float arrowSize = HandleUtility.GetHandleSize(_gestureDirectionPoint) * 0.08f;
                Handles.ConeHandleCap(
                    0,
                    _gestureDirectionPoint,
                    Quaternion.LookRotation((_gestureDirectionPoint - _gestureSurfacePoint).normalized),
                    arrowSize,
                    EventType.Repaint);
            }

            DrawSceneOverlay();
        }

        private void UpdateEraseTarget(Vector2 mousePosition)
        {
            _eraseTarget = null;
            if (_root == null)
            {
                return;
            }

            HashSet<GameObject> candidates = new HashSet<GameObject>();
            GameObject exactPick = HandleUtility.PickGameObject(mousePosition, false);
            AddEraseCandidate(exactPick, candidates);
            if (candidates.Count > 0)
            {
                foreach (GameObject candidate in candidates)
                {
                    _eraseTarget = candidate;
                    return;
                }
            }

            float pickRadius = _config != null ? _config.EraserPickRadiusPixels : 14f;
            Rect pickRect = new Rect(
                mousePosition.x - pickRadius,
                mousePosition.y - pickRadius,
                pickRadius * 2f,
                pickRadius * 2f);
            GameObject[] rectanglePicks = HandleUtility.PickRectObjects(pickRect, false);
            for (int i = 0; i < rectanglePicks.Length; i++)
            {
                AddEraseCandidate(rectanglePicks[i], candidates);
            }

            Camera sceneCamera = SceneView.lastActiveSceneView != null
                ? SceneView.lastActiveSceneView.camera
                : null;
            float bestScore = float.PositiveInfinity;
            foreach (GameObject candidate in candidates)
            {
                if (candidate == null || !TryGetWorldBounds(candidate, out Bounds bounds))
                {
                    continue;
                }

                Vector2 guiCenter = HandleUtility.WorldToGUIPoint(bounds.center);
                float screenDistance = (guiCenter - mousePosition).sqrMagnitude;
                float depth = 0f;
                if (sceneCamera != null)
                {
                    depth = Vector3.Dot(
                        bounds.center - sceneCamera.transform.position,
                        sceneCamera.transform.forward);
                    if (depth < 0f)
                    {
                        continue;
                    }
                }

                float score = screenDistance + depth * 0.01f;
                if (score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                _eraseTarget = candidate;
            }
        }

        private void AddEraseCandidate(GameObject pickedObject, ISet<GameObject> candidates)
        {
            if (pickedObject == null || _root == null)
            {
                return;
            }

            Transform candidate = pickedObject.transform;
            while (candidate != null && candidate.parent != _root)
            {
                candidate = candidate.parent;
            }

            if (candidate != null && candidate.parent == _root)
            {
                candidates.Add(candidate.gameObject);
            }
        }

        private void EraseCurrentTarget()
        {
            if (_eraseTarget == null || _root == null || _eraseTarget.transform.parent != _root)
            {
                return;
            }

            Undo.DestroyObjectImmediate(_eraseTarget);
            if (_root != null && _root.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(_root.gameObject.scene);
            }
        }

        private void DrawEraseVisuals()
        {
            if (_eraseTarget != null && TryGetWorldBounds(_eraseTarget, out Bounds bounds))
            {
                Handles.color = InvalidColor;
                Handles.DrawWireCube(bounds.center, bounds.size);

                float markerSize = HandleUtility.GetHandleSize(bounds.center) * 0.12f;
                Handles.DrawLine(
                    bounds.center - Vector3.right * markerSize,
                    bounds.center + Vector3.right * markerSize,
                    3f);
                Handles.DrawLine(
                    bounds.center - Vector3.up * markerSize,
                    bounds.center + Vector3.up * markerSize,
                    3f);
            }

            Handles.BeginGUI();
            Rect overlayRect = new Rect(16f, 16f, 350f, 92f);
            GUIStyle boxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 9, 9)
            };
            GUI.Box(overlayRect, GUIContent.none, boxStyle);

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = new Color(1f, 0.48f, 0.42f, 1f) }
            };
            GUI.Label(new Rect(29f, 25f, 324f, 20f), "Режим стирания", titleStyle);
            GUI.Label(
                new Rect(29f, 47f, 324f, 18f),
                _eraseTarget != null
                    ? $"{_eraseTarget.name} · отпустите ЛКМ для удаления"
                    : "Под курсором нет объекта из выбранного Root",
                EditorStyles.miniLabel);
            GUI.Label(new Rect(29f, 65f, 324f, 18f), "Esc: отменить", EditorStyles.miniLabel);
            Handles.EndGUI();
        }

        private static bool TryGetWorldBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (hasBounds)
            {
                return true;
            }

            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return hasBounds;
        }

        private void DrawHeightGuide()
        {
            if (!_feedback.HasFloor)
            {
                GUIStyle warningStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    normal = { textColor = new Color(1f, 0.7f, 0.36f, 1f) },
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
                Handles.Label(_feedback.AnchorPosition + Vector3.up * 0.15f, "Пол не найден", warningStyle);
                return;
            }

            Color guideColor = new Color(0.32f, 0.88f, 1f, 0.95f);
            Handles.color = guideColor;
            Handles.DrawDottedLine(_feedback.FloorPoint, _feedback.AnchorPosition, 4f);

            float discSize = HandleUtility.GetHandleSize(_feedback.FloorPoint) * 0.08f;
            Handles.DrawWireDisc(_feedback.FloorPoint, Vector3.up, discSize);

            GUIStyle labelStyle = new GUIStyle(EditorStyles.helpBox)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };
            Vector3 labelPosition = Vector3.Lerp(_feedback.FloorPoint, _feedback.AnchorPosition, 0.5f);
            Handles.Label(labelPosition, $"{_feedback.FloorHeight:0.00} м", labelStyle);
        }

        private void DrawSceneOverlay()
        {
            PrefabPainterEntry entry = GetSelectedEntry();
            string prefabName = entry != null && entry.Prefab != null ? entry.Prefab.name : "Нет выбранного prefab-а";
            string mode = entry != null && entry.WallOnly ? "Только стены" : "Пол и склоны";
            string placementState;
            if (_gestureActive)
            {
                placementState = _feedback.IsValid
                    ? _directionalDragRotation
                        ? _feedback.HasNearbyWall || _feedback.IsWallPlacement
                            ? "Ориентация по стене · отпустите ЛКМ"
                            : _hasGestureDirection
                                ? "Поворот задан · отпустите ЛКМ"
                                : "Потяните для поворота или отпустите"
                        : "Отпустите ЛКМ, чтобы разместить"
                    : _feedback.Message;
            }
            else
            {
                placementState = _directionalDragRotation
                    ? "ЛКМ: позиция · протягивание: направление"
                    : "Зажмите ЛКМ и перемещайте объект";
            }

            if (_gestureActive && entry != null && !entry.WallOnly && _wallSnapTemporarilyDisabled)
            {
                placementState = "Shift: прилипание к стене временно отключено";
            }

            Handles.BeginGUI();
            Rect overlayRect = new Rect(16f, 16f, 350f, 92f);
            GUIStyle boxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 9, 9)
            };
            GUI.Box(overlayRect, GUIContent.none, boxStyle);

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.55f, 0.92f, 1f, 1f) }
            };
            GUI.Label(new Rect(29f, 25f, 324f, 20f), prefabName, titleStyle);
            GUI.Label(new Rect(29f, 46f, 324f, 18f), $"{mode} · {placementState}", EditorStyles.miniLabel);
            GUI.Label(
                new Rect(29f, 68f, 324f, 18f),
                _directionalDragRotation
                    ? "Направление: ВКЛ  ·  Shift: без стены  ·  Alt+ЛКМ: стереть"
                    : "Колесо: выбрать  ·  Shift: без стены  ·  Alt+ЛКМ: стереть",
                EditorStyles.miniLabel);
            Handles.EndGUI();
        }

        private float GenerateRandomYaw()
        {
            Vector2 range = _config != null ? _config.RandomYawRange : Vector2.zero;
            return Mathf.Lerp(range.x, range.y, (float)_random.NextDouble());
        }

        private bool CanPaint(out string reason)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                reason = "Prefab Painter доступен только в Edit Mode.";
                return false;
            }

            if (_config == null)
            {
                reason = "Назначьте или создайте PrefabPainterConfig.";
                return false;
            }

            if (_root == null)
            {
                reason = "Назначьте или создайте Root в открытой сцене.";
                return false;
            }

            if (EditorUtility.IsPersistent(_root) || !_root.gameObject.scene.IsValid())
            {
                reason = "Root должен быть объектом открытой сцены.";
                return false;
            }

            EnsureValidSelection();
            PrefabPainterEntry entry = GetSelectedEntry();
            if (!PrefabPainterPlacementController.IsEntryValid(entry, out reason))
            {
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private PrefabPainterEntry GetSelectedEntry()
        {
            if (_config == null || _selectedIndex < 0 || _selectedIndex >= _config.Prefabs.Count)
            {
                return null;
            }

            return _config.Prefabs[_selectedIndex];
        }

        private void UpdateWindowState()
        {
            if (_paintButton == null)
            {
                return;
            }

            bool canPaint = CanPaint(out string reason);
            _paintButton.text = _isPainting ? "●  Режим размещения включён" : "Включить режим размещения";
            _paintButton.SetEnabled(canPaint || _isPainting);
            _paintButton.style.backgroundColor = _isPainting ? AccentSoftColor : new Color(0.18f, 0.22f, 0.27f, 1f);
            _paintButton.style.color = _isPainting ? new Color(0.72f, 0.96f, 1f, 1f) : Color.white;
            _paintButton.style.borderLeftColor = _isPainting ? AccentColor : PanelBorderColor;
            _paintButton.style.borderRightColor = _isPainting ? AccentColor : PanelBorderColor;
            _paintButton.style.borderTopColor = _isPainting ? AccentColor : PanelBorderColor;
            _paintButton.style.borderBottomColor = _isPainting ? AccentColor : PanelBorderColor;

            if (_statusLabel != null)
            {
                bool hasScaleWarning = _root != null && !IsUnitScale(_root.lossyScale);
                if (canPaint && hasScaleWarning)
                {
                    _statusLabel.text = "Root имеет неединичный мировой масштаб — он повлияет на размер и смещения префабов.";
                    SetStatusStyle(new Color(0.32f, 0.24f, 0.1f, 1f), new Color(1f, 0.76f, 0.35f, 1f));
                }
                else if (canPaint)
                {
                    _statusLabel.text = _isPainting
                        ? "Prefab Painter перехватывает ЛКМ и колесо мыши в Scene View."
                        : "Готово к размещению.";
                    SetStatusStyle(new Color(0.08f, 0.25f, 0.27f, 1f), new Color(0.48f, 0.94f, 1f, 1f));
                }
                else
                {
                    _statusLabel.text = reason;
                    SetStatusStyle(new Color(0.31f, 0.13f, 0.13f, 1f), new Color(1f, 0.58f, 0.54f, 1f));
                }
            }

            UpdateSelectionLabel();
            _focusPrefabButton?.SetEnabled(GetSelectedEntry()?.Prefab != null);
            if (_removePrefabButton != null)
            {
                int selectedCount = _selectedPrefabs != null ? _selectedPrefabs.Count : 0;
                _removePrefabButton.text = selectedCount > 1 ? $"Удалить ({selectedCount})" : "Удалить";
                _removePrefabButton.SetEnabled(selectedCount > 0);
            }
        }

        private void UpdateSelectionLabel()
        {
            if (_selectionLabel == null)
            {
                return;
            }

            PrefabPainterEntry entry = GetSelectedEntry();
            int visibleIndex = _cardEntryIndices.IndexOf(_selectedIndex);
            int selectedCount = _selectedPrefabs != null ? _selectedPrefabs.Count : 0;
            _selectionLabel.text = entry != null && entry.Prefab != null && visibleIndex >= 0
                ? selectedCount > 1
                    ? $"Выбрано: {selectedCount} · активно {visibleIndex + 1}/{_cardEntryIndices.Count}"
                    : $"{visibleIndex + 1}/{_cardEntryIndices.Count}"
                : "Ничего не выбрано";
        }

        private void UpdateCardScaleLabel()
        {
            if (_cardScaleLabel != null)
            {
                _cardScaleLabel.text = $"Размер карточек: {Mathf.RoundToInt(_cardScale * 100f)}%";
            }

            if (_cardRowCountLabel != null)
            {
                _cardRowCountLabel.text = $"Рядов: {_cardRowCount}";
            }
        }

        private float GetScaledCardHeight()
        {
            return CardHeight * Mathf.Clamp(_cardScale, 0.25f, 1.25f);
        }

        private float GetCardGridHeight()
        {
            float scale = Mathf.Clamp(_cardScale, 0.25f, 1.25f);
            return Mathf.Clamp(_cardRowCount, 1, 6) * (GetScaledCardHeight() + 8f * scale);
        }

        private void SetStatusStyle(Color background, Color textColor)
        {
            _statusLabel.style.backgroundColor = background;
            _statusLabel.style.color = textColor;
        }

        private void PollConfigChanges()
        {
            int currentHash = ComputeConfigHash();
            if (currentHash == _lastConfigHash)
            {
                return;
            }

            CancelGesture();
            EnsureValidSelection();
            RefreshCards();

            if (_isPainting && !CanPaint(out _))
            {
                StopPainting();
                return;
            }

            UpdateWindowState();
            SceneView.RepaintAll();
        }

        private int ComputeConfigHash()
        {
            if (_config == null)
            {
                return 0;
            }

            unchecked
            {
                int hash = _config.GetInstanceID();
                hash = hash * 31 + _config.SurfaceMask.value;
                hash = hash * 31 + _config.MaxFloorSlopeAngle.GetHashCode();
                hash = hash * 31 + _config.WallDeviationAngle.GetHashCode();
                hash = hash * 31 + _config.SurfaceOffset.GetHashCode();
                hash = hash * 31 + _config.NearbyWallDistance.GetHashCode();
                hash = hash * 31 + _config.NearbyWallProbeHeight.GetHashCode();
                hash = hash * 31 + _config.FloorProbeDistance.GetHashCode();
                hash = hash * 31 + _config.EraserPickRadiusPixels;
                hash = hash * 31 + _config.RandomYawRange.GetHashCode();
                hash = hash * 31 + _config.Sections.Count;
                for (int i = 0; i < _config.Sections.Count; i++)
                {
                    hash = hash * 31 + _config.Sections[i].GetHashCode();
                }

                hash = hash * 31 + _config.Prefabs.Count;

                for (int i = 0; i < _config.Prefabs.Count; i++)
                {
                    PrefabPainterEntry entry = _config.Prefabs[i];
                    if (entry == null)
                    {
                        hash *= 31;
                        continue;
                    }

                    hash = hash * 31 + (entry.Prefab != null ? entry.Prefab.GetInstanceID() : 0);
                    hash = hash * 31 + entry.WallOnly.GetHashCode();
                    hash = hash * 31 + entry.AttachmentSide.GetHashCode();
                    hash = hash * 31 + entry.LocalOffset.GetHashCode();
                    hash = hash * 31 + entry.StartRotationEuler.GetHashCode();
                    hash = hash * 31 + entry.Section.GetHashCode();
                }

                return hash;
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
            {
                StopPainting();
            }
        }

        private void OnProjectChanged()
        {
            CancelGesture();
            RefreshCards();

            if (_isPainting && !CanPaint(out _))
            {
                StopPainting();
                return;
            }

            UpdateWindowState();
        }

        private void OnUndoRedo()
        {
            CancelGesture();
            EnsureValidSelection();
            RefreshCards();
            RefreshConfigInspector();
            UpdateWindowState();
            SceneView.RepaintAll();
        }

        private static VisualElement CreatePanel()
        {
            VisualElement panel = new VisualElement();
            panel.style.backgroundColor = PanelColor;
            panel.style.paddingLeft = 10f;
            panel.style.paddingRight = 10f;
            panel.style.paddingTop = 9f;
            panel.style.paddingBottom = 9f;
            panel.style.borderLeftWidth = 1f;
            panel.style.borderRightWidth = 1f;
            panel.style.borderTopWidth = 1f;
            panel.style.borderBottomWidth = 1f;
            panel.style.borderLeftColor = PanelBorderColor;
            panel.style.borderRightColor = PanelBorderColor;
            panel.style.borderTopColor = PanelBorderColor;
            panel.style.borderBottomColor = PanelBorderColor;
            panel.style.borderTopLeftRadius = 7f;
            panel.style.borderTopRightRadius = 7f;
            panel.style.borderBottomLeftRadius = 7f;
            panel.style.borderBottomRightRadius = 7f;
            return panel;
        }

        private static VisualElement CreateRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 3f;
            row.style.marginBottom = 3f;
            return row;
        }

        private static Label CreateSectionHeading(string text)
        {
            Label heading = new Label(text);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.fontSize = 12f;
            heading.style.color = Color.white;
            heading.style.marginBottom = 3f;
            return heading;
        }

        private static void StyleSmallButton(Button button)
        {
            button.style.height = 25f;
            button.style.marginLeft = 6f;
            button.style.paddingLeft = 11f;
            button.style.paddingRight = 11f;
        }

        private static void ApplyCardSelectionStyle(
            VisualElement card,
            bool isPrimary,
            bool isBatchSelected)
        {
            Color borderColor = isPrimary
                ? AccentColor
                : isBatchSelected
                    ? new Color(0.95f, 0.65f, 0.24f, 1f)
                    : PanelBorderColor;
            card.style.backgroundColor = isPrimary
                ? AccentSoftColor
                : isBatchSelected
                    ? new Color(0.3f, 0.22f, 0.1f, 1f)
                    : new Color(0.12f, 0.14f, 0.17f, 1f);
            card.style.borderLeftColor = borderColor;
            card.style.borderRightColor = borderColor;
            card.style.borderTopColor = borderColor;
            card.style.borderBottomColor = borderColor;
        }

        private static bool ContainsPrefabAsset(IReadOnlyList<UnityEngine.Object> objects)
        {
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] is GameObject prefab &&
                    EditorUtility.IsPersistent(prefab) &&
                    PrefabUtility.IsPartOfPrefabAsset(prefab))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsUnitScale(Vector3 scale)
        {
            return Mathf.Approximately(scale.x, 1f) &&
                   Mathf.Approximately(scale.y, 1f) &&
                   Mathf.Approximately(scale.z, 1f);
        }
    }
}
