#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Owner-controlled, non-destructive partition of the original O wall mesh.</summary>
public sealed class BossWallCutWindow : EditorWindow
{
    private const string OutputRoot = "Assets/Generated/ArenaGeometry";
    private const string UndoName = "Manual boss wall cut";
    [SerializeField] private ArenaBossBoundary _boundary;
    [SerializeField] private MeshFilter _wall;
    [SerializeField] private Mesh _source;
    [SerializeField] private List<Vector3> _polygon = new();
    [SerializeField] private bool[] _selected = Array.Empty<bool>();
    [SerializeField] private bool _drawing;
    [SerializeField] private bool _showPreview = true;
    [SerializeField] private bool _showWire = true;
    [SerializeField] private int _mode;
    [SerializeField] private Matrix4x4 _sourceMatrix;
    [SerializeField] private string _sourceHash;
    [SerializeField] private string _message;
    private Vector3[] _vertices;
    private Vector3[] _centers;
    private Vector3[] _wire;
    private int[] _triangles;
    private bool[] _preview;
    private readonly Vector3[] _face = new Vector3[3];
    private int _controlId;
    private Vector2 _scroll;

    [MenuItem("Tools/Logo Survivor/Manual Boss Wall Cut")]
    public static void Open()
    {
        var window = GetWindow<BossWallCutWindow>("Вырезать стену О");
        window.minSize = new Vector2(390, 520);
        if (window._boundary == null) window.FindBoundary();
        window.Show();
    }

    private void OnEnable()
    {
        _polygon ??= new List<Vector3>();
        _selected ??= Array.Empty<bool>();
        SceneView.duringSceneGui += OnSceneGUI;
        Undo.undoRedoPerformed += OnUndoRedo;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (_source != null && _wall != null && _source.isReadable) BuildCache();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        ReleaseMouse();
        SceneView.RepaintAll();
    }

    private void OnUndoRedo()
    {
        _drawing = false;
        _polygon.Clear();
        ReleaseMouse();
        RefreshPreview();
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        _drawing = false;
        ReleaseMouse();
        Repaint();
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.HelpBox("1. Найдите стену. 2. Вид сверху → обведите внутреннюю стенку кликами. "
            + "3. Enter подтверждает выделение. 4. Проверьте красный участок и примените.", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            EditorGUI.BeginChangeCheck();
            var target = (ArenaBossBoundary)EditorGUILayout.ObjectField("Граница босса", _boundary,
                typeof(ArenaBossBoundary), true);
            if (EditorGUI.EndChangeCheck()) { _boundary = target; LoadSource(); }
            if (GUILayout.Button("Найти BossBoundary_O в открытой сцене")) FindBoundary();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Стена в сцене", _wall, typeof(MeshFilter), true);
                EditorGUILayout.ObjectField("Полный исходный меш", _source, typeof(Mesh), false);
            }
            string error = ValidateContext();
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
            using (new EditorGUI.DisabledScope(error != null))
            {
                if (GUILayout.Button("Вид сверху: центр О")) FrameTop();
                EditorGUI.BeginChangeCheck();
                _showPreview = EditorGUILayout.Toggle("Красный предпросмотр", _showPreview);
                _showWire = EditorGUILayout.Toggle("Каркас полного исходника", _showWire);
                if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
                EditorGUI.BeginChangeCheck();
                _mode = GUILayout.Toolbar(_mode, new[] { "Заменить", "Добавить", "Убрать" });
                if (EditorGUI.EndChangeCheck()) RefreshPreview();
                if (GUILayout.Button(_drawing ? "Отменить текущую обводку (Esc)" : "Начать обводку"))
                {
                    _drawing = !_drawing;
                    _polygon.Clear();
                    ReleaseMouse();
                    if (_drawing) { FrameTop(); SceneView.lastActiveSceneView?.Focus(); }
                    RefreshPreview();
                }
                if (_drawing)
                {
                    EditorGUILayout.HelpBox("ЛКМ — вершина; Backspace — убрать последнюю; Enter — "
                        + "подтвердить контур; Esc — отменить. Alt и средняя кнопка остаются для камеры. "
                        + "Обведите стенку с небольшим запасом, не задевая внешний контур О.", MessageType.None);
                    EditorGUILayout.LabelField($"Точек: {_polygon.Count}");
                    using (new EditorGUI.DisabledScope(!ValidPolygon()))
                        if (GUILayout.Button("Подтвердить контур (Enter)")) ConfirmPolygon();
                    if (_polygon.Count >= 3 && !ValidPolygon())
                        EditorGUILayout.HelpBox("Контур пересекает сам себя или не имеет площади.", MessageType.Warning);
                }
                int count = _preview == null ? 0 : _preview.Count(value => value);
                EditorGUILayout.LabelField($"Выбрано: {count} / {_selected.Length} треугольников");
                if (GUILayout.Button("Очистить всё выделение"))
                {
                    Array.Clear(_selected, 0, _selected.Length);
                    _polygon.Clear();
                    _drawing = false;
                    ReleaseMouse();
                    RefreshPreview();
                }
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Выбираются целые треугольники по их центрам в проекции XZ, "
                    + "на всю высоту. GLB и старые mesh assets не изменяются. Применение создаёт две "
                    + "новые копии и подключает красный участок к открытию босса. Сцена не сохраняется автоматически.",
                    MessageType.Info);
                using (new EditorGUI.DisabledScope(_drawing || count == 0 || count == _selected.Length))
                    if (GUILayout.Button("Применить: создать меши и подключить стенку", GUILayout.Height(34))) ApplyCut();
                EditorGUILayout.HelpBox("Undo (Ctrl+Z) откатывает сцену; новые mesh assets остаются на диске. "
                    + "NavMesh и круговые runtime-барьеры/связи не пересчитываются — проход к боссу нужно проверить.",
                    MessageType.Warning);
            }
        }
        if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Info);
        EditorGUILayout.EndScrollView();
    }

    private void FindBoundary()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var candidates = Object.FindObjectsByType<ArenaBossBoundary>(FindObjectsInactive.Include)
            .Where(item => item.gameObject.scene ==
                UnityEngine.SceneManagement.SceneManager.GetActiveScene()).ToArray();
        if (candidates.Length != 1)
        {
            _message = "Нужна открытая Game-сцена с одной ArenaBossBoundary; "
                + "при нескольких границах перетащите нужный объект в поле выше.";
            return;
        }
        _boundary = candidates[0];
        LoadSource();
    }

    private void LoadSource()
    {
        _wall = _boundary != null ? _boundary.LogoWallFilter : null;
        // Never use the already cut wall as a fallback: it has missing triangles.
        _source = _boundary != null ? _boundary.SourceLogoWallMesh : null;
        _selected = Array.Empty<bool>();
        _polygon.Clear();
        _drawing = false;
        ReleaseMouse();
        _vertices = null;
        _preview = null;
        _message = null;
        if (_wall != null && _source != null && _source.isReadable
            && _source.subMeshCount == 1 && _source.GetTopology(0) == MeshTopology.Triangles)
        {
            _sourceMatrix = _wall.transform.localToWorldMatrix;
            _sourceHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(_source)).ToString();
            BuildCache();
        }
        Repaint();
        SceneView.RepaintAll();
    }

    private void BuildCache()
    {
        _selected ??= Array.Empty<bool>();
        _vertices = _source.vertices;
        _triangles = _source.triangles;
        int count = _triangles.Length / 3;
        if (_selected.Length != count) _selected = new bool[count];
        _centers = new Vector3[count];
        _wire = new Vector3[count * 6];
        for (int i = 0; i < count; i++)
        {
            Vector3 a = _vertices[_triangles[i * 3]], b = _vertices[_triangles[i * 3 + 1]],
                c = _vertices[_triangles[i * 3 + 2]];
            _centers[i] = _sourceMatrix.MultiplyPoint3x4((a + b + c) / 3f);
            int line = i * 6;
            _wire[line] = a; _wire[line + 1] = b;
            _wire[line + 2] = b; _wire[line + 3] = c;
            _wire[line + 4] = c; _wire[line + 5] = a;
        }
        RefreshPreview();
    }

    private string ValidateContext()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Инструмент доступен только вне Play Mode.";
        if (_boundary == null || _wall == null || _source == null)
            return "Не назначены граница, стена или полный Source Logo Wall Mesh. Откройте Game и нажмите «Найти».";
        if (EditorUtility.IsPersistent(_boundary) || !_boundary.gameObject.scene.IsValid()
            || !_boundary.gameObject.scene.isLoaded || EditorSceneManager.IsPreviewScene(_boundary.gameObject.scene))
            return "Выберите границу обычной открытой сцены, не prefab asset/stage.";
        if (_wall.gameObject.scene != _boundary.gameObject.scene || !_wall.gameObject.activeInHierarchy)
            return "Стена должна быть активной и находиться в той же сцене.";
        if (_boundary.LogoWallFilter != _wall || _boundary.SourceLogoWallMesh != _source
            || _wall.transform.localToWorldMatrix != _sourceMatrix)
            return "Источник/transform изменён. Нажмите «Найти» и повторите выделение.";
        if (!_source.isReadable || _source.subMeshCount != 1 || _source.GetTopology(0) != MeshTopology.Triangles
            || !EditorUtility.IsPersistent(_source) || _vertices == null || _selected.Length == 0)
            return "Нужен читаемый asset меша с одним submesh из треугольников.";
        if (_wall.GetComponent<MeshRenderer>() == null) return "На стене отсутствует MeshRenderer.";
        return null;
    }

    private List<BossWallSelectionMath.Point> Polygon2D() =>
        _polygon.Select(p => new BossWallSelectionMath.Point(p.x, p.z)).ToList();

    private bool ValidPolygon() => BossWallSelectionMath.IsSimplePolygon(Polygon2D());

    private void RefreshPreview()
    {
        _selected ??= Array.Empty<bool>();
        _polygon ??= new List<Vector3>();
        _preview = (bool[])_selected.Clone();
        if (_centers != null && ValidPolygon())
        {
            var polygon = Polygon2D();
            for (int i = 0; i < _preview.Length; i++)
            {
                bool inside = BossWallSelectionMath.Contains(polygon,
                    new BossWallSelectionMath.Point(_centers[i].x, _centers[i].z));
                _preview[i] = _mode == 0 ? inside : _mode == 1 ? _selected[i] || inside : _selected[i] && !inside;
            }
        }
        Repaint();
        SceneView.RepaintAll();
    }

    private void ConfirmPolygon()
    {
        if (!ValidPolygon()) return;
        RefreshPreview();
        _selected = (bool[])_preview.Clone();
        _polygon.Clear();
        _drawing = false;
        ReleaseMouse();
        RefreshPreview();
    }

    private void FrameTop()
    {
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null || _boundary == null) return;
        float radius = _boundary.transform.TransformVector(Vector3.right * _boundary.LocalRadius).magnitude;
        view.in2DMode = false;
        view.LookAt(_boundary.transform.position, Quaternion.LookRotation(Vector3.down, Vector3.forward),
            Mathf.Max(radius * 2.8f, 0.5f), true, true);
    }

    private void OnSceneGUI(SceneView view)
    {
        if (ValidateContext() != null) { ReleaseMouse(); return; }
        Event evt = Event.current;
        _controlId = GUIUtility.GetControlID(FocusType.Passive);
        if (_drawing && !evt.alt)
        {
            if (evt.type == EventType.Layout) HandleUtility.AddDefaultControl(_controlId);
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
                Plane plane = new(Vector3.up, _boundary.transform.position);
                if (plane.Raycast(ray, out float distance))
                {
                    _polygon.Add(ray.GetPoint(distance));
                    GUIUtility.hotControl = _controlId;
                    RefreshPreview();
                }
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && evt.button == 0 && GUIUtility.hotControl == _controlId)
            { ReleaseMouse(); evt.Use(); }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == _controlId) evt.Use();
            else if (evt.type == EventType.KeyDown)
            {
                if (evt.keyCode == KeyCode.Escape)
                { _drawing = false; _polygon.Clear(); ReleaseMouse(); RefreshPreview(); evt.Use(); }
                else if (evt.keyCode == KeyCode.Backspace && _polygon.Count > 0)
                { _polygon.RemoveAt(_polygon.Count - 1); RefreshPreview(); evt.Use(); }
                else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                { ConfirmPolygon(); evt.Use(); }
            }
        }
        // Mouse release must still work if Alt was pressed during a click.
        if (evt.rawType == EventType.MouseUp && evt.button == 0) ReleaseMouse();
        if (evt.type != EventType.Repaint) return;
        Matrix4x4 oldMatrix = Handles.matrix;
        Color oldColor = Handles.color;
        CompareFunction oldDepth = Handles.zTest;
        try
        {
            Handles.zTest = CompareFunction.Always;
            Handles.matrix = _sourceMatrix;
            if (_showWire) { Handles.color = new Color(0.7f, 0.85f, 1f, 0.18f); Handles.DrawLines(_wire); }
            if (_showPreview && _preview != null)
            {
                Handles.color = new Color(1f, 0.15f, 0.08f, 0.65f);
                for (int i = 0; i < _preview.Length; i++)
                {
                    if (!_preview[i]) continue;
                    for (int corner = 0; corner < 3; corner++) _face[corner] = _vertices[_triangles[i * 3 + corner]];
                    Handles.DrawAAConvexPolygon(_face);
                }
            }
            Handles.matrix = Matrix4x4.identity;
            Handles.color = Color.yellow;
            if (_polygon.Count >= 2)
            {
                var outline = new List<Vector3>(_polygon) { _polygon[0] };
                Handles.DrawAAPolyLine(3f, outline.ToArray());
            }
            for (int i = 0; i < _polygon.Count; i++)
            {
                Handles.DrawSolidDisc(_polygon[i], Vector3.up, HandleUtility.GetHandleSize(_polygon[i]) * 0.025f);
                Handles.Label(_polygon[i], (i + 1).ToString());
            }
        }
        finally { Handles.matrix = oldMatrix; Handles.color = oldColor; Handles.zTest = oldDepth; }
    }

    private void ReleaseMouse()
    {
        if (_controlId != 0 && GUIUtility.hotControl == _controlId) GUIUtility.hotControl = 0;
    }

    private void ApplyCut()
    {
        string error = ValidateContext();
        if (error != null) { _message = error; return; }
        string assetPath = AssetDatabase.GetAssetPath(_source);
        if (AssetDatabase.GetAssetDependencyHash(assetPath).ToString() != _sourceHash)
        { _message = "Исходный asset переимпортирован. Нажмите «Найти» и повторите выбор."; return; }
        var serializedBoundary = new SerializedObject(_boundary);
        var oldRoot = serializedBoundary.FindProperty("_authoredBoundaryRoot").objectReferenceValue as GameObject;
        if (oldRoot != null && (oldRoot.scene != _wall.gameObject.scene
            || _wall.transform.IsChildOf(oldRoot.transform) || _boundary.transform.IsChildOf(oldRoot.transform)))
        { _message = "Старая граница содержит стену/контроллер или находится в другой сцене. Исправьте ссылку перед применением."; return; }
        MeshCollider[] colliders = _wall.GetComponents<MeshCollider>();
        if (colliders.Any(c => c.convex || (c.sharedMesh != _wall.sharedMesh && c.sharedMesh != _source)))
        { _message = "На стене нестандартный/convex MeshCollider. Сначала проверьте его вручную; автоматическая замена отменена."; return; }
        // A second full-wall collider would keep blocking the removed geometry.
        var extraColliders = _wall.gameObject.scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<MeshCollider>(true))
            .Where(c => c.gameObject != _wall.gameObject && c.enabled && c.gameObject.activeInHierarchy
                && (c.sharedMesh == _source || c.sharedMesh == _wall.sharedMesh)
                && (oldRoot == null || !c.transform.IsChildOf(oldRoot.transform))).ToArray();
        if (extraColliders.Length != 0)
        { _message = "Есть отдельный коллайдер полного меша: " + extraColliders[0].name + ". Проверьте его до вырезки."; return; }

        Mesh kept = null, cut = null;
        string folder = null;
        int undoGroup = -1;
        try
        {
            BossWallSelectionMath.Partition(_triangles, _selected, out int[] remaining, out int[] extracted);
            kept = Instantiate(_source); cut = Instantiate(_source);
            kept.name = "WallsWithoutManualBossBoundary"; cut.name = "ManualBossBoundaryO";
            kept.SetTriangles(remaining, 0, true); cut.SetTriangles(extracted, 0, true);
            EnsureFolder(OutputRoot);
            folder = AssetDatabase.GenerateUniqueAssetPath(OutputRoot + "/ManualCut_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(OutputRoot, System.IO.Path.GetFileName(folder))))
                throw new InvalidOperationException("Не удалось создать папку mesh assets.");
            AssetDatabase.CreateAsset(kept, folder + "/Walls.asset");
            AssetDatabase.CreateAsset(cut, folder + "/BossBoundary.asset");
            AssetDatabase.SaveAssetIfDirty(kept);
            AssetDatabase.SaveAssetIfDirty(cut);

            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            Undo.RecordObject(_wall, UndoName);
            Undo.RecordObject(_boundary, UndoName);
            var newRoot = new GameObject("ImportedBossBoundary_O_Manual");
            Undo.RegisterCreatedObjectUndo(newRoot, UndoName);
            Undo.SetTransformParent(newRoot.transform, _wall.transform, UndoName);
            Undo.RecordObject(newRoot.transform, UndoName);
            newRoot.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            newRoot.transform.localScale = Vector3.one;
            newRoot.layer = _wall.gameObject.layer;
            Undo.AddComponent<MeshFilter>(newRoot).sharedMesh = cut;
            var renderer = Undo.AddComponent<MeshRenderer>(newRoot);
            var sourceRenderer = _wall.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            renderer.receiveShadows = sourceRenderer.receiveShadows;
            renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
            renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
            renderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
            renderer.enabled = true;
            // No static navigation flags: the boss wall must disappear without a rebake.
            GameObjectUtility.SetStaticEditorFlags(newRoot, 0);
            foreach (MeshCollider collider in colliders)
            {
                var boundaryCollider = Undo.AddComponent<MeshCollider>(newRoot);
                boundaryCollider.enabled = collider.enabled;
                boundaryCollider.isTrigger = collider.isTrigger;
                boundaryCollider.sharedMaterial = collider.sharedMaterial;
                boundaryCollider.cookingOptions = collider.cookingOptions;
                boundaryCollider.includeLayers = collider.includeLayers;
                boundaryCollider.excludeLayers = collider.excludeLayers;
                boundaryCollider.layerOverridePriority = collider.layerOverridePriority;
                boundaryCollider.providesContacts = collider.providesContacts;
                boundaryCollider.sharedMesh = cut;
                Undo.RecordObject(collider, UndoName);
                collider.sharedMesh = kept;
                RecordChanged(collider);
            }
            if (oldRoot != null)
            {
                Undo.RecordObject(oldRoot, UndoName);
                oldRoot.SetActive(false);
                RecordChanged(oldRoot);
            }
            _wall.sharedMesh = kept;
            _boundary.SetAuthoredBoundary(_wall, _source, newRoot);
            RecordChanged(_wall);
            RecordChanged(_boundary);
            EditorSceneManager.MarkSceneDirty(_wall.gameObject.scene);
            Undo.CollapseUndoOperations(undoGroup);
            undoGroup = -1;
            _showPreview = false;
            _showWire = false;
            _message = $"Готово: {extracted.Length / 3} треугольников отделено. Меши: {folder}. "
                + "Проверьте стенку в Scene и сохраните Game.unity (Ctrl+S). Ctrl+Z отменяет изменения сцены.";
            Debug.Log(_message, newRoot);
            Selection.activeGameObject = newRoot;
        }
        catch (Exception exception)
        {
            if (undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
            _message = "Вырезка отменена: " + exception.Message
                + (folder == null ? "" : " Созданные копии, если есть, оставлены в " + folder);
            Debug.LogException(exception);
        }
        finally
        {
            if (kept != null && !EditorUtility.IsPersistent(kept)) DestroyImmediate(kept);
            if (cut != null && !EditorUtility.IsPersistent(cut)) DestroyImmediate(cut);
            Repaint();
            SceneView.RepaintAll();
        }
    }

    private static void RecordChanged(Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, path.Substring(slash + 1))))
            throw new InvalidOperationException("Не удалось создать " + path);
    }
}
#endif
