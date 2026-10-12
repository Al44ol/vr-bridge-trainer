// ============================================================
// DebriefTests.cs — Разбор упражнения и отчёт (C3)
//
// По записанным прогонам B2-01 и B3-01 (ExerciseSimulation, как в C2):
// report.html самодостаточен и пригоден для печати; схема траектории
// совпадает с оценкой (наименьшая дистанция, начало манёвра); отчёт по
// оценке в шлеме и по записи одинаков; сводная таблица по нескольким
// сессиям (разные сценарии, сессия без снимка порогов); тексты нарушений;
// страницы таблицы на панели; геометрия схемы (отсечение, триангуляция).
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

public class DebriefTests
{
    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "VRBridgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Delete(string dir)
    {
        try { Directory.Delete(dir, true); } catch (Exception) { }
    }

    private static int Count(string s, string what) => Regex.Matches(s, Regex.Escape(what)).Count;

    // ─────────── report.html ───────────
    [Test]
    public void Report_SelfContained_PrintableA4_AllCriteria()
    {
        string dir = NewDir();
        try
        {
            ExerciseSimulation sim = AssessmentTests.B3Run("port", dir);
            var build = new DebriefBuild(dir, sim.LiveResult);
            build.Run();
            Assert.AreEqual("", build.Error);
            Assert.IsTrue(File.Exists(Path.Combine(dir, SessionFormat.ReportFile)));
            string html = File.ReadAllText(build.ReportPath);
            TestContext.WriteLine($"report.html: {html.Length / 1024} КБ, построение {build.ElapsedMs:F0} мс");

            // Без внешних ресурсов: ни скриптов, ни подключаемых файлов, ни ссылок в сеть
            StringAssert.StartsWith("<!DOCTYPE html>", html.TrimStart('﻿'));
            StringAssert.Contains("<meta charset=\"utf-8\">", html);
            Assert.AreEqual(0, Count(html, "<script"), "скрипты");
            Assert.AreEqual(0, Count(html, "<link"), "внешние стили");
            Assert.AreEqual(0, Count(html, " src="), "внешние файлы");
            Assert.AreEqual(0, Count(html, "https://"), "ссылки в сеть");
            Assert.AreEqual(Count(html, "http://"), Count(html, "xmlns=\"http://www.w3.org/2000/svg\""), "http — только пространство имён SVG");
            Assert.AreEqual(Count(html, "url("), Count(html, "url(#"), "url — только ссылки на clipPath внутри страницы");

            // Печать A4
            StringAssert.Contains("size: A4", html);
            StringAssert.Contains("thead { display: table-header-group; }", html);
            StringAssert.Contains("break-inside: avoid", html);

            // Разметка закрыта
            Assert.AreEqual(Count(html, "<svg"), Count(html, "</svg>"));
            Assert.AreEqual(Count(html, "<table"), Count(html, "</table>"));
            Assert.AreEqual(Count(html, "<figure>"), Count(html, "</figure>"));
            StringAssert.EndsWith("</html>\n", html);
            // clipPath — уникальные id (несколько схем и графиков на странице)
            var ids = new HashSet<string>();
            foreach (Match m in Regex.Matches(html, "<clipPath id=\"([^\"]+)\"")) Assert.IsTrue(ids.Add(m.Groups[1].Value), $"повтор id {m.Groups[1].Value}");

            // Все критерии в таблице, нарушения с правилами
            Assert.AreEqual(sim.LiveResult.Items.Count, Count(html, "<td class=\"mark\">"));
            StringAssert.Contains("Пересечение курса по носу судна, находящегося справа — T2, правило 15", html);
            StringAssert.Contains("совпадает с оценкой в шлеме", html);
            Assert.AreEqual(build.Map.Views.Count + 4, Count(html, "<figure>"), "рисунки: схемы по видам + 4 графика");
        }
        finally { Delete(dir); }
    }

    [Test]
    public void Report_LiveAndOfflineAssessment_SameText()
    {
        string dir = NewDir();
        try
        {
            ExerciseSimulation sim = AssessmentTests.B3Run("correct", dir);
            RecordedSession s = SessionReader.Load(dir);
            AssessmentResult offline = SessionAssessment.Assess(s);
            var opt = new ReportOptions { AssessmentSource = "проверка", Generated = new DateTime(2026, 10, 11, 12, 0, 0) };
            string a = SessionReport.Build(s, sim.LiveResult, DebriefMap.Build(s, sim.LiveResult), opt);
            string b = SessionReport.Build(s, offline, DebriefMap.Build(s, offline), opt);
            Assert.AreEqual(a, b, "отчёт по оценке в шлеме и по записи");
        }
        finally { Delete(dir); }
    }

    // ─────────── Схема ───────────
    [Test]
    public void Map_MinDistanceAndManoeuvre_MatchAssessment()
    {
        string dir = NewDir();
        try
        {
            ExerciseSimulation sim = AssessmentTests.B3Run("correct", dir);
            AssessmentResult r = sim.LiveResult;
            RecordedSession s = SessionReader.Load(dir);
            DebriefMapData map = DebriefMap.Build(s, r);

            // Акватория — из снимка в записи
            Assert.IsTrue(map.HasArea, "акватория в записи");
            Assert.IsTrue(map.HasRoute, "маршрут");
            SessionArea area = s.Meta.scenario.area;
            Assert.Greater(area.aids.Count, 0);
            Assert.AreEqual(area.shoals.Count + area.land.Count, map.Polygons.Count);
            Assert.AreEqual(area.aids.Count, map.Markers.FindAll(m => m.Layer == MapLayer.Aid).Count);
            Assert.AreEqual(3, map.TargetIds.Count, "T1, T2, T3");

            // Наименьшая дистанция: отрезок между судами в момент MinTime = дистанция оценки
            var links = map.Lines.FindAll(l => l.Layer == MapLayer.MinLink);
            int complete = 0;
            foreach (EncounterSummary e in r.Encounters)
            {
                if (!e.Complete || double.IsNaN(e.MinTime)) continue;
                complete++;
                MapLine link = links.Find(l => l.Label == $"{ScenarioText.Num(e.MinDistanceM)} м" && map.TargetIds[l.Series] == e.TargetKey);
                Assert.IsNotNull(link, $"{e.TargetId}: нет отрезка наименьшей дистанции");
                double de = link.Points[1].East - link.Points[0].East, dn = link.Points[1].North - link.Points[0].North;
                Assert.AreEqual(e.MinDistanceM, Math.Sqrt(de * de + dn * dn), 1.0, $"{e.TargetId}: длина отрезка");
                Assert.AreEqual(e.MinDistanceM >= e.RequiredCpaM, link.Ok);
            }
            Assert.AreEqual(complete, links.Count);
            Assert.AreEqual(1 + complete, map.Views.Count, "весь маршрут + окно на каждое завершённое сближение");

            // Окно сближения содержит оба судна в момент наименьшей дистанции
            foreach (MapLine l in links)
            {
                DebriefMapView v = map.Views.Find(x => x.Key == l.Encounter);
                Assert.IsTrue(v != null && v.Bounds.Contains(l.Points[0]), $"окно {l.Encounter} содержит своё судно");
                Assert.IsNotNull(v, $"окно для {map.TargetIds[l.Series]}");
                Assert.IsTrue(v.Bounds.Contains(l.Points[1]), $"окно {v.Key} содержит цель");
                Assert.GreaterOrEqual(v.Bounds.Width, DebriefMap.EncounterWindowMinM - 1e-6);
            }

            // Начало манёвра — в точке траектории в момент действия; одно действие — одна отметка
            var actions = new HashSet<double>();
            foreach (EncounterSummary e in r.Encounters) if (!double.IsNaN(e.ActionTime)) actions.Add(Math.Round(e.ActionTime, 3));
            var marks = map.Markers.FindAll(m => m.Layer == MapLayer.Manoeuvre);
            Assert.AreEqual(actions.Count, marks.Count);
            foreach (MapMarker m in marks)
            {
                TrackSample t = s.StateAt(m.Time);
                Assert.AreEqual(t.East, m.Position.East, 1e-9);
                Assert.AreEqual(t.North, m.Position.North, 1e-9);
            }

            // SVG вида: закрыт, свой clipPath
            string svg = DebriefSvg.Map(map, map.Views[1], "m1");
            StringAssert.StartsWith("<svg", svg);
            StringAssert.Contains("<clipPath id=\"m1\">", svg);
            StringAssert.EndsWith("</svg>", svg.TrimEnd());
        }
        finally { Delete(dir); }
    }

    [Test]
    public void Map_Collision_AccidentMarker()
    {
        string dir = NewDir();
        try
        {
            ExerciseSimulation sim = AssessmentTests.B3Run("none", dir);
            RecordedSession s = SessionReader.Load(dir);
            DebriefMapData map = DebriefMap.Build(s, sim.LiveResult);
            MapMarker acc = map.Markers.Find(m => m.Layer == MapLayer.Accident);
            Assert.IsNotNull(acc);
            Assert.AreEqual("столкновение", acc.Label);
            MapLine link = map.Lines.Find(l => l.Layer == MapLayer.MinLink && map.TargetIds[l.Series] == "T2");
            Assert.IsNotNull(link);
            Assert.IsFalse(link.Ok);
            Assert.AreEqual("столкновение", link.Label);
        }
        finally { Delete(dir); }
    }

    [Test]
    public void SessionArea_SnapshotSurvivesJson()
    {
        ScenarioDefinition sc = AssessmentTests.DemoB3();
        SessionMeta meta = SessionMeta.Create(sc, sc.shipParameters, "7", new DateTime(2026, 10, 11, 12, 0, 0));
        Assert.AreEqual(sc.waterArea.aids.Count, meta.scenario.area.aids.Count);
        Assert.AreEqual(sc.waterArea.shoals.Count, meta.scenario.area.shoals.Count);
        var back = new SessionMeta();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(meta, true), back);
        Assert.AreEqual(meta.scenario.area.aids.Count, back.scenario.area.aids.Count);
        Assert.AreEqual(sc.waterArea.aids[0].type, back.scenario.area.aids[0].Type);
        Assert.AreEqual(sc.waterArea.shoals[0].vertices.Count, back.scenario.area.shoals[0].points.Count);
        Assert.AreEqual(sc.waterArea.shoals[0].vertices[1].North, back.scenario.area.shoals[0].points[1].y_n_m, 1e-3);
    }

    // ─────────── Сводная таблица ───────────
    [Test]
    public void Summary_SeveralSessions_UnionColumnsAndFormat()
    {
        string root = NewDir();
        try
        {
            string a = Path.Combine(root, "a_B3"), b = Path.Combine(root, "b_B2"), c = Path.Combine(root, "c_B2_old");
            Directory.CreateDirectory(a); Directory.CreateDirectory(b); Directory.CreateDirectory(c);
            ExerciseSimulation simA = AssessmentTests.B3Run("correct", a);
            var simB = new ExerciseSimulation { Scenario = AssessmentTests.DemoB2(), RecordFolder = b };
            simB.Run();
            // «Старая» сессия: копия B2 без assessment.csv и без снимка порогов (как до C2)
            foreach (string f in Directory.GetFiles(b)) File.Copy(f, Path.Combine(c, Path.GetFileName(f)));
            File.Delete(Path.Combine(c, SessionFormat.AssessmentFile));
            RecordedSession old = SessionReader.Load(c);
            old.Meta.assessment_settings = new AssessmentSettings();
            old.Meta.assessment = new SessionAssessmentSummary();
            old.Meta.session_id += "_old";
            File.WriteAllText(Path.Combine(c, SessionFormat.MetaFile), JsonUtility.ToJson(old.Meta, true));

            List<string> folders = ExperimentSummary.FindSessions(root);
            Assert.AreEqual(3, folders.Count);
            ExperimentSummaryResult res = ExperimentSummary.Build(folders);
            Assert.AreEqual(3, res.Sessions);
            TestContext.WriteLine(string.Join("\n", res.Log));

            string[] wide = res.WideCsv.TrimEnd('\n').Split('\n');
            Assert.AreEqual(4, wide.Length, "заголовок + 3 сессии");
            List<string> head = CsvFormat.Split(wide[0]);
            foreach (string line in wide) Assert.AreEqual(head.Count, CsvFormat.Split(line).Count, "одинаковое число столбцов");
            // Объединение: критерии по целям (только B3) и маршрут (обе)
            CollectionAssert.Contains(head, "E1_T2-1_value");
            CollectionAssert.Contains(head, "E8_T3_passed");
            CollectionAssert.Contains(head, "R1_score");
            string turn = simB.LiveResult.Items.Find(x => x.Code == AssessmentCatalog.R6).Subject;
            CollectionAssert.Contains(head, ExperimentSummary.Column(AssessmentCatalog.R6, turn) + "_value");

            Dictionary<string, string> Row(int i)
            {
                var d = new Dictionary<string, string>();
                List<string> f = CsvFormat.Split(wide[i]);
                for (int k = 0; k < head.Count; k++) d[head[k]] = f[k];
                return d;
            }
            var rows = new List<Dictionary<string, string>> { Row(1), Row(2), Row(3) };
            Dictionary<string, string> rA = rows.Find(x => x["scenario_id"] == DemoTrafficScenario.Id);
            Dictionary<string, string> rB = rows.Find(x => x["scenario_id"] == DemoScenario.Id && !x["session_id"].EndsWith("_old"));
            Dictionary<string, string> rC = rows.Find(x => x["session_id"].EndsWith("_old"));
            // Точка — десятичный разделитель; значения — как в оценке
            Assert.AreEqual(Math.Round(simA.LiveResult.Score, 2), CsvFormat.ParseDouble(rA["score"]), 1e-9);
            StringAssert.Contains(".", rA["score"]);
            Assert.AreEqual("1", rA["passed"]);
            Assert.AreEqual("", rB["E1_T2-1_value"], "в B2-01 нет целей — пусто");
            Assert.AreEqual("1", rA["recorded_match"]);
            Assert.AreEqual("1", rB["recorded_match"]);
            Assert.AreEqual("", rC["recorded_match"], "нет assessment.csv");
            Assert.AreEqual("", rC["recorded_score"]);
            // Без снимка — пороги по умолчанию: тот же балл, что у B2 (пороги в B2 — по умолчанию)
            Assert.AreEqual(rB["score"], rC["score"]);

            // Длинная таблица: строка на сессию и критерий
            int items = simA.LiveResult.Items.Count + 2 * simB.LiveResult.Items.Count;
            Assert.AreEqual(1 + items, res.LongCsv.TrimEnd('\n').Split('\n').Length);

            // Файлы: UTF-8 с BOM
            string outDir = Path.Combine(root, "out");
            ExperimentSummary.Write(outDir, res);
            byte[] bytes = File.ReadAllBytes(Path.Combine(outDir, ExperimentSummary.WideFile));
            Assert.AreEqual(0xEF, bytes[0]); Assert.AreEqual(0xBB, bytes[1]); Assert.AreEqual(0xBF, bytes[2]);
        }
        finally { Delete(root); }
    }

    // ─────────── Тексты и страницы ───────────
    [Test]
    public void Violations_TextForEveryCriterion_AccidentFirst()
    {
        foreach (CriterionInfo c in AssessmentCatalog.All)
            Assert.IsTrue(DebriefText.HasFailureText(c.Code), $"нет текста нарушения для {c.Code}");

        var e3 = new CriterionResult
        {
            Info = AssessmentCatalog.Find(AssessmentCatalog.E3), Setting = AssessmentCatalog.DefaultSetting(AssessmentCatalog.Find(AssessmentCatalog.E3)),
            Subject = "T1", Evaluated = true, Passed = false, Note = "первая перекладка влево"
        };
        Assert.AreEqual("Поворот влево при сближении на встречных курсах — T1, правило 14 а: первая перекладка влево", DebriefText.ViolationText(e3));

        ExerciseSimulation none = AssessmentTests.B3Run("none");
        List<DebriefViolation> v = DebriefText.Violations(none.LiveResult);
        Assert.Greater(v.Count, 1);
        Assert.AreEqual(AssessmentCatalog.A2, v[0].Code, "авария — первой");
        StringAssert.StartsWith("Столкновение с «T2»", v[0].Text);
        bool minorSeen = false;
        foreach (DebriefViolation x in v)
        {
            if (!x.Severe) minorSeen = true;
            else Assert.IsFalse(minorSeen, "обязательные — до замечаний");
        }

        ExerciseSimulation port = AssessmentTests.B3Run("port");
        List<DebriefViolation> vp = DebriefText.Violations(port.LiveResult);
        DebriefViolation e5 = vp.Find(x => x.Code == AssessmentCatalog.E5);
        Assert.IsNotNull(e5);
        Assert.IsFalse(e5.Severe);
        StringAssert.EndsWith("(замечание)", e5.Text);
    }

    [Test]
    public void Pages_CriteriaTableSplit()
    {
        var rows = new List<int>();
        for (int i = 0; i < 23; i++) rows.Add(i);
        Assert.AreEqual(3, DebriefText.PageCount(rows.Count, 10));
        Assert.AreEqual(1, DebriefText.PageCount(0, 10));
        CollectionAssert.AreEqual(new[] { 20, 21, 22 }, DebriefText.Page(rows, 2, 10));
        CollectionAssert.AreEqual(new[] { 20, 21, 22 }, DebriefText.Page(rows, 7, 10), "за последней — последняя");
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, DebriefText.Page(rows, -1, 10));
    }

    // ─────────── Геометрия ───────────
    [Test]
    public void Geometry_DashRuns_TerminateAtRoundingEdge()
    {
        // Случай из шлема (C4-01, вид «весь маршрут»): штрих 41,58 м, пробел 29,7 м, ось фарватера 3500 м —
        // прежний цикл на s = 326,7 м получал шаг 2·10⁻¹⁴ м и не заканчивался (Unity зависал)
        var runs = new List<(double s0, double s1)>();
        double phase = 0;
        MapGeometry.DashRuns(3500, 41.58, 29.7, ref phase, runs);
        double period = 41.58 + 29.7, on = 0;
        foreach (var r in runs) { Assert.Greater(r.s1, r.s0); on += r.s1 - r.s0; }
        Assert.AreEqual(Math.Ceiling(3500 / period), runs.Count, 1, "штрих на каждый период");
        Assert.AreEqual(41.58 / period, on / 3500, 0.01, "доля штриха");
        Assert.GreaterOrEqual(phase, 0); Assert.Less(phase, period, "фаза — в пределах периода");

        // Перебор масштабов и длин: всегда конечное число штрихов
        for (int k = 1; k <= 400; k++)
        {
            double scale = 0.01 + k * 0.0123, dash = 7 / scale, gap = 5 / scale;
            phase = 0;
            int total = 0;
            foreach (double len in new[] { 3500.0, 2700.0, 1234.567, 0.3, 1e-9 })
            {
                MapGeometry.DashRuns(len, dash, gap, ref phase, runs);
                total += runs.Count;
                Assert.LessOrEqual(runs.Count, len / (dash + gap) + 2);
            }
            Assert.Greater(total, 0);
        }
    }

    [Test]
    public void Geometry_ClipTriangulateDecimate()
    {
        var r = new MapRect(0, 0, 100, 100);
        var a = new WorldPosition(-50, 50);
        var b = new WorldPosition(150, 50);
        Assert.IsTrue(MapGeometry.ClipSegment(r, ref a, ref b));
        Assert.AreEqual(0, a.East, 1e-9); Assert.AreEqual(100, b.East, 1e-9);
        var c = new WorldPosition(-10, -10);
        var d = new WorldPosition(-5, 200);
        Assert.IsFalse(MapGeometry.ClipSegment(r, ref c, ref d), "вне окна");

        // Квадрат 200×200 с центром в углу окна — в окне остаётся четверть
        var sq = new List<WorldPosition> { new WorldPosition(-100, -100), new WorldPosition(100, -100), new WorldPosition(100, 100), new WorldPosition(-100, 100) };
        Assert.AreEqual(100 * 100, Math.Abs(Polygon2D.SignedArea(MapGeometry.ClipPolygon(sq, r))), 1e-6);

        // Вогнутый многоугольник «Г»: сумма площадей треугольников = площадь
        var l = new List<WorldPosition>
        {
            new WorldPosition(0, 0), new WorldPosition(30, 0), new WorldPosition(30, 10),
            new WorldPosition(10, 10), new WorldPosition(10, 30), new WorldPosition(0, 30)
        };
        foreach (bool reverse in new[] { false, true })
        {
            var poly = new List<WorldPosition>(l);
            if (reverse) poly.Reverse();
            List<int> tri = MapGeometry.Triangulate(poly);
            Assert.AreEqual(3 * (poly.Count - 2), tri.Count);
            double sum = 0;
            for (int i = 0; i < tri.Count; i += 3)
                sum += Math.Abs(Polygon2D.SignedArea(new List<WorldPosition> { poly[tri[i]], poly[tri[i + 1]], poly[tri[i + 2]] }));
            Assert.AreEqual(Math.Abs(Polygon2D.SignedArea(poly)), sum, 1e-6);
        }

        var line = new List<WorldPosition>();
        for (int i = 0; i <= 100; i++) line.Add(new WorldPosition(i * 0.1, 0));
        List<WorldPosition> dec = MapGeometry.Decimate(line, 1.0);
        Assert.AreEqual(11, dec.Count, "шаг 0,1 м, порог 1 м");
        Assert.AreEqual(10.0, dec[dec.Count - 1].East, 1e-9, "последняя точка остаётся");
    }

    [Test]
    public void SvgPlot_NaNBreaksLine_MarksAndClipId()
    {
        var p = new SvgPlot { ClipId = "c7" };
        p.Add("курс", SvgPlot.Dash.Solid, new List<Vector2> { new Vector2(0, 350), new Vector2(1, 359), new Vector2(float.NaN, float.NaN), new Vector2(1, 1), new Vector2(2, 10) });
        p.AddMark(1.5f, "T2");
        string svg = p.Render();
        Assert.AreEqual(2, Count(svg, "<polyline"), "разрыв — два отрезка");
        StringAssert.Contains("<clipPath id=\"c7\">", svg);
        StringAssert.Contains("url(#c7)", svg);
        StringAssert.Contains(">T2</text>", svg);
        Assert.AreEqual(1, Count(svg, ">курс</text>"), "в легенде — одна строка");

        // По умолчанию — как до C3 (id «plot», без отметок)
        var q = new SvgPlot();
        q.Add("a", SvgPlot.Dash.Dashed, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 1) });
        string s = q.Render();
        StringAssert.Contains("<clipPath id=\"plot\">", s);
        Assert.AreEqual(1, Count(s, "<polyline"));
        Assert.AreEqual(0, Count(s, "stroke-width=\"0.8\" stroke-dasharray=\"2,3\""), "отметок нет");
    }
}
