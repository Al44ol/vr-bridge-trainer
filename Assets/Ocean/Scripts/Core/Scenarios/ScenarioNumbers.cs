// ============================================================
// ScenarioNumbers.cs — Порядковые номера встроенных сценариев (22.10.2026, решение пользователя)
//
// Раньше идентификатор встроенного сценария был номером запроса плана (B2-01, 19-03…), теперь —
// порядковый номер упражнения в учебном порядке, от простого к сложному: 01 … 07 (константы Id в Demo*.cs).
// Renamed — соответствие «было → стало»:
//   • ассеты библиотеки перенумеровываются сами при входе в Play Mode (ScenarioLibrarySync);
//   • прежние файлы «Образцы\B2-01 ….json» удаляются при записи образцов (ScenarioFolder.ExportSamples);
//   • старые записи (session.json с прежним id) по Current находят свой встроенный сценарий.
// Свои сценарии («МОЙ-NN») не меняются.
// ============================================================
using System;

public static class ScenarioNumbers
{
    /// <summary>Прежний id → нынешний, в учебном порядке.</summary>
    public static readonly (string Old, string Now)[] Renamed =
    {
        ("B2-01", DemoScenario.Id),          // 01 Проход фарватером по путевым точкам
        ("B3-01", DemoTrafficScenario.Id),   // 02 Расхождение с судами на подходе и в фарватере
        ("C4-01", DemoFogScenario.Id),       // 03 Плавание в тумане
        ("21-01", DemoTriggerScenario.Id),   // 04 Фарватер с триггерами
        ("19-01", DemoBerthScenario.Id),     // 05 Швартовка к причалу лагом
        ("19-02", DemoTugBerthScenario.Id),  // 06 Швартовка с буксиром при отжимном ветре
        ("19-03", DemoTugWorkScenario.Id),   // 07 Буксир прижимает судно к причалу
    };

    /// <summary>Нынешний id встроенного сценария по прежнему (или тот же, если не переименовывался).
    /// Вариант на катере «B2-01-FB» → «01-FB».</summary>
    public static string Current(string id)
    {
        if (string.IsNullOrEmpty(id)) return id ?? "";
        foreach ((string old, string now) in Renamed)
        {
            if (string.Equals(id, old, StringComparison.OrdinalIgnoreCase)) return now;
            if (id.StartsWith(old + "-", StringComparison.OrdinalIgnoreCase)) return now + id.Substring(old.Length);
        }
        return id;
    }

    /// <summary>Прежний id, если это файл образца со старым именем («B2-01 Проход….json»); иначе null.</summary>
    public static string LegacyPrefix(string fileName)
    {
        foreach ((string old, string _) in Renamed)
            if (fileName != null && fileName.StartsWith(old + " ", StringComparison.OrdinalIgnoreCase)) return old;
        return null;
    }
}
