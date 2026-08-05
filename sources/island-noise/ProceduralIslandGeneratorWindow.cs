using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 사이즈/시드/블록 카탈로그로 단층 프로시저럴 섬을 생성하는 에디터 창.
/// </summary>
public class ProceduralIslandGeneratorWindow : EditorWindow
{
    const string PrefsCatalogGuid = "ProceduralIslandGenerator_CatalogGuid";
    const string PrefsFoldShape = "ProceduralIslandGenerator_FoldShape";
    const string PrefsFoldHill = "ProceduralIslandGenerator_FoldHill";
    const string PrefsFoldCliff = "ProceduralIslandGenerator_FoldCliff";
    const string PrefsFoldBeach = "ProceduralIslandGenerator_FoldBeach";
    const string PrefsFoldHeightRules = "ProceduralIslandGenerator_FoldHeightRules";

    // —— 공통 ——
    static readonly GUIContent LabelCatalog = new GUIContent(
        "블록 카탈로그",
        "이동 가능 / 못 올라가는 언덕 / 해변에 쓸 FloorBlock top 머티리얼 리스트 SO.");

    static readonly GUIContent LabelTarget = new GUIContent(
        "배치 부모 (Floor)",
        "블록이 붙을 Transform. 비우면 ProceduralIsland_Preview를 만들거나 재사용.");

    static readonly GUIContent LabelDiameter = new GUIContent(
        "대략 지름",
        "목표 섬 지름(월드 단위).\n← 작음 / → 큼.");

    static readonly GUIContent LabelBlockSize = new GUIContent(
        "블록 크기",
        "한 칸 가로·세로 크기. 프로젝트 기본 2.\n← 촘촘 / → 성김.");

    static readonly GUIContent LabelSeed = new GUIContent(
        "시드",
        "같은 시드+파라미터면 같은 섬. 바꾸면 다른 모양.");

    static readonly GUIContent LabelClear = new GUIContent(
        "생성 전 자식 비우기",
        "생성 전 대상 자식 블록을 지울지. 켜면 덮어쓰기, 끄면 기존 위에 추가.");

    // —— 윤곽 ——
    static readonly GUIContent LabelShapeFreq = new GUIContent(
        "윤곽 밀도",
        "섬 실루엣 노이즈 밀도.\n← 크고 둥근 덩어리 / → 자잘한 들쭉날쭉.");

    static readonly GUIContent LabelLandThreshold = new GUIContent(
        "육지 기준",
        "육지로 인정하는 마스크 기준.\n← 섬이 커지고 메움 / → 섬이 작아지고 구멍·해안 많음.");

    // —— 언덕 ——
    static readonly GUIContent LabelHeightFreq = new GUIContent(
        "언덕 밀도",
        "언덕 위치 노이즈 밀도.\n← 넓고 완만한 구릉 / → 작고 잦은 언덕.");

    static readonly GUIContent LabelHillStart = new GUIContent(
        "언덕 시작점",
        "이 값보다 낮은 높이 노이즈는 평지(Y=0).\n← 언덕 많음 / → 평지 위주, 언덕 드묾.");

    static readonly GUIContent LabelHillPower = new GUIContent(
        "언덕 뾰족함",
        "언덕 높이 곡선. 클수록 봉우리만 뚜렷.\n← 완만한 구릉 / → 평지+높은 봉우리.");

    static readonly GUIContent LabelMaxLandY = new GUIContent(
        "최대 높이",
        "육지·절벽 언덕의 최대 Y. 넘으면 깎이거나 절벽이 평지로 강등.\n← 낮은 천장 / → 높은 언덕 허용.");

    // —— 절벽 언덕 ——
    static readonly GUIContent LabelCliffFreq = new GUIContent(
        "절벽 밀도",
        "못 올라가는 언덕 영역 노이즈 밀도.\n← 큰 절벽 덩어리 / → 잘게 쪼개진 절벽.");

    static readonly GUIContent LabelCliffThreshold = new GUIContent(
        "절벽 기준",
        "못 올라가는 언덕으로 뽑는 기준.\n← 절벽 언덕 많음 / → 거의 없음.");

    // —— 해변 ——
    static readonly GUIContent LabelBeachFreq = new GUIContent(
        "해변 밀도",
        "해변(모래) vs 절벽(공백) 해안 노이즈 밀도.\n← 긴 해변 구간 / → 해변·절벽이 잘게 교차.");

    static readonly GUIContent LabelBeachThreshold = new GUIContent(
        "해변 기준",
        "해안 빈칸을 해변 블록으로 채울 확률.\n← 해변 많음 / → 해변 적고 절벽(공백) 많음.");

    // —— 고정 규칙 ——
    static readonly GUIContent LabelBaseLandY = new GUIContent(
        "기준 평지 Y",
        "기준 평지 높이. 고정 0. 해변(-1)과 약 1 차이.");

    static readonly GUIContent LabelYStep = new GUIContent(
        "등반 계단",
        "등반 가능 높이 계단. 고정 0.2.");

    static readonly GUIContent LabelSandY = new GUIContent(
        "해변 Y",
        "해변 블록 높이. 고정 -1.0.");

    static readonly GUIContent LabelMaxClimb = new GUIContent(
        "최대 등반 Δ",
        "이동 가능 땅 이웃끼리 허용 ΔY. 고정 0.2.");

    static readonly GUIContent LabelCliffMin = new GUIContent(
        "절벽 최소 Δ",
        "못 올라가는 언덕이 인접 평지보다 최소 이만큼 높음. 고정 0.4.");

    static readonly GUIContent LabelLiveMode = new GUIContent(
        "Live Mode",
        "켜면 노이즈 파라미터 변경 시 같은 시드로 즉시 재생성합니다. 창 배경이 녹색으로 바뀝니다.");

    static readonly GUIContent LabelUndoParams = new GUIContent(
        "Undo",
        "노이즈 파라미터를 움직이기 전 값으로 되돌립니다. (Ctrl+Z)");

    static readonly Color LiveModeBgColor = new Color(0.12f, 0.28f, 0.16f, 1f);
    static readonly Color LiveModeBannerColor = new Color(0.2f, 0.55f, 0.32f, 1f);
    const int MaxParamUndoStack = 32;

    ProceduralIslandGenerationSettings _settings = ProceduralIslandGenerationSettings.Default;
    ProceduralIslandBlockCatalog _catalog;
    Transform _targetParent;
    Vector2 _scroll;
    string _lastSummary = string.Empty;
    bool _liveMode;
    readonly List<ProceduralIslandGenerationSettings> _paramUndoStack = new List<ProceduralIslandGenerationSettings>();
    bool _paramEditSessionActive;

    bool _foldShape = true;
    bool _foldHill = true;
    bool _foldCliff;
    bool _foldBeach;
    bool _foldHeightRules;

    [MenuItem("Tools/Island/Procedural Generator")]
    public static void ShowWindow()
    {
        var window = GetWindow<ProceduralIslandGeneratorWindow>("Island Proc Gen");
        window.titleContent = new GUIContent("Island Proc Gen", EditorGUIUtility.IconContent("Terrain Icon").image);
        window.minSize = new Vector2(360f, 480f);
    }

    /// <summary>
    /// Agent/자동화용 빠른 생성.
    /// </summary>
    public static void GenerateQuick(float approximateDiameterWorld, int seed, ProceduralIslandBlockCatalog catalog)
    {
        ProceduralIslandGenerator.Generate(approximateDiameterWorld, seed, catalog);
    }

    void OnEnable()
    {
        LoadCatalogPrefs();
        _foldShape = EditorPrefs.GetBool(PrefsFoldShape, true);
        _foldHill = EditorPrefs.GetBool(PrefsFoldHill, true);
        _foldCliff = EditorPrefs.GetBool(PrefsFoldCliff, false);
        _foldBeach = EditorPrefs.GetBool(PrefsFoldBeach, false);
        _foldHeightRules = EditorPrefs.GetBool(PrefsFoldHeightRules, false);
    }

    void OnDisable()
    {
        EditorPrefs.SetBool(PrefsFoldShape, _foldShape);
        EditorPrefs.SetBool(PrefsFoldHill, _foldHill);
        EditorPrefs.SetBool(PrefsFoldCliff, _foldCliff);
        EditorPrefs.SetBool(PrefsFoldBeach, _foldBeach);
        EditorPrefs.SetBool(PrefsFoldHeightRules, _foldHeightRules);
        EditorApplication.delayCall -= RunLiveGenerateDeferred;
    }

    void OnGUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("플레이 모드에서는 사용할 수 없습니다.", MessageType.Warning);
            return;
        }

        HandleParamUndoHotkey();
        EndParamEditSessionIfNeeded();

        if (_liveMode)
            EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), LiveModeBgColor);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawLiveModeBanner();
        DrawCatalogSection();
        DrawTargetSection();
        DrawSizeSection();
        DrawNoiseSection();
        DrawHeightRulesSection();
        DrawActions();

        EditorGUILayout.EndScrollView();
    }

    void DrawLiveModeBanner()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.ToggleLeft(LabelLiveMode, _liveMode);
            if (EditorGUI.EndChangeCheck())
            {
                _liveMode = next;
                if (_liveMode)
                    ScheduleLiveGenerate();
            }

            using (new EditorGUI.DisabledScope(_paramUndoStack.Count == 0))
            {
                var undoContent = _paramUndoStack.Count > 0
                    ? new GUIContent($"Undo ({_paramUndoStack.Count})", LabelUndoParams.tooltip)
                    : LabelUndoParams;
                if (GUILayout.Button(undoContent, GUILayout.Width(88f)))
                    UndoParameters();
            }
        }

        if (!_liveMode)
            return;

        var bannerRect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(bannerRect, LiveModeBannerColor);
        EditorGUI.LabelField(
            bannerRect,
            "  LIVE — 노이즈 변경 시 시드 유지 재생성 · Undo로 이전 파라미터 복원",
            EditorStyles.whiteBoldLabel);
    }

    void DrawCatalogSection()
    {
        EditorGUILayout.LabelField("블록 카탈로그", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _catalog = (ProceduralIslandBlockCatalog)EditorGUILayout.ObjectField(
            LabelCatalog,
            _catalog,
            typeof(ProceduralIslandBlockCatalog),
            false);
        if (EditorGUI.EndChangeCheck())
            SaveCatalogPrefs();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("카탈로그 생성", "새 Block Catalog SO를 프로젝트에 만듭니다.")))
                CreateCatalogAsset();
            if (GUILayout.Button(new GUIContent("Ping", "현재 카탈로그 에셋을 Project에서 강조합니다."), GUILayout.Width(60f)))
            {
                if (_catalog != null)
                    EditorGUIUtility.PingObject(_catalog);
            }
        }

        if (_catalog != null)
        {
            using (new EditorGUI.DisabledScope(true))
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    new GUIContent($"이동 {_catalog.WalkableTopMaterials.Count}", "등반 가능 평지·언덕 top 머티리얼 수"),
                    EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    new GUIContent($"절벽 {_catalog.UnclimbableHillTopMaterials.Count}", "Δ≥0.4 절벽 언덕 top 머티리얼 수"),
                    EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    new GUIContent($"해변 SandBlock", "해변은 콜라이더 없는 SandBlock 사용"),
                    EditorStyles.miniLabel);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("카탈로그를 지정해야 Generate할 수 있습니다.", MessageType.Warning);
        }
    }

    void DrawTargetSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("대상", EditorStyles.boldLabel);
        _targetParent = (Transform)EditorGUILayout.ObjectField(
            LabelTarget,
            _targetParent,
            typeof(Transform),
            true);
    }

    void DrawSizeSection()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("크기 / 시드", EditorStyles.boldLabel);
        _settings.ApproximateDiameterWorld = EditorGUILayout.FloatField(LabelDiameter, _settings.ApproximateDiameterWorld);
        _settings.BlockSize = EditorGUILayout.FloatField(LabelBlockSize, _settings.BlockSize);
        using (new EditorGUILayout.HorizontalScope())
        {
            _settings.Seed = EditorGUILayout.IntField(LabelSeed, _settings.Seed);
            if (GUILayout.Button(new GUIContent("랜덤", "시드를 무작위로 바꿉니다."), GUILayout.Width(56f)))
                _settings.Seed = Random.Range(1, 999999);
        }

        _settings.ClearTargetChildren = EditorGUILayout.Toggle(LabelClear, _settings.ClearTargetChildren);
    }

    void DrawNoiseSection()
    {
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("노이즈", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "라벨에 마우스를 올리면 ←/→ 효과가 표시됩니다.",
            EditorStyles.miniLabel);

        var beforeEdit = _settings;
        var noiseChanged = false;

        // 윤곽
        _foldShape = EditorGUILayout.BeginFoldoutHeaderGroup(_foldShape, "① 윤곽 (섬 모양)");
        if (_foldShape)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("실루엣·면적", EditorStyles.miniBoldLabel);
                EditorGUI.BeginChangeCheck();
                _settings.ShapeFrequency = EditorGUILayout.Slider(LabelShapeFreq, _settings.ShapeFrequency, 0.01f, 0.3f);
                _settings.LandThreshold = EditorGUILayout.Slider(LabelLandThreshold, _settings.LandThreshold, 0.05f, 0.9f);
                noiseChanged |= EditorGUI.EndChangeCheck();
            }
        }

        EditorGUILayout.EndFoldoutHeaderGroup();

        // 언덕
        _foldHill = EditorGUILayout.BeginFoldoutHeaderGroup(_foldHill, "② 언덕 (등반 가능)");
        if (_foldHill)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("평지 Y=0 위 0.2 계단", EditorStyles.miniBoldLabel);
                EditorGUI.BeginChangeCheck();
                _settings.HeightFrequency = EditorGUILayout.Slider(LabelHeightFreq, _settings.HeightFrequency, 0.01f, 0.4f);
                _settings.HillStartThreshold = EditorGUILayout.Slider(LabelHillStart, _settings.HillStartThreshold, 0.3f, 0.9f);
                _settings.HillPower = EditorGUILayout.Slider(LabelHillPower, _settings.HillPower, 0.5f, 3f);
                _settings.MaxLandY = EditorGUILayout.FloatField(LabelMaxLandY, _settings.MaxLandY);
                noiseChanged |= EditorGUI.EndChangeCheck();
            }
        }

        EditorGUILayout.EndFoldoutHeaderGroup();

        // 절벽
        _foldCliff = EditorGUILayout.BeginFoldoutHeaderGroup(_foldCliff, "③ 절벽 언덕 (못 올라감)");
        if (_foldCliff)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("인접 평지 대비 Δ≥0.4", EditorStyles.miniBoldLabel);
                EditorGUI.BeginChangeCheck();
                _settings.CliffFrequency = EditorGUILayout.Slider(LabelCliffFreq, _settings.CliffFrequency, 0.01f, 0.4f);
                _settings.CliffThreshold = EditorGUILayout.Slider(LabelCliffThreshold, _settings.CliffThreshold, 0.4f, 0.95f);
                noiseChanged |= EditorGUI.EndChangeCheck();
            }
        }

        EditorGUILayout.EndFoldoutHeaderGroup();

        // 해변
        _foldBeach = EditorGUILayout.BeginFoldoutHeaderGroup(_foldBeach, "④ 해변");
        if (_foldBeach)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("해안 모래(Y=-1) vs 절벽 공백", EditorStyles.miniBoldLabel);
                EditorGUI.BeginChangeCheck();
                _settings.BeachFrequency = EditorGUILayout.Slider(LabelBeachFreq, _settings.BeachFrequency, 0.01f, 0.5f);
                _settings.BeachThreshold = EditorGUILayout.Slider(LabelBeachThreshold, _settings.BeachThreshold, 0.05f, 0.95f);
                noiseChanged |= EditorGUI.EndChangeCheck();
            }
        }

        EditorGUILayout.EndFoldoutHeaderGroup();

        if (!noiseChanged)
            return;

        PushParamUndoIfNewSession(beforeEdit);
        if (_liveMode)
            ScheduleLiveGenerate();
    }

    void DrawHeightRulesSection()
    {
        EditorGUILayout.Space(6f);
        _foldHeightRules = EditorGUILayout.BeginFoldoutHeaderGroup(_foldHeightRules, "고정 높이 규칙 (읽기 전용)");
        if (_foldHeightRules)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField(LabelBaseLandY, _settings.BaseLandY);
                EditorGUILayout.FloatField(LabelYStep, _settings.YStep);
                EditorGUILayout.FloatField(LabelSandY, _settings.SandY);
                EditorGUILayout.FloatField(LabelMaxClimb, _settings.MaxClimbStep);
                EditorGUILayout.FloatField(LabelCliffMin, _settings.CliffMinDelta);
            }
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    void DrawActions()
    {
        EditorGUILayout.Space(12f);
        using (new EditorGUILayout.HorizontalScope())
        {
            var generate = GUILayout.Button(
                new GUIContent("Generate", "현재 파라미터로 섬 블록을 생성합니다."),
                GUILayout.Height(32f));
            var generateRandom = GUILayout.Button(
                new GUIContent("랜덤 생성", "시드를 무작위로 바꾼 뒤 바로 생성합니다."),
                GUILayout.Height(32f),
                GUILayout.Width(88f));
            if (GUILayout.Button(
                    new GUIContent("기본값", "노이즈·크기 기본값으로 되돌립니다. 카탈로그/대상은 유지."),
                    GUILayout.Height(32f),
                    GUILayout.Width(72f)))
            {
                PushParamUndoForced(_settings);
                var keepParent = _targetParent;
                var keepCatalog = _catalog;
                _settings = ProceduralIslandGenerationSettings.Default;
                _targetParent = keepParent;
                _catalog = keepCatalog;
                if (_liveMode)
                    ScheduleLiveGenerate();
            }

            if (generateRandom)
            {
                _settings.Seed = Random.Range(1, 999999);
                RunGenerate();
            }
            else if (generate)
            {
                RunGenerate();
            }
        }

        if (!string.IsNullOrEmpty(_lastSummary))
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(_lastSummary, MessageType.None);
        }
    }

    void HandleParamUndoHotkey()
    {
        var e = Event.current;
        if (e.type != EventType.KeyDown)
            return;
        if (e.keyCode != KeyCode.Z)
            return;
        if (!(e.control || e.command) || e.shift || e.alt)
            return;
        if (_paramUndoStack.Count == 0)
            return;

        UndoParameters();
        e.Use();
        GUI.FocusControl(null);
        Repaint();
    }

    void EndParamEditSessionIfNeeded()
    {
        if (!_paramEditSessionActive)
            return;

        var e = Event.current;
        if (e.type == EventType.MouseDown
            || e.type == EventType.MouseUp
            || e.rawType == EventType.MouseUp
            || (e.type == EventType.KeyUp
                && (e.keyCode == KeyCode.Return
                    || e.keyCode == KeyCode.KeypadEnter
                    || e.keyCode == KeyCode.Tab
                    || e.keyCode == KeyCode.Escape)))
        {
            _paramEditSessionActive = false;
        }
    }

    void PushParamUndoIfNewSession(ProceduralIslandGenerationSettings beforeEdit)
    {
        if (_paramEditSessionActive)
            return;

        PushParamUndoForced(beforeEdit);
        _paramEditSessionActive = true;
    }

    void PushParamUndoForced(ProceduralIslandGenerationSettings snapshot)
    {
        _paramUndoStack.Add(snapshot);
        while (_paramUndoStack.Count > MaxParamUndoStack)
            _paramUndoStack.RemoveAt(0);
    }

    void UndoParameters()
    {
        if (_paramUndoStack.Count == 0)
            return;

        var index = _paramUndoStack.Count - 1;
        _settings = _paramUndoStack[index];
        _paramUndoStack.RemoveAt(index);
        _paramEditSessionActive = false;
        EditorApplication.delayCall -= RunLiveGenerateDeferred;

        if (_liveMode)
            ScheduleLiveGenerate();
        else
            _lastSummary = $"파라미터 Undo → seed={_settings.Seed}";

        Repaint();
    }

    void RunGenerate()
    {
        var keepClear = _settings.ClearTargetChildren;
        var keepRecordUndo = _settings.RecordUndo;
        if (_liveMode)
        {
            _settings.ClearTargetChildren = true;
            _settings.RecordUndo = false;
        }
        else
        {
            _settings.RecordUndo = true;
        }

        _settings.BlockCatalog = _catalog;
        var result = ProceduralIslandGenerator.Generate(_targetParent, _settings);
        _settings.ClearTargetChildren = keepClear;
        _settings.RecordUndo = keepRecordUndo;

        if (result.Parent == null)
        {
            _lastSummary = "생성 실패. Console을 확인하세요.";
            return;
        }

        _targetParent = result.Parent;
        _lastSummary =
            $"walkable={result.WalkableCount} cliffHill={result.UnclimbableHillCount} beach={result.BeachCount}\n" +
            $"landY=[{result.MinLandY:0.##}..{result.MaxLandY:0.##}] radiusBlocks={result.RadiusBlocks} seed={_settings.Seed}" +
            (_liveMode ? "  [LIVE]" : string.Empty);
    }

    void ScheduleLiveGenerate()
    {
        if (!_liveMode)
            return;

        EditorApplication.delayCall -= RunLiveGenerateDeferred;
        EditorApplication.delayCall += RunLiveGenerateDeferred;
    }

    void RunLiveGenerateDeferred()
    {
        if (this == null || !_liveMode)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        RunGenerate();
        Repaint();
    }

    void CreateCatalogAsset()
    {
        var path = EditorUtility.SaveFilePanelInProject(
            "Create Procedural Island Block Catalog",
            "ProceduralIslandBlockCatalog",
            "asset",
            "카탈로그 저장 위치 선택");
        if (string.IsNullOrEmpty(path))
            return;

        var asset = CreateInstance<ProceduralIslandBlockCatalog>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        _catalog = asset;
        SaveCatalogPrefs();
        EditorGUIUtility.PingObject(asset);
        Selection.activeObject = asset;
    }

    void LoadCatalogPrefs()
    {
        var guid = EditorPrefs.GetString(PrefsCatalogGuid, string.Empty);
        if (string.IsNullOrEmpty(guid))
            return;
        var path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path))
            return;
        _catalog = AssetDatabase.LoadAssetAtPath<ProceduralIslandBlockCatalog>(path);
    }

    void SaveCatalogPrefs()
    {
        if (_catalog == null)
        {
            EditorPrefs.DeleteKey(PrefsCatalogGuid);
            return;
        }

        var path = AssetDatabase.GetAssetPath(_catalog);
        var guid = AssetDatabase.AssetPathToGUID(path);
        EditorPrefs.SetString(PrefsCatalogGuid, guid);
    }
}
