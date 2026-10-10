// ============================================================
// WorldPosition.cs — Позиция в мировой системе координат тренажёра
//
// С B1 перенесена из WorldOrigin.cs в сборку VRBridge.Core: её используют
// и сцена (плавающее начало координат), и чистые классы акватории
// (WaterArea, GroundingDetector) и тесты.
// +East — восток, +North — север, метры, double.
// ============================================================

/// <summary>
/// Позиция в мировой системе координат тренажёра (метры, double).
/// </summary>
[System.Serializable]
public struct WorldPosition
{
    public double East;
    public double North;

    public WorldPosition(double east, double north)
    {
        East = east;
        North = north;
    }

    public static WorldPosition operator +(WorldPosition a, WorldPosition b) => new WorldPosition(a.East + b.East, a.North + b.North);
    public static WorldPosition operator -(WorldPosition a, WorldPosition b) => new WorldPosition(a.East - b.East, a.North - b.North);
    public static WorldPosition operator *(WorldPosition a, double k) => new WorldPosition(a.East * k, a.North * k);

    /// <summary>Длина вектора, м.</summary>
    public double Magnitude => System.Math.Sqrt(East * East + North * North);

    public override string ToString() => $"E {East:F1} м, N {North:F1} м";
}
