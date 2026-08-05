using UnityEngine;

/// <summary>
/// 에디터 프로시저럴 섬 생성 튜닝값.
/// 육지는 단층(수직 스택 없음). Y는 YStep(0.2) 단위.
/// 이동 가능 이웃 Δ ≤ MaxClimbStep, 못 올라가는 언덕은 CliffMinDelta 이상.
/// </summary>
public struct ProceduralIslandGenerationSettings
{
    public float ApproximateDiameterWorld;
    public float BlockSize;
    public int Seed;

    public float ShapeFrequency;
    public float HeightFrequency;
    public float CliffFrequency;
    public float BeachFrequency;

    public float LandThreshold;
    /// <summary>이 값 미만의 높이 노이즈는 기준 평지(BaseLandY). 클수록 평지 비중↑.</summary>
    public float HillStartThreshold;
    /// <summary>언덕 곡선 지수(>1이면 높은 봉우리만 남김).</summary>
    public float HillPower;
    public float CliffThreshold;
    public float BeachThreshold;

    public float BaseLandY;
    public float YStep;
    public float MaxLandY;
    public float SandY;
    public float MaxClimbStep;
    public float CliffMinDelta;

    public ProceduralIslandBlockCatalog BlockCatalog;
    public bool ClearTargetChildren;
    /// <summary>false면 Live Mode 등에서 Undo 기록 없이 빠르게 재생성.</summary>
    public bool RecordUndo;

    public static ProceduralIslandGenerationSettings Default => new ProceduralIslandGenerationSettings
    {
        ApproximateDiameterWorld = 40f,
        BlockSize = 2f,
        Seed = 1,
        ShapeFrequency = 0.07f,
        HeightFrequency = 0.09f,
        CliffFrequency = 0.11f,
        BeachFrequency = 0.14f,
        LandThreshold = 0.18f,
        HillStartThreshold = 0.58f,
        HillPower = 1.6f,
        CliffThreshold = 0.72f,
        BeachThreshold = 0.45f,
        BaseLandY = 0f,
        YStep = 0.2f,
        MaxLandY = 1.6f,
        SandY = -1f,
        MaxClimbStep = 0.2f,
        CliffMinDelta = 0.4f,
        BlockCatalog = null,
        ClearTargetChildren = true,
        RecordUndo = true,
    };
}
