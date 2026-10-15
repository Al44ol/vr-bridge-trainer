// ============================================================
// SessionReplayWindow.cs — Воспроизведение записанной сессии (C1)
// Меню: Tools/VR Bridge/Session Replay. Play Mode не нужен.
//
// Сессия читается SessionReader (Core) — тем же кодом, что будут
// использовать офлайн-оценка (C2) и отчёт (C3).
//   • выбор сессии из папки записи (новые сверху) или любой папки «Папка…»;
//   • карта (вид сверху): акватория (мели, суша, фарватер, знаки — из ассета
//     сценария с тем же id), маршрут и коридор ±XTE доп. из session.json,
//     траектория (пройденная часть: синий — в коридоре, красный — вне коридора,
//     бордовый — на мели), метки событий, судно; колесо — масштаб,
//     перетаскивание — сдвиг, «Вписать», «Следовать»;
//   • шкала времени: пуск/пауза, скорость ×1…×60, переход по событиям;
//   • состояние судна в момент t (интерполяция по track.csv) и список событий
//     (клик — перемотка);
//   • «Сохранить» — метка keep: сессия не удаляется ротацией записи;
//   • C3: «Отчёт (report.html)» — отчёт по записи (в том числе для старой сессии)
//     и открыть в браузере; «Сводка по папке…» — сводная таблица по сессиям.
//   • 19.2б-3б: причалы (пирс, кромка, кранцы, кнехты — из снимка session.json), судно
//     на крупном масштабе — контуром в масштабе; строка причала из mooring.csv в состоянии.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class SessionReplayWindow : EditorWindow
{
    private static readonly float[] Speeds = { 1f, 2f, 5f, 10f, 30f, 60f };
    private static readonly string[] SpeedNames = { "×1", "×2", "×5", "×10", "×30", "×60" };

    private static readonly Color Water = new Color(0.11f, 0.17f, 0.24f);
    private static readonly Color LandColor = new Color(0.75f, 0.62f, 0.42f);
    private static readonly Color ShoalColor = new Color(1f, 0.62f, 0.2f);
    private static readonly Color FairwayColor = new Color(0.55f, 0.65f, 0.75f, 0.6f);
    private static readonly Color RouteColor = new Color(0.85f, 0.92f, 1f);
    private static readonly Color CorridorColor = new Color(0.6f, 0.8f, 1f, 0.55f);
    private static readonly Color TrackFuture = new Color(0.55f, 0.6f, 0.65f, 0.45f);
    private static readonly Color TrackIn = new Color(0.3f, 0.7f, 1f);
    private static readonly Color TrackOut = new Color(1f, 0.3f, 0.3f);
    private static readonly Color TrackGrounded = new Color(0.75f, 0.1f, 0.35f);
    private static readonly Color ShipColor = new Color(1f, 0.95f, 0.4f);

    private string root;
    private List<string> sessions = new List<string>();
    private string[] sessionNames = new string[0];
    private int selected = -1;
    private RecordedSession session;
    // C2: оценка по записи и сравнение с оценкой в шлеме (assessment.csv)
    private AssessmentResult offline;
    private string assessmentMatch = "";
    private Vector2 assessmentScroll;
    private WaterArea area;

    private double t;
    private bool playing;
    private int speedIndex = 3;
    private double lastTick;

    private double viewE, viewN, mpp = 5.0;   // центр карты (м) и масштаб, м на пиксель
    private float halfW, halfH;
    private bool follow;
    private bool needFit = true;
    private Vector2 eventsScroll;
    private GUIStyle mapLabel, eventStyle, eventCurrentStyle;

    [MenuItem("Tools/VR Bridge/Session Replay")]
    public static void Open()
    {
        var w = GetWindow<SessionReplayWindow>();
        w.titleContent = new GUIContent("Session Replay");
        w.minSize = new Vector2(860f, 540f);
    }

    void OnEnable()
    {
        if (string.IsNullOrEmpty(root)) root = ExerciseRecorder.DefaultRoot;
        Refresh();
        EditorApplication.update += Tick;
    }

    void OnDisable() => EditorApplication.update -= Tick;

    // ─────────── Сессии ───────────
    private void Refresh()
    {
        string current = session != null ? session.Folder : null;
        sessions = SessionStorage.List(root);
        sessionNames = new string[sessions.Count];
        for (int i = 0; i < sessions.Count; i++)
            sessionNames[i] = Path.GetFileName(sessions[i]) + (SessionStorage.IsKept(sessions[i]) ? "  [сохранена]" : "");
        selected = current != null ? sessions.IndexOf(current) : -1;
        if (selected < 0 && sessions.Count > 0 && session == null) Load(0);
    }

    private void Load(int index)
    {
        if (index < 0 || index >= sessions.Count) return;
        selected = index;
        session = SessionReader.Load(sessions[index]);
        Assess();
        area = FindArea(session.Meta);
        t = 0;
        playing = false;
        needFit = true;
        Repaint();
    }

    private static WaterArea FindArea(SessionMeta meta) => DebriefReport.FindArea(meta);

    private double Duration => session != null ? session.Duration : 0;

    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        double dt = now - lastTick;
        lastTick = now;
        if (!playing || session == null) return;
        t += dt * Speeds[speedIndex];
        if (t >= Duration) { t = Duration; playing = false; }
        Repaint();
    }

    // ─────────── Интерфейс ───────────
    void OnGUI()
    {
        Styles();
        Toolbar();
        if (session == null)
        {
            EditorGUILayout.HelpBox($"В папке нет записанных сессий:\n{root}\n\nЗапись появляется после упражнения в Play Mode (Setup C1 Recording). Кнопка «Папка…» — открыть другую папку (например, скопированную из шлема).", MessageType.Info);
            return;
        }
        MetaLine();

        EditorGUILayout.BeginHorizontal();
        Rect map = GUILayoutUtility.GetRect(300f, 4000f, 300f, 4000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        Map(map);
        EditorGUILayout.BeginVertical(GUILayout.Width(330f));
        State();
        AssessmentView();
        Events();
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();

        Timeline();
    }

    private void Styles()
    {
        if (mapLabel != null) return;
        mapLabel = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.9f, 0.93f, 0.97f) } };
        eventStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, fontSize = 11 };
        eventCurrentStyle = new GUIStyle(eventStyle) { fontStyle = FontStyle.Bold };
    }

    private void Toolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        int ns = EditorGUILayout.Popup(selected, sessionNames, EditorStyles.toolbarPopup, GUILayout.Width(400f));
        if (ns != selected) Load(ns);
        if (GUILayout.Button("Обновить", EditorStyles.toolbarButton)) Refresh();
        if (GUILayout.Button("Папка…", EditorStyles.toolbarButton))
        {
            string p = EditorUtility.OpenFolderPanel("Папка сессий или одна сессия", root, "");
            if (!string.IsNullOrEmpty(p))
            {
                p = Path.GetFullPath(p);
                bool single = SessionStorage.IsSession(p);
                root = single ? Path.GetDirectoryName(p) : p;
                session = null;
                Refresh();
                if (single) Load(sessions.IndexOf(p));
            }
        }
        GUILayout.FlexibleSpace();
        if (session != null)
        {
            bool kept = SessionStorage.IsKept(session.Folder);
            bool nk = GUILayout.Toggle(kept, "Сохранить (не удалять при ротации)", EditorStyles.toolbarButton);
            if (nk != kept)
            {
                SessionStorage.SetKept(session.Folder, nk);
                Refresh();
            }
            if (GUILayout.Button("Отчёт (report.html)", EditorStyles.toolbarButton))
            {
                try { DebriefReport.BuildReport(session.Folder); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (GUILayout.Button("Показать в проводнике", EditorStyles.toolbarButton))
                EditorUtility.RevealInFinder(Path.Combine(session.Folder, SessionFormat.MetaFile));
        }
        if (GUILayout.Button("Сводка по папке…", EditorStyles.toolbarButton)) DebriefReport.Summary(root);
        EditorGUILayout.EndHorizontal();
    }

    private void MetaLine()
    {
        SessionMeta m = session.Meta;
        string trainee = string.IsNullOrEmpty(m.trainee_id) ? "—" : m.trainee_id;
        EditorGUILayout.LabelField(
            $"{m.scenario.id} «{m.scenario.title}» · обучающийся {trainee} · {m.start_local} · {ScenarioText.Clock(m.duration_s)} · " +
            $"{m.end_reason_ru}{(m.by_instructor ? " (инструктор)" : "")} · судно {m.ship.asset}" +
            (m.completed ? "" : " · НЕ ЗАВЕРШЕНА"), EditorStyles.boldLabel);
        if (session.Warnings.Count > 0)
            EditorGUILayout.HelpBox(string.Join("\n", session.Warnings.GetRange(0, Math.Min(5, session.Warnings.Count))), MessageType.Warning);
        if (area == null)
            EditorGUILayout.LabelField("Акватория не найдена в проекте — на карте только маршрут и траектория.", EditorStyles.miniLabel);
    }

    private void State()
    {
        TrackSample s = session.StateAt(t);
        EditorGUILayout.LabelField("Состояние судна", EditorStyles.boldLabel);
        Row("Время", $"{ScenarioText.Clock(t)} / {ScenarioText.Clock(Duration)}  ({ScenarioText.Num(t, 1)} с)");
        Row("Позиция", $"E {ScenarioText.Num(s.East)} м, N {ScenarioText.Num(s.North)} м");
        Row("Курс / COG", $"{ScenarioText.Num(s.HeadingDeg, 1)}° / {ScenarioText.Num(s.CogDeg, 1)}°");
        Row("Скорость", $"{ScenarioText.Num(s.SpeedKnots, 1)} уз");
        Row("ROT", $"{ScenarioText.Num(s.RotDegMin)} °/мин");
        Row("Руль зад. / факт.", $"{ScenarioText.Rudder(s.RudderOrderedDeg)} / {ScenarioText.Rudder(s.RudderActualDeg)}");
        Row("Телеграф", $"{ScenarioText.Telegraph((TelegraphOrder)Mathf.Clamp(s.Telegraph, 0, 8))} (SF {ScenarioText.Num(s.TelegraphSf, 2)})");
        Row("XTE", double.IsNaN(s.XteM) ? "—" : $"{ScenarioText.Num(s.XteM)} м (доп. {ScenarioText.Num(s.XteLimitM)} м), участок {s.Leg}");
        string st = s.Grounded ? "НА МЕЛИ" : !double.IsNaN(s.XteM) && Math.Abs(s.XteM) > s.XteLimitM ? "вне коридора" : "в коридоре";
        Row("Состояние", st);
        // 19.2б-3б: строка mooring.csv в момент t (если причал понадобился)
        MooringSample? ms = session.MooringAt(t);
        if (ms.HasValue && ms.Value.HasBerth)
        {
            MooringSample m = ms.Value;
            Row("Причал: нос / корма", $"{ScenarioText.Num(m.BowGapM, 1)} / {ScenarioText.Num(m.SternGapM, 1)} м, угол {ScenarioText.Num(m.AngleDeg)}°");
            Row("К причалу", $"нос {ScenarioText.Num(m.BowSpeedKn, 2)} · корма {ScenarioText.Num(m.SternSpeedKn, 2)} уз");
            Row("Кранцы", m.FenderContact ? $"обжаты до {ScenarioText.Num(m.FenderDeflectionRatio * 100.0)} %, {ScenarioText.Num(m.FenderForceKn, 1)} кН" : m.QuayContact ? "УДАР О ПРИЧАЛ" : "нет касания");
            int fast = 0;
            if (m.Lines != null) foreach (MooringLineSample l in m.Lines) if (l.Fast) fast++;
            Row("Концы / подрул.", $"подано {fast} · {ThrusterOrders.TitleOfPercent(m.ThrusterOrderPct)}" + (m.StagesDone >= 0 ? $" · заданий {m.StagesDone}" : ""));
        }
        EditorGUILayout.Space(4f);
    }

    private static void Row(string label, string value) => EditorGUILayout.LabelField(label, value);

    private void Assess()
    {
        offline = null; assessmentMatch = "";
        try
        {
            offline = SessionAssessment.Assess(session);
            string file = Path.Combine(session.Folder, SessionFormat.AssessmentFile);
            assessmentMatch = !File.Exists(file) ? "в шлеме не оценивалась (сессия до C2 или прервана)"
                : File.ReadAllText(file).TrimStart('﻿') == offline.ToCsv() ? "совпадает с оценкой в шлеме" : "НЕ совпадает с assessment.csv";
        }
        catch (Exception e) { assessmentMatch = "ошибка: " + e.Message; }
    }

    private void AssessmentView()
    {
        if (offline == null) return;
        EditorGUILayout.LabelField("Оценка по записи (C2)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(offline.Headline() + (offline.Failed ? " — " + offline.FailureText : ""), EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField(assessmentMatch, EditorStyles.miniLabel);
        assessmentScroll = EditorGUILayout.BeginScrollView(assessmentScroll, GUILayout.Height(160f));
        foreach (CriterionResult c in offline.Items)
            EditorGUILayout.LabelField($"{(c.Evaluated ? c.Passed ? "+" : "×" : "–")} {c.Code} {c.Subject}: {c.Note}", eventStyle);
        EditorGUILayout.EndScrollView();
        if (GUILayout.Button("Пересчитать оценку")) Assess();
        EditorGUILayout.Space(4f);
    }

    private void Events()
    {
        EditorGUILayout.LabelField($"События ({session.Events.Count})", EditorStyles.boldLabel);
        int current = -1;
        for (int i = 0; i < session.Events.Count; i++)
            if (session.Events[i].Time <= t + 1e-6) current = i;
        eventsScroll = EditorGUILayout.BeginScrollView(eventsScroll);
        for (int i = 0; i < session.Events.Count; i++)
        {
            ExerciseEvent e = session.Events[i];
            Color prev = GUI.contentColor;
            GUI.contentColor = e.Time > t + 1e-6 ? new Color(prev.r, prev.g, prev.b, 0.5f) : EventColor(e.Type, prev);
            if (GUILayout.Button($"{ScenarioText.Clock(e.Time)}  {e.Describe()}", i == current ? eventCurrentStyle : eventStyle))
            {
                t = e.Time;
                playing = false;
            }
            GUI.contentColor = prev;
        }
        EditorGUILayout.EndScrollView();
    }

    private void Timeline()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(playing ? "Пауза" : "Пуск", GUILayout.Width(64f)))
        {
            if (!playing && t >= Duration - 1e-6) t = 0;
            playing = !playing;
            lastTick = EditorApplication.timeSinceStartup;
        }
        if (GUILayout.Button("В начало", GUILayout.Width(70f))) t = 0;
        if (GUILayout.Button("◄ событие", GUILayout.Width(80f))) t = PrevEventTime();
        if (GUILayout.Button("событие ►", GUILayout.Width(80f))) t = NextEventTime();
        speedIndex = EditorGUILayout.Popup(speedIndex, SpeedNames, GUILayout.Width(52f));
        float nt = GUILayout.HorizontalSlider((float)t, 0f, Mathf.Max(0.001f, (float)Duration), GUILayout.ExpandWidth(true));
        if (Math.Abs(nt - t) > 1e-3) t = nt;
        GUILayout.Label(ScenarioText.Clock(t), GUILayout.Width(52f));
        follow = GUILayout.Toggle(follow, "Следовать", GUILayout.Width(80f));
        if (GUILayout.Button("Вписать", GUILayout.Width(64f))) { needFit = true; follow = false; }
        EditorGUILayout.EndHorizontal();
    }

    private double PrevEventTime()
    {
        double best = 0;
        foreach (ExerciseEvent e in session.Events) if (e.Time < t - 1e-3) best = e.Time;
        return best;
    }

    private double NextEventTime()
    {
        foreach (ExerciseEvent e in session.Events) if (e.Time > t + 1e-3) return e.Time;
        return Duration;
    }

    // ─────────── Карта ───────────
    private void Map(Rect rect)
    {
        Event ev = Event.current;
        if (ev.type == EventType.Layout) return;
        halfW = rect.width * 0.5f;
        halfH = rect.height * 0.5f;
        if (needFit) { Fit(rect); needFit = false; }
        TrackSample now = session.StateAt(t);
        if (follow && session.Track.Count > 0) { viewE = now.East; viewN = now.North; }

        if (rect.Contains(ev.mousePosition))
        {
            Vector2 local = ev.mousePosition - rect.position;
            if (ev.type == EventType.ScrollWheel)
            {
                double we = viewE + (local.x - halfW) * mpp, wn = viewN - (local.y - halfH) * mpp;
                mpp = Math.Max(0.2, Math.Min(500.0, mpp * (ev.delta.y > 0 ? 1.15 : 1.0 / 1.15)));
                if (!follow) { viewE = we - (local.x - halfW) * mpp; viewN = wn + (local.y - halfH) * mpp; }
                ev.Use();
                Repaint();
            }
            else if (ev.type == EventType.MouseDrag && (ev.button == 0 || ev.button == 2))
            {
                viewE -= ev.delta.x * mpp;
                viewN += ev.delta.y * mpp;
                follow = false;
                ev.Use();
                Repaint();
            }
        }
        if (ev.type != EventType.Repaint) return;

        GUI.BeginClip(rect);
        EditorGUI.DrawRect(new Rect(0, 0, rect.width, rect.height), Water);
        if (area != null) DrawArea();
        DrawBerths();
        DrawRoute();
        DrawTrack();
        DrawTargets();
        DrawEventMarkers();
        DrawShip(now);
        DrawScaleAndLegend(rect);
        GUI.EndClip();
    }

    private Vector3 P(double e, double n) => new Vector3((float)((e - viewE) / mpp) + halfW, halfH - (float)((n - viewN) / mpp), 0f);
    private Vector3 P(WorldPosition w) => P(w.East, w.North);

    private void Fit(Rect rect)
    {
        double minE = double.MaxValue, maxE = double.MinValue, minN = double.MaxValue, maxN = double.MinValue;
        void Add(double e, double n)
        {
            if (double.IsNaN(e) || double.IsNaN(n)) return;
            minE = Math.Min(minE, e); maxE = Math.Max(maxE, e);
            minN = Math.Min(minN, n); maxN = Math.Max(maxN, n);
        }
        foreach (TrackSample s in session.Track) Add(s.East, s.North);
        foreach (SessionWaypoint w in session.Meta.scenario.route) Add(w.x_e_m, w.y_n_m);
        if (minE > maxE && area != null) { Add(area.boundsMin.East, area.boundsMin.North); Add(area.boundsMax.East, area.boundsMax.North); }
        if (minE > maxE) { viewE = viewN = 0; mpp = 5; return; }
        viewE = 0.5 * (minE + maxE);
        viewN = 0.5 * (minN + maxN);
        mpp = Math.Max(0.5, Math.Max((maxE - minE) / Math.Max(50f, rect.width - 60f), (maxN - minN) / Math.Max(50f, rect.height - 60f)));
    }

    private void DrawArea()
    {
        foreach (LandArea l in area.land) Polygon(l.vertices, LandColor, 2.5f);
        foreach (ShoalArea s in area.shoals)
        {
            Polygon(s.vertices, ShoalColor, 1.5f);
            if (s.vertices.Count > 0)
            {
                double e = 0, n = 0;
                foreach (WorldPosition v in s.vertices) { e += v.East; n += v.North; }
                Vector3 c = P(e / s.vertices.Count, n / s.vertices.Count);
                GUI.Label(new Rect(c.x - 60f, c.y - 8f, 120f, 16f), $"{s.name} {ScenarioText.Num(s.depth, 1)} м", Centered(mapLabel, ShoalColor));
            }
        }
        foreach (FairwayLine f in area.fairways)
        {
            for (int i = 0; i + 1 < f.axis.Count; i++)
            {
                Handles.color = FairwayColor;
                Handles.DrawDottedLine(P(f.axis[i]), P(f.axis[i + 1]), 3f);
                OffsetSegment(f.axis[i], f.axis[i + 1], f.width * 0.5, FairwayColor, false);
                OffsetSegment(f.axis[i], f.axis[i + 1], -f.width * 0.5, FairwayColor, false);
            }
        }
        foreach (AidToNavigation a in area.aids)
        {
            Handles.color = MarkToColor(IalaMarks.Get(a.type).MainColor);
            Handles.DrawSolidDisc(P(a.position), Vector3.forward, 3.5f);
        }
    }

    // 19.2б-3б: причалы — из снимка в session.json (запись до 19.2б-3б — из ассета акватории): пирс, кромка,
    // кранцы (длина × вылет, при крупном масштабе), кнехты
    private static readonly Color PierColor = new Color(0.5f, 0.52f, 0.55f), QuayColor = new Color(0.95f, 0.95f, 0.95f), FenderColor = new Color(0.1f, 0.1f, 0.1f);

    private void DrawBerths()
    {
        var berths = new List<BerthArea>();
        SessionArea snap = session.Meta.scenario.area;
        if (snap != null && snap.berths != null && snap.berths.Count > 0)
            for (int i = 0; i < snap.berths.Count; i++) berths.Add(snap.Berth(i));
        else if (area != null && area.berths != null) berths.AddRange(area.berths);
        foreach (BerthArea b in berths)
        {
            if (b == null || b.Length < 1.0) continue;
            double w = Math.Max(1.0, b.structureWidthM);
            Handles.color = PierColor;
            Handles.DrawAAConvexPolygon(P(b.a), P(b.b), P(b.ToWorld(b.Length, -w)), P(b.ToWorld(0, -w)));
            Handles.color = QuayColor;
            Handles.DrawAAPolyLine(2.5f, P(b.a), P(b.b));
            if (mpp < 1.0)
            {
                double fl = 0.5 * b.fenderLengthM, fd = Math.Max(b.fenderOuterDiameterM, 0.5 * mpp);
                Handles.color = FenderColor;
                foreach (double ft in b.FenderPositions())
                    Handles.DrawAAConvexPolygon(P(b.ToWorld(ft - fl, 0)), P(b.ToWorld(ft + fl, 0)), P(b.ToWorld(ft + fl, fd)), P(b.ToWorld(ft - fl, fd)));
            }
            if (mpp < 2.0)
            {
                Handles.color = QuayColor;
                for (int k = 0; k < b.BollardPositions().Count; k++) Handles.DrawSolidDisc(P(b.BollardWorld(k)), Vector3.forward, 2f);
            }
            Vector3 c = P(b.ToWorld(0.5 * b.Length, -0.5 * w));
            GUI.Label(new Rect(c.x - 60f, c.y - 8f, 120f, 16f), b.name, Centered(mapLabel, QuayColor));
        }
    }

    private void DrawRoute()
    {
        List<SessionWaypoint> r = session.Meta.scenario.route;
        for (int i = 0; i + 1 < r.Count; i++)
        {
            var a = new WorldPosition(r[i].x_e_m, r[i].y_n_m);
            var b = new WorldPosition(r[i + 1].x_e_m, r[i + 1].y_n_m);
            Handles.color = RouteColor;
            Handles.DrawAAPolyLine(2f, P(a), P(b));
            OffsetSegment(a, b, r[i + 1].xte_limit_m, CorridorColor, true);
            OffsetSegment(a, b, -r[i + 1].xte_limit_m, CorridorColor, true);
        }
        for (int i = 0; i < r.Count; i++)
        {
            Vector3 p = P(r[i].x_e_m, r[i].y_n_m);
            Handles.color = RouteColor;
            Handles.DrawWireDisc(p, Vector3.forward, 5f);
            GUI.Label(new Rect(p.x + 7f, p.y - 8f, 200f, 16f), $"{i} {r[i].name}", mapLabel);
        }
    }

    private void DrawTrack()
    {
        List<TrackSample> tr = session.Track;
        if (tr.Count < 2) return;
        int nowIndex = session.IndexAt(t);

        // Вся траектория — бледно; пройденная — по цвету состояния
        var all = new List<Vector3>(tr.Count);
        Vector3 last = new Vector3(float.NaN, 0, 0);
        foreach (TrackSample s in tr) AddDecimated(all, P(s.East, s.North), ref last);
        Handles.color = TrackFuture;
        if (all.Count > 1) Handles.DrawAAPolyLine(1.5f, all.ToArray());

        var run = new List<Vector3>();
        Color runColor = Color.clear;
        last = new Vector3(float.NaN, 0, 0);
        for (int i = 0; i <= nowIndex + 1 && i < tr.Count; i++)
        {
            TrackSample s = i <= nowIndex ? tr[i] : session.StateAt(t);
            Color c = s.Grounded ? TrackGrounded : !double.IsNaN(s.XteM) && Math.Abs(s.XteM) > s.XteLimitM ? TrackOut : TrackIn;
            Vector3 p = P(s.East, s.North);
            if (c != runColor && run.Count > 0)
            {
                run.Add(p);
                Flush(run, runColor);
                run.Clear();
                last = new Vector3(float.NaN, 0, 0);
            }
            runColor = c;
            AddDecimated(run, p, ref last);
        }
        Flush(run, runColor);
    }

    // Другие суда (B3, targets.csv): весь путь — бледно, пройденный — ярче, судно в момент t
    private static readonly Color TargetColor = new Color(1f, 0.62f, 0.2f);

    private void DrawTargets()
    {
        if (session.Targets.Count == 0) return;
        var byId = new Dictionary<string, List<TargetSample>>();
        foreach (TargetSample s in session.Targets)
        {
            if (!byId.TryGetValue(s.Id ?? "", out List<TargetSample> list)) { list = new List<TargetSample>(); byId[s.Id ?? ""] = list; }
            list.Add(s);
        }
        foreach (KeyValuePair<string, List<TargetSample>> kv in byId)
        {
            List<TargetSample> tr = kv.Value;
            var all = new List<Vector3>();
            var done = new List<Vector3>();
            Vector3 last = new Vector3(float.NaN, 0, 0), lastDone = new Vector3(float.NaN, 0, 0);
            int k = -1;
            for (int i = 0; i < tr.Count; i++)
            {
                Vector3 p = P(tr[i].East, tr[i].North);
                AddDecimated(all, p, ref last);
                if (tr[i].Time <= t) { AddDecimated(done, p, ref lastDone); k = i; }
            }
            Handles.color = new Color(TargetColor.r, TargetColor.g, TargetColor.b, 0.3f);
            if (all.Count > 1) Handles.DrawAAPolyLine(1.5f, all.ToArray());
            if (k < 0) continue;

            // Состояние в момент t — интерполяция между строками
            TargetSample a = tr[k];
            double e = a.East, n = a.North, h = a.HeadingDeg;
            if (k + 1 < tr.Count && tr[k + 1].Time > a.Time)
            {
                TargetSample b = tr[k + 1];
                double f = Math.Max(0.0, Math.Min(1.0, (t - a.Time) / (b.Time - a.Time)));
                e += (b.East - a.East) * f; n += (b.North - a.North) * f;
                h = RecordedSession.LerpAngle(a.HeadingDeg, b.HeadingDeg, f);
            }
            done.Add(P(e, n));
            Handles.color = TargetColor;
            if (done.Count > 1) Handles.DrawAAPolyLine(2.5f, done.ToArray());
            Vector3 c = P(e, n);
            float psi = (float)(h * Math.PI / 180.0);
            var fw = new Vector3(Mathf.Sin(psi), -Mathf.Cos(psi), 0f);
            var rt = new Vector3(Mathf.Cos(psi), Mathf.Sin(psi), 0f);
            Handles.DrawAAConvexPolygon(c + fw * 9f, c - fw * 6f + rt * 4f, c - fw * 6f - rt * 4f);
            GUI.Label(new Rect(c.x + 8f, c.y - 8f, 120f, 16f), kv.Key, Centered(mapLabel, TargetColor));
        }
    }

    private static void AddDecimated(List<Vector3> list, Vector3 p, ref Vector3 last)
    {
        if (!float.IsNaN(last.x) && (p - last).sqrMagnitude < 2.25f) return;   // точки ближе 1,5 пикселя пропускаем
        list.Add(p);
        last = p;
    }

    private static void Flush(List<Vector3> run, Color c)
    {
        if (run.Count < 2) return;
        Handles.color = c;
        Handles.DrawAAPolyLine(3f, run.ToArray());
    }

    private void DrawEventMarkers()
    {
        foreach (ExerciseEvent e in session.Events)
        {
            if (double.IsNaN(e.Position.East) || e.Type == ExerciseEventType.HelmStarted || e.Type == ExerciseEventType.HornEnded) continue;
            Color c = EventColor(e.Type, Color.white);
            if (e.Time > t + 1e-6) c.a = 0.3f;
            Handles.color = c;
            float r = e.Type >= ExerciseEventType.HelmStarted ? 2.5f : 4.5f;
            Handles.DrawSolidDisc(P(e.Position), Vector3.forward, r);
        }
    }

    private void DrawShip(TrackSample s)
    {
        if (session.Track.Count == 0) return;
        // 19.2б-3б: на крупном масштабе — контур корпуса в масштабе (MooringHull), как на схеме «У причала»
        double L = session.Meta.ship.length_m, B = session.Meta.ship.beam_m;
        if (L > 0 && B > 0 && L / mpp >= 40.0)
        {
            List<WorldPosition> hull = DebriefMap.HullOutline(s.Position, s.HeadingDeg, L, B);
            var pts = new Vector3[hull.Count + 1];
            for (int i = 0; i < hull.Count; i++) pts[i] = P(hull[i]);
            pts[hull.Count] = pts[0];
            Handles.color = new Color(ShipColor.r, ShipColor.g, ShipColor.b, 0.35f);
            Handles.DrawAAConvexPolygon(pts[0], pts[1], pts[2], pts[3], pts[4]);
            Handles.color = ShipColor;
            Handles.DrawAAPolyLine(2f, pts);
            return;
        }
        Vector3 c = P(s.East, s.North);
        float len = Mathf.Max((float)(session.Meta.ship.length_m / mpp), 16f);
        float wid = Mathf.Max((float)(session.Meta.ship.beam_m / mpp), len * 0.35f);
        float psi = (float)(s.HeadingDeg * Math.PI / 180.0);
        var f = new Vector3(Mathf.Sin(psi), -Mathf.Cos(psi), 0f);   // нос на экране (север — вверх)
        var r = new Vector3(Mathf.Cos(psi), Mathf.Sin(psi), 0f);    // правый борт
        Vector3 bow = c + f * len * 0.6f, sl = c - f * len * 0.4f - r * wid * 0.5f, sr = c - f * len * 0.4f + r * wid * 0.5f;
        Handles.color = ShipColor;
        Handles.DrawAAConvexPolygon(bow, sr, sl);
        Handles.color = Color.black;
        Handles.DrawAAPolyLine(1.5f, bow, sr, sl, bow);
    }

    private void DrawScaleAndLegend(Rect rect)
    {
        // Масштабная линейка: 1, 2 или 5 × 10^k метров ≈ 120 пикселей
        double target = 120.0 * mpp;
        double pow = Math.Pow(10, Math.Floor(Math.Log10(target)));
        double len = target / pow >= 5 ? 5 * pow : target / pow >= 2 ? 2 * pow : pow;
        float px = (float)(len / mpp);
        var a = new Vector3(14f, rect.height - 16f, 0f);
        Handles.color = Color.white;
        Handles.DrawAAPolyLine(2f, a, a + new Vector3(px, 0f, 0f));
        GUI.Label(new Rect(a.x, a.y - 18f, 160f, 16f), len >= 1000 ? $"{ScenarioText.Num(len / 1000.0, len >= 10000 ? 0 : 1)} км" : $"{len:F0} м", mapLabel);
        GUI.Label(new Rect(10f, 6f, 40f, 16f), "N ↑", mapLabel);
        GUI.Label(new Rect(rect.width - 330f, 6f, 324f, 16f), "синий — в коридоре · красный — вне коридора · бордовый — на мели",
            new GUIStyle(mapLabel) { alignment = TextAnchor.UpperRight });
    }

    private void Polygon(List<WorldPosition> v, Color c, float width)
    {
        if (v == null || v.Count < 2) return;
        var pts = new Vector3[v.Count + 1];
        for (int i = 0; i < v.Count; i++) pts[i] = P(v[i]);
        pts[v.Count] = pts[0];
        Handles.color = c;
        Handles.DrawAAPolyLine(width, pts);
    }

    // Отрезок, смещённый вправо (> 0) или влево (< 0) от направления a → b на offset метров
    private void OffsetSegment(WorldPosition a, WorldPosition b, double offset, Color c, bool dotted)
    {
        double de = b.East - a.East, dn = b.North - a.North, len = Math.Sqrt(de * de + dn * dn);
        if (len < 1e-6) return;
        double ne = dn / len * offset, nn = -de / len * offset;   // нормаль вправо (t_N, −t_E)
        Vector3 p0 = P(a.East + ne, a.North + nn), p1 = P(b.East + ne, b.North + nn);
        Handles.color = c;
        if (dotted) Handles.DrawDottedLine(p0, p1, 4f);
        else Handles.DrawAAPolyLine(1f, p0, p1);
    }

    private static GUIStyle Centered(GUIStyle s, Color c) => new GUIStyle(s) { alignment = TextAnchor.MiddleCenter, normal = { textColor = c } };

    private static Color MarkToColor(MarkColor m)
    {
        switch (m)
        {
            case MarkColor.Red: return new Color(0.95f, 0.25f, 0.2f);
            case MarkColor.Green: return new Color(0.2f, 0.85f, 0.35f);
            case MarkColor.Yellow: return new Color(1f, 0.9f, 0.2f);
            case MarkColor.Black: return new Color(0.15f, 0.15f, 0.15f);
            default: return Color.white;
        }
    }

    private static Color EventColor(ExerciseEventType type, Color fallback)
    {
        switch (type)
        {
            case ExerciseEventType.WaypointPassed: return new Color(0.5f, 0.9f, 0.5f);
            case ExerciseEventType.CorridorExit: return new Color(1f, 0.45f, 0.4f);
            case ExerciseEventType.CorridorReturn: return new Color(0.6f, 0.85f, 1f);
            case ExerciseEventType.ShoalWarningStarted: return ShoalColor;
            case ExerciseEventType.Grounded:
            case ExerciseEventType.Collision: return new Color(1f, 0.2f, 0.5f);
            case ExerciseEventType.HelmStarted:
            case ExerciseEventType.HelmEnded:
            case ExerciseEventType.TelegraphChanged: return new Color(0.85f, 0.75f, 1f);
            default: return fallback;
        }
    }
}
