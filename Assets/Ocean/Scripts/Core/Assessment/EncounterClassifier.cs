// ============================================================
// EncounterClassifier.cs — Ситуация сближения по МППСС-72 (C2)
//
// Ситуация определяется в момент возникновения опасности столкновения
// (цель обнаружена, CPA < k·CPA_доп, 0 < TCPA ≤ T_гор) по двум курсовым углам:
//   q_о — курсовой угол цели со своего судна (от носа по часовой, 0…360°);
//   q_ц — курсовой угол своего судна с цели.
// Порядок проверки:
//   1. q_ц ∈ [112,5°; 247,5°] — своё судно более 22,5° позади траверза цели
//      и сближается с ней: своё судно ОБГОНЯЕТ (правило 13 б), уступает;
//   2. q_о ∈ [112,5°; 247,5°] — цель обгоняет своё судно (правило 13),
//      своё судно привилегированное (правило 17);
//   3. |q_о| ≤ α и |q_ц| ≤ α (углы — от −180° до 180°) — каждое судно видит
//      другое прямо или почти прямо по носу: ВСТРЕЧНЫЕ КУРСЫ (правило 14);
//      оба уступают, поворот вправо;
//   4. иначе ПЕРЕСЕЧЕНИЕ КУРСОВ (правило 15): цель справа (0 < q_о < 180°) —
//      своё судно уступает дорогу; цель слева — привилегированное (правило 17).
//      Цель точно по носу (q_о = 0) вне сектора встречных — по стороне,
//      с которой цель видит своё судно.
// 22,5° — норма (правило 13 б); α (по умолчанию 6°) — параметр: правило 14 б
// говорит «в створе или почти в створе» без числа (ThesisNotes, раздел 10).
// ============================================================
using System;

public enum EncounterSituation
{
    None,
    HeadOn,           // встречные курсы, правило 14
    CrossingGiveWay,  // пересечение, цель справа — уступить дорогу, правила 15, 16
    CrossingStandOn,  // пересечение, цель слева — сохранять курс и скорость, правило 17
    Overtaking,       // своё судно обгоняет, правило 13
    Overtaken         // цель обгоняет своё судно, правила 13, 17
}

public static class EncounterClassifier
{
    public const double AbaftBeamDeg = 112.5;   // 22,5° позади траверза (правило 13 б)

    /// <summary>Угол в диапазон (−180°; 180°].</summary>
    public static double Signed(double deg)
    {
        double a = Math.IEEERemainder(deg, 360.0);
        return a <= -180.0 ? a + 360.0 : a;
    }

    private static bool AbaftBeam(double q)
    {
        double a = q % 360.0;
        if (a < 0) a += 360.0;
        return a >= AbaftBeamDeg && a <= 360.0 - AbaftBeamDeg;
    }

    public static EncounterSituation Classify(double qOwnDeg, double qTargetDeg, double headOnSectorDeg)
    {
        if (AbaftBeam(qTargetDeg)) return EncounterSituation.Overtaking;
        if (AbaftBeam(qOwnDeg)) return EncounterSituation.Overtaken;
        double so = Signed(qOwnDeg), st = Signed(qTargetDeg);
        if (Math.Abs(so) <= headOnSectorDeg && Math.Abs(st) <= headOnSectorDeg) return EncounterSituation.HeadOn;
        if (so > 0) return EncounterSituation.CrossingGiveWay;
        if (so < 0) return EncounterSituation.CrossingStandOn;
        // Цель точно по носу, а своё судно видно с цели под углом: если цель видит нас справа,
        // мы у неё справа — уступает она, мы привилегированные
        return st > 0 ? EncounterSituation.CrossingStandOn : EncounterSituation.CrossingGiveWay;
    }

    /// <summary>Своё судно уступает дорогу (иначе — привилегированное).</summary>
    public static bool GiveWay(EncounterSituation s) =>
        s == EncounterSituation.HeadOn || s == EncounterSituation.CrossingGiveWay || s == EncounterSituation.Overtaking;

    public static string Title(EncounterSituation s)
    {
        switch (s)
        {
            case EncounterSituation.HeadOn: return "встречные курсы";
            case EncounterSituation.CrossingGiveWay: return "пересечение курсов, цель справа";
            case EncounterSituation.CrossingStandOn: return "пересечение курсов, цель слева";
            case EncounterSituation.Overtaking: return "обгон: своё судно обгоняет";
            case EncounterSituation.Overtaken: return "обгон: цель обгоняет своё судно";
            default: return "нет опасности";
        }
    }

    public static string Duty(EncounterSituation s)
    {
        switch (s)
        {
            case EncounterSituation.HeadOn: return "отворот вправо (правило 14)";
            case EncounterSituation.CrossingGiveWay: return "уступить дорогу, не пересекать курс по носу (правила 15, 16)";
            case EncounterSituation.CrossingStandOn: return "сохранять курс и скорость (правило 17)";
            case EncounterSituation.Overtaking: return "держаться в стороне от обгоняемого (правило 13)";
            case EncounterSituation.Overtaken: return "сохранять курс и скорость (правила 13, 17)";
            default: return "";
        }
    }
}
