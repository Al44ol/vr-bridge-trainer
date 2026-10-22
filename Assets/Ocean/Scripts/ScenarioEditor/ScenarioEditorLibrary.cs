// ============================================================
// ScenarioEditorLibrary.cs — Лист «Библиотека» редактора (запрос 21.6в; часть ScenarioEditorApp, partial-класс)
//
// Кнопка «Открыть…» верхней панели (и «Библиотека…» в стартовых диалогах) — лист на месте карты:
//   вкладки «Сценарии (N)», «Акватории (N)», «Корзина (N)»; слева — список своих файлов страницами
//   (✓ / ! предупреждения / × ошибки, «(открыт)» — документ редактора); справа — выбранный файл и действия:
//   «Открыть в редакторе», «Копия», «Переименовать…», «В корзину…», «История версий (N)…», «Экспорт на флешку…»,
//   ниже — «Новый «с нуля»… / На основе встроенного…» (акватории — «Новая… / Копия встроенной…»).
//   Корзина: «Восстановить» (место занято — «Заменить / Копией»), «Восстановить копией», «Очистить корзину…»
//   (насовсем — с подтверждением). История версий: список версий, «Восстановить эту версию» (текущая — в историю).
//   Внизу — «Импорт с флешки…», «Экспорт всех своих…», «Открыть папку в Проводнике» и итог последнего действия.
// Флешка — съёмный диск из списка (или путь вручную), на ней «VRBridgeTrainer\Мои сценарии|Мои акватории».
// Файл, открытый в редакторе с несохранёнными правками, перед переименованием / корзиной / откатом — «Сохранить?»;
// после переименования или отката документ открывается заново из файла. Core — ScenarioLibrary. Консоль — «[Библиотека] …».
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;

public partial class ScenarioEditorApp
{
    private enum LibTab { Scenarios, Areas, Trash, Versions }

    private const int LibRows = 14;
    private RectTransform libSheet, libFileActions, libTrashActions, libVersionActions;
    private LibTab libTab = LibTab.Scenarios, libBackTab = LibTab.Scenarios;
    private int libSel = -1, libTop, libSelVersions;
    private LibraryScan libScan = new LibraryScan();
    private List<TrashEntry> libTrash = new List<TrashEntry>();
    private List<HistoryVersion> libVersions = new List<HistoryVersion>();
    private LibraryEntry libVersionsOf;
    private readonly RectTransform[] libRowButtons = new RectTransform[LibRows];
    private readonly string[] libRowText = new string[LibRows];
    private TextMeshProUGUI libHeader, libInfo, libListInfo, libMessage, libListTitle;
    private string libMessageText = "";
    private static string lastFlashPath = "";

    private List<LibraryEntry> LibFiles => libTab == LibTab.Areas ? libScan.Areas : libScan.Scenarios;
    private int LibCount => libTab == LibTab.Trash ? libTrash.Count : libTab == LibTab.Versions ? libVersions.Count : LibFiles.Count;
    private LibraryEntry LibEntry => (libTab == LibTab.Scenarios || libTab == LibTab.Areas) && libSel >= 0 && libSel < LibFiles.Count ? LibFiles[libSel] : null;
    private TrashEntry LibTrashSel => libTab == LibTab.Trash && libSel >= 0 && libSel < libTrash.Count ? libTrash[libSel] : null;
    private HistoryVersion LibVersion => libTab == LibTab.Versions && libSel >= 0 && libSel < libVersions.Count ? libVersions[libSel] : null;

    // ─────────── Построение ───────────
    private void BuildLibrarySheet()
    {
        RectTransform p = libSheet = Sheet("LibrarySheet", "Библиотека — свои сценарии и акватории", out float y0);
        float listW = 760f, x1 = 20f, x2 = x1 + listW + 30f, colW = SheetWidth - x2 - 20f;

        // Вкладки
        float x = x1, y = y0;
        float tab = (listW - 2f * MonitorUi.Gap) / 3f;
        ui.Button(p, ref x, y, tab, "", MonitorUi.NeutralColor, () => LibSetTab(LibTab.Scenarios), () => $"Сценарии ({libScan.Scenarios.Count})",
                  () => libTab == LibTab.Scenarios || libTab == LibTab.Versions && libBackTab == LibTab.Scenarios ? MonitorUi.ActiveColor : MonitorUi.NeutralColor);
        ui.Button(p, ref x, y, tab, "", MonitorUi.NeutralColor, () => LibSetTab(LibTab.Areas), () => $"Акватории ({libScan.Areas.Count})",
                  () => libTab == LibTab.Areas || libTab == LibTab.Versions && libBackTab == LibTab.Areas ? MonitorUi.ActiveColor : MonitorUi.NeutralColor);
        ui.Button(p, ref x, y, tab, "", MonitorUi.NeutralColor, () => LibSetTab(LibTab.Trash), () => $"Корзина ({libTrash.Count})",
                  () => libTab == LibTab.Trash ? MonitorUi.ActiveColor : MonitorUi.NeutralColor);
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
        libListTitle = MonitorUi.Text(p, "", 15f, MonitorUi.MutedColor, TextAlignmentOptions.BottomLeft);
        MonitorUi.TopLeft(libListTitle.rectTransform, x1, y, listW, 22f);
        y -= 24f;

        // Список
        for (int i = 0; i < LibRows; i++)
        {
            int row = i;
            float xr = x1;
            libRowButtons[i] = ui.Button(p, ref xr, y, listW, "", MonitorUi.NeutralColor, () => { libSel = libTop + row; FillLibrary(); }, () => libRowText[row] ?? "",
                                         () => libTop + row == libSel ? MonitorUi.ActiveColor : MonitorUi.NeutralColor, null, 34f, 14f);
            libRowButtons[i].GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
            y -= 34f + MonitorUi.Gap;
        }
        float xs = x1;
        ui.Button(p, ref xs, y, 48f, "<", MonitorUi.NeutralColor, () => LibScroll(-LibRows), null, null, () => libTop > 0);
        libListInfo = MonitorUi.Text(p, "", 14f, MonitorUi.MutedColor, TextAlignmentOptions.Center);
        MonitorUi.TopLeft(libListInfo.rectTransform, xs, y, listW - 2f * (48f + MonitorUi.Gap), MonitorUi.RowHeight);
        xs = x1 + listW - 48f;
        ui.Button(p, ref xs, y, 48f, ">", MonitorUi.NeutralColor, () => LibScroll(LibRows), null, null, () => libTop + LibRows < LibCount);
        y -= MonitorUi.RowHeight + 2f * MonitorUi.Gap;
        float third = (listW - 2f * MonitorUi.Gap) / 3f;
        xs = x1;
        ui.Button(p, ref xs, y, third, "Импорт с флешки…", MonitorUi.StartColor, () => FlashDialog(true, null));
        ui.Button(p, ref xs, y, third, "Экспорт всех своих…", MonitorUi.ButtonColor, () => FlashDialog(false, AllOwnOk()), null, null, () => AllOwnOk().Count > 0);
        ui.Button(p, ref xs, y, third, "Открыть папку в Проводнике", MonitorUi.NeutralColor, () => Application.OpenURL("file:///" + Root.Replace('\\', '/')));
        y -= MonitorUi.RowHeight + 2f * MonitorUi.Gap;
        libMessage = MonitorUi.Text(p, "", 14f, Color.white, TextAlignmentOptions.TopLeft);
        libMessage.overflowMode = TextOverflowModes.Ellipsis;   // длинный итог импорта — полностью в консоли
        MonitorUi.TopLeft(libMessage.rectTransform, x1, y, listW, SheetHeight + y - 12f);

        // Выбранный файл
        y = y0;
        libHeader = MonitorUi.Text(p, "", 18f, MonitorUi.TitleColor, TextAlignmentOptions.TopLeft);
        libHeader.fontStyle = FontStyles.Bold;
        MonitorUi.TopLeft(libHeader.rectTransform, x2, y, colW, 54f);
        y -= 58f;
        libInfo = MonitorUi.Text(p, "", 14f, Color.white, TextAlignmentOptions.TopLeft);
        MonitorUi.TopLeft(libInfo.rectTransform, x2, y, colW, 300f);
        y -= 306f;

        float ya = 0f, step = MonitorUi.RowHeight + MonitorUi.Gap;
        RectTransform f = libFileActions = SubPanel(p, "LibFileActions", x2, y, colW, 11f * step);
        LibButton(f, ref ya, colW, "Открыть в редакторе", MonitorUi.StartColor, LibOpen, () => LibEntry != null && LibEntry.Ok);
        LibButton(f, ref ya, colW, "Копия", MonitorUi.ButtonColor, LibCopy, () => LibEntry != null && LibEntry.Ok);
        LibButton(f, ref ya, colW, "Переименовать…", MonitorUi.ButtonColor, LibRename, () => LibEntry != null && LibEntry.Ok);
        LibButton(f, ref ya, colW, "В корзину…", MonitorUi.StopColor, LibTrashAsk, () => LibEntry != null);
        LibButton(f, ref ya, colW, "", MonitorUi.NeutralColor, LibShowVersions, () => LibEntry != null && libSelVersions > 0, () => $"История версий ({libSelVersions})…");
        LibButton(f, ref ya, colW, "Экспорт на флешку…", MonitorUi.NeutralColor, () => FlashDialog(false, new List<LibraryEntry> { LibEntry }), () => LibEntry != null && LibEntry.Ok);
        ya -= 2f * MonitorUi.Gap;
        LibButton(f, ref ya, colW, "", MonitorUi.StartColor, LibNew, null, () => libTab == LibTab.Areas ? "Новая акватория (пустая)…" : "Новый сценарий «с нуля»…");
        LibButton(f, ref ya, colW, "", MonitorUi.ButtonColor, LibFromBuiltIn, null, () => libTab == LibTab.Areas ? "Копия встроенной акватории…" : "На основе встроенного…");

        ya = 0f;
        RectTransform t = libTrashActions = SubPanel(p, "LibTrashActions", x2, y, colW, 4f * step);
        LibButton(t, ref ya, colW, "Восстановить", MonitorUi.StartColor, () => LibRestore(false), () => LibTrashSel != null);
        LibButton(t, ref ya, colW, "Восстановить копией", MonitorUi.ButtonColor, LibRestoreCopy, () => LibTrashSel != null);
        ya -= 2f * MonitorUi.Gap;
        LibButton(t, ref ya, colW, "Очистить корзину…", MonitorUi.StopColor, LibEmptyTrashAsk, () => libTrash.Count > 0);

        ya = 0f;
        RectTransform v = libVersionActions = SubPanel(p, "LibVersionActions", x2, y, colW, 3f * step);
        LibButton(v, ref ya, colW, "Восстановить эту версию…", MonitorUi.WarnColor, LibRestoreVersionAsk, () => LibVersion != null);
        LibButton(v, ref ya, colW, "Назад к списку", MonitorUi.NeutralColor, () => LibSetTab(libBackTab), null);
    }

    private void LibButton(RectTransform p, ref float y, float w, string label, Color color, Action action, Func<bool> enabled, Func<string> text = null)
    {
        float x = 0f;
        ui.Button(p, ref x, y, w, label, color, () => action(), text, null, enabled);
        y -= MonitorUi.RowHeight + MonitorUi.Gap;
    }

    // ─────────── Открыть, заполнить ───────────
    private void OpenLibrary(bool areas)
    {
        if (libSheet == null) return;
        EndFieldEdit();
        foreach (RectTransform r in new[] { condSheet, critSheet, briefSheet, taskSheet, trigSheet }) if (r != null) r.gameObject.SetActive(false);
        libTab = areas ? LibTab.Areas : LibTab.Scenarios;
        libSel = -1;
        libTop = 0;
        RescanLibrary();
        string open = OpenFilePath(libTab == LibTab.Areas ? LibraryKind.Area : LibraryKind.Scenario);
        libSel = LibFiles.FindIndex(e => SameFile(e.FilePath, open));   // выбран открытый в редакторе
        libSheet.gameObject.SetActive(true);
        Refresh();
    }

    private void RescanLibrary()
    {
        try
        {
            libScan = ScenarioLibrary.Scan(Root, Catalog);
            libTrash = ScenarioLibrary.TrashList(Root);
            if (libTab == LibTab.Versions && libVersionsOf != null) libVersions = ScenarioLibrary.Versions(Root, libVersionsOf.Kind, libVersionsOf.FilePath);
        }
        catch (Exception e)
        {
            libMessageText = $"<color=#FF7A7A>Папка {Root} не читается: {e.Message}</color>";
            Debug.LogWarning("[Библиотека] " + libMessageText);
        }
    }

    private void LibSetTab(LibTab t)
    {
        libTab = t;
        libSel = -1;
        libTop = 0;
        RescanLibrary();
        if (t != LibTab.Versions && t != LibTab.Trash)
        {
            string open = OpenFilePath(t == LibTab.Areas ? LibraryKind.Area : LibraryKind.Scenario);
            libSel = LibFiles.FindIndex(e => SameFile(e.FilePath, open));
        }
        if (libSel < 0 && LibCount > 0) libSel = 0;
        FillLibrary();
    }

    private void LibScroll(int d)
    {
        libTop = Mathf.Clamp(libTop + d, 0, Math.Max(0, (LibCount - 1) / LibRows * LibRows));
        FillLibrary();
    }

    // Выбрать файл по пути после действия (копия, переименование, импорт)
    private void LibSelect(LibraryKind kind, string path)
    {
        libTab = kind == LibraryKind.Area ? LibTab.Areas : LibTab.Scenarios;
        RescanLibrary();
        libSel = LibFiles.FindIndex(e => SameFile(e.FilePath, path));
        if (libSel >= 0) libTop = libSel / LibRows * LibRows;
    }

    private void FillLibrary()
    {
        if (!Active(libSheet)) return;
        int n = LibCount;
        if (libSel >= n) libSel = n - 1;
        if (libSel < 0 && n > 0) libSel = 0;
        if (libSel >= 0 && (libSel < libTop || libSel >= libTop + LibRows)) libTop = libSel / LibRows * LibRows;
        if (libTop >= n) libTop = Math.Max(0, (n - 1) / LibRows * LibRows);

        string openS = OpenFilePath(LibraryKind.Scenario), openA = OpenFilePath(LibraryKind.Area);
        for (int i = 0; i < LibRows; i++)
        {
            int k = libTop + i;
            bool on = k < n;
            libRowButtons[i].gameObject.SetActive(on);
            libRowText[i] = on ? RowText(k, openS, openA) : "";
        }
        libListInfo.text = n == 0 ? EmptyText() : n > LibRows ? $"{libTop + 1}–{Math.Min(n, libTop + LibRows)} из {n}" : $"всего {n}";
        libListTitle.text = libTab == LibTab.Scenarios ? $"«{ScenarioFolder.ScenariosDir}» — {Path.Combine(Root, ScenarioFolder.ScenariosDir)}"
                          : libTab == LibTab.Areas ? $"«{ScenarioFolder.AreasDir}» — {Path.Combine(Root, ScenarioFolder.AreasDir)}"
                          : libTab == LibTab.Trash ? $"Удалённые — {Path.Combine(Root, ScenarioLibrary.TrashDir)} (вернуть — «Восстановить»)"
                          : $"Версии «{libVersionsOf?.Name}» — последние {AreaDocument.HistoryKeep}, новые — первыми";

        bool files = libTab == LibTab.Scenarios || libTab == LibTab.Areas;
        libFileActions.gameObject.SetActive(files);
        libTrashActions.gameObject.SetActive(libTab == LibTab.Trash);
        libVersionActions.gameObject.SetActive(libTab == LibTab.Versions);
        LibraryEntry e = LibEntry;
        libSelVersions = e != null ? ScenarioLibrary.Versions(Root, e.Kind, e.FilePath).Count : 0;
        libHeader.text = files ? (e != null ? e.Name : "файл не выбран")
                       : libTab == LibTab.Trash ? (LibTrashSel != null ? LibTrashSel.Name : "корзина пуста")
                       : (LibVersion != null ? $"Версия от {LibVersion.When:dd.MM.yyyy HH:mm:ss}" : "версий нет");
        libInfo.text = files ? EntryInfo(e) : libTab == LibTab.Trash ? TrashInfo(LibTrashSel) : VersionInfo(LibVersion);
        libMessage.text = libMessageText;
    }

    private string EmptyText() =>
        libTab == LibTab.Scenarios ? "своих сценариев нет — «Новый сценарий» или «Импорт с флешки»"
        : libTab == LibTab.Areas ? "своих акваторий нет — «Новая акватория» или «Копия встроенной»"
        : libTab == LibTab.Trash ? "корзина пуста" : "версий нет";

    private string RowText(int k, string openS, string openA)
    {
        const string grey = "<color=#9AA6B2>";
        switch (libTab)
        {
            case LibTab.Trash:
            {
                TrashEntry t = libTrash[k];
                return $"{t.Name}  {grey}· {ScenarioLibrary.KindTitle(t.Kind)} · удалён {t.DeletedAt:dd.MM HH:mm}</color>";
            }
            case LibTab.Versions:
            {
                HistoryVersion v = libVersions[k];
                return $"{v.When:dd.MM.yyyy HH:mm:ss}  {grey}· {ScenarioLibrary.SizeText(v.Bytes)}</color>";
            }
            default:
            {
                LibraryEntry e = LibFiles[k];
                string mark = !e.Ok ? "<color=#FF7A7A>×</color>" : e.Warnings.Count > 0 ? "<color=#FFC85A>!</color>" : "<color=#6FE08A>✓</color>";
                bool open = SameFile(e.FilePath, e.Kind == LibraryKind.Scenario ? openS : openA);
                string what = !e.Ok ? "ошибки" : e.Kind == LibraryKind.Scenario ? e.AreaName : $"сценариев {e.UsedBy}";
                return $"{mark} {e.Name}{(open ? " <color=#6FE08A>(открыт)</color>" : "")}  {grey}· {what} · {e.Modified:dd.MM HH:mm}</color>";
            }
        }
    }

    private string EntryInfo(LibraryEntry e)
    {
        if (e == null) return "";
        var b = new StringBuilder();
        string folder = e.Kind == LibraryKind.Scenario ? ScenarioFolder.ScenariosDir : ScenarioFolder.AreasDir;
        b.AppendLine($"<color=#9AA6B2>Файл:</color> {Path.Combine(folder, e.FileName)}");
        b.AppendLine($"<color=#9AA6B2>Изменён:</color> {e.Modified:dd.MM.yyyy HH:mm} · {ScenarioLibrary.SizeText(e.Bytes)}");
        if (e.Ok && e.Kind == LibraryKind.Scenario)
        {
            b.AppendLine($"<color=#9AA6B2>Акватория:</color> {e.AreaName} · своё судно: {(e.Scenario.ownShipType == ScenarioShipType.Tug ? "буксир" : "модель 45 м")}");
            b.AppendLine($"<color=#9AA6B2>Точек маршрута:</color> {e.Scenario.route?.Count ?? 0} · судов {e.Scenario.traffic?.Count ?? 0} · триггеров {e.Scenario.triggers?.Count ?? 0}");
        }
        if (e.Ok && e.Kind == LibraryKind.Area)
            b.AppendLine($"<color=#9AA6B2>Своих сценариев на ней:</color> {e.UsedBy} (у них копия акватории вписана в файл)");
        b.AppendLine($"<color=#9AA6B2>Версий в «История»:</color> {libSelVersions}");
        int shown = 0;
        foreach (string x in e.Errors) if (shown++ < 6) b.AppendLine($"<color=#FF7A7A>× {x}</color>");
        foreach (string x in e.Warnings) if (shown++ < 8) b.AppendLine($"<color=#FFC85A>! {x}</color>");
        if (e.Errors.Count + e.Warnings.Count > shown) b.AppendLine($"<color=#9AA6B2>… и ещё {e.Errors.Count + e.Warnings.Count - shown}</color>");
        if (!e.Ok) b.AppendLine("<color=#9AA6B2>Файл с ошибками не открывается: «История версий» — откатить, или исправить файл в Блокноте.</color>");
        return b.ToString().TrimEnd();
    }

    private static string TrashInfo(TrashEntry t)
    {
        if (t == null) return "";
        string folder = t.Kind == LibraryKind.Scenario ? ScenarioFolder.ScenariosDir : ScenarioFolder.AreasDir;
        return $"<color=#9AA6B2>Было:</color> {Path.Combine(folder, t.OriginalName)}\n" +
               $"<color=#9AA6B2>Удалён:</color> {t.DeletedAt:dd.MM.yyyy HH:mm:ss}\n" +
               $"<color=#9AA6B2>В корзине:</color> {Path.Combine(ScenarioLibrary.TrashDir, ScenarioLibrary.Section(t.Kind), Path.GetFileName(t.FilePath))}\n\n" +
               "<color=#9AA6B2>«Восстановить» — на прежнее место; занято — «Заменить» (занявший файл — в «История») или копией с новым id / именем.</color>";
    }

    private string VersionInfo(HistoryVersion v)
    {
        if (v == null || libVersionsOf == null) return "";
        return $"<color=#9AA6B2>Файл версии:</color> {Path.GetFileName(v.FilePath)} · {ScenarioLibrary.SizeText(v.Bytes)}\n\n" +
               $"<color=#9AA6B2>«Восстановить» ставит версию на место «{libVersionsOf.FileName}» с его идентификатором и названием; " +
               "текущий вариант сначала уходит в «История» — откат тоже можно отменить.</color>";
    }

    // ─────────── Документ редактора и файл ───────────
    private string OpenFilePath(LibraryKind k) => k == LibraryKind.Scenario ? sdoc?.FilePath ?? "" : doc?.FilePath ?? "";

    private static bool SameFile(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch (Exception) { return false; }
    }

    private bool IsOpen(LibraryEntry e) => e != null && SameFile(e.FilePath, OpenFilePath(e.Kind));

    // Файл открыт и изменён — сначала сохранить (иначе правки разойдутся с файлом)
    private void GuardOpenFile(LibraryEntry e, Action then)
    {
        bool dirty = IsOpen(e) && (e.Kind == LibraryKind.Scenario ? sdoc.Dirty : doc.Dirty);
        if (!dirty) { then(); return; }
        Dialog("Файл открыт и изменён", $"«{e.Name}» открыт в редакторе, есть несохранённые правки. Сохранить их перед этим?", null, true,
               ("Сохранить", MonitorUi.ButtonColor, () =>
               {
                   CloseDialog();
                   if (e.Kind == LibraryKind.Scenario) SaveScenario(false); else Save(false);
                   bool still = e.Kind == LibraryKind.Scenario ? sdoc != null && sdoc.Dirty : doc != null && doc.Dirty;
                   RescanLibrary();
                   if (!still) then();
               }),
               ("Отмена", MonitorUi.NeutralColor, CloseDialog));
    }

    // Открытый документ — заново из файла (после переименования или отката); null — закрыть (файл в корзине)
    private void ReloadOpenDocument(LibraryKind kind, string newPath)
    {
        bool wasOpen = Active(libSheet);
        LoadFolder();
        if (kind == LibraryKind.Scenario)
        {
            ScenarioDocument d = newPath != null ? ScenarioDocument.Open(newPath, Catalog, folderAreas, null) : null;
            if (d != null) SetScenarioDocument(d);
            else { sdoc = null; ShowPropsGroup(); mapDirty = true; mapData = null; }
        }
        else
        {
            AreaDocument d = newPath != null ? AreaDocument.Open(newPath, null) : null;
            if (d != null) SetDocument(d);
            else { doc = null; ShowPropsGroup(); mapDirty = true; mapData = null; }
        }
        if (wasOpen && !Active(libSheet))
        {
            // Смена режима закрыла листы — вернуть библиотеку на той же вкладке
            LibTab tab = libTab, back = libBackTab;
            int sel = libSel, top = libTop;
            OpenLibrary(tab == LibTab.Areas || tab == LibTab.Versions && back == LibTab.Areas);
            libTab = tab; libBackTab = back; libSel = sel; libTop = top;
            RescanLibrary();
        }
    }

    private void LibReport(LibraryResult r)
    {
        string col = r.Done ? "#E6EDF3" : "#FF7A7A";
        libMessageText = $"<color={col}>{r.Message}</color>";
        if (r.Done) Debug.Log("[Библиотека] " + r.Message);
        else Debug.LogWarning("[Библиотека] " + r.Message);
        LoadFolder();   // свои акватории и id — для «Новый сценарий», «Сохранить как»
    }

    // ─────────── Действия с файлом ───────────
    private void LibOpen()
    {
        LibraryEntry e = LibEntry;
        if (e == null || !e.Ok) return;
        string path = e.FilePath;
        var errors = new List<string>();
        if (e.Kind == LibraryKind.Scenario)
            GuardedScenario(() =>
            {
                LoadFolder();
                ScenarioDocument d = ScenarioDocument.Open(path, Catalog, folderAreas, errors);
                if (d == null) { LibError(errors); return; }
                CloseSheets();
                SetScenarioDocument(d);
            });
        else
            GuardedArea(() =>
            {
                AreaDocument d = AreaDocument.Open(path, errors);
                if (d == null) { LibError(errors); return; }
                CloseSheets();
                SetDocument(d);
            });
    }

    private void LibError(List<string> errors) =>
        Dialog("Не открылся", string.Join("\n", errors), null, true, ("Понятно", MonitorUi.NeutralColor, CloseDialog));

    private void LibCopy()
    {
        LibraryEntry e = LibEntry;
        if (e == null) return;
        LibraryResult r = ScenarioLibrary.Copy(Root, Catalog, libScan, e.Kind, e.FilePath);
        LibReport(r);
        if (r.Done) LibSelect(e.Kind, r.Path);
        FillLibrary();
    }

    private void LibRename()
    {
        LibraryEntry e = LibEntry;
        if (e == null || !e.Ok) return;
        GuardOpenFile(e, () =>
        {
            LibraryEntry cur = libScan.Of(e.Kind).Find(x => SameFile(x.FilePath, e.FilePath)) ?? e;
            if (cur.Kind == LibraryKind.Area)
                Dialog("Переименовать акваторию", $"Новое имя (и файла, и ссылки из сценариев; имя встроенной занимать нельзя). Своих сценариев на ней: {cur.UsedBy} — они не изменятся.",
                       cur.AreaName, true,
                       ("Переименовать", MonitorUi.StartColor, () => { string n = DialogInput(); CloseDialog(); DoRename(cur, n, "", false); }),
                       ("Отмена", MonitorUi.NeutralColor, CloseDialog));
            else
                Dialog("Переименовать сценарий: идентификатор", "Идентификатор (в записях и отчётах; файл — «id название.json»; id встроенного занимать нельзя):",
                       cur.Id, true,
                       ("Дальше", MonitorUi.ButtonColor, () =>
                       {
                           string id = DialogInput();
                           CloseDialog();
                           Dialog("Переименовать сценарий: название", $"Название сценария (идентификатор — {id.Trim()}):", cur.Title, true,
                                  ("Переименовать", MonitorUi.StartColor, () => { string t = DialogInput(); CloseDialog(); DoRename(cur, id, t, false); }),
                                  ("Отмена", MonitorUi.NeutralColor, CloseDialog));
                       }),
                       ("Отмена", MonitorUi.NeutralColor, CloseDialog));
        });
    }

    private void DoRename(LibraryEntry e, string a, string b, bool overwrite)
    {
        bool open = IsOpen(e);
        LibraryResult r = e.Kind == LibraryKind.Scenario
            ? ScenarioLibrary.RenameScenario(Root, Catalog, libScan, e, a, b, overwrite, DateTime.Now)
            : ScenarioLibrary.RenameArea(Root, Catalog, libScan, e, a, overwrite, DateTime.Now);
        if (r.Status == LibraryStatus.NameTaken)
        {
            Dialog("Имя занято", r.Message + " Заменить его? Прежний файл уйдёт в «История».", null, true,
                   ("Заменить", MonitorUi.WarnColor, () => { CloseDialog(); DoRename(e, a, b, true); }),
                   ("Отмена", MonitorUi.NeutralColor, CloseDialog));
            return;
        }
        LibReport(r);
        if (r.Done && open) ReloadOpenDocument(e.Kind, r.Path);
        if (r.Done) LibSelect(e.Kind, r.Path);
        FillLibrary();
    }

    private void LibTrashAsk()
    {
        LibraryEntry e = LibEntry;
        if (e == null) return;
        GuardOpenFile(e, () =>
            Dialog("В корзину", $"Перенести «{e.Name}» в корзину? Вернуть можно на вкладке «Корзина»" +
                                (IsOpen(e) ? "; в редакторе файл закроется." : "."), null, true,
                   ("В корзину", MonitorUi.StopColor, () =>
                   {
                       CloseDialog();
                       bool open = IsOpen(e);
                       LibraryResult r = ScenarioLibrary.Trash(Root, e, DateTime.Now);
                       LibReport(r);
                       if (r.Done && open) ReloadOpenDocument(e.Kind, null);
                       RescanLibrary();
                       FillLibrary();
                   }),
                   ("Отмена", MonitorUi.NeutralColor, CloseDialog)));
    }

    private void LibShowVersions()
    {
        LibraryEntry e = LibEntry;
        if (e == null) return;
        libVersionsOf = e;
        libBackTab = libTab;
        libTab = LibTab.Versions;
        libSel = 0;
        libTop = 0;
        RescanLibrary();
        FillLibrary();
    }

    private void LibRestoreVersionAsk()
    {
        HistoryVersion v = LibVersion;
        LibraryEntry e = libVersionsOf;
        if (v == null || e == null) return;
        GuardOpenFile(e, () =>
            Dialog("Восстановить версию", $"Поставить версию от {v.When:dd.MM.yyyy HH:mm:ss} на место «{e.FileName}»? Текущий вариант уйдёт в «История».", null, true,
                   ("Восстановить", MonitorUi.WarnColor, () =>
                   {
                       CloseDialog();
                       RescanLibrary();
                       LibraryEntry cur = libScan.Of(e.Kind).Find(x => SameFile(x.FilePath, e.FilePath)) ?? e;
                       bool open = IsOpen(cur);
                       LibraryResult r = ScenarioLibrary.RestoreVersion(Root, Catalog, libScan, cur, v, DateTime.Now);
                       LibReport(r);
                       if (r.Done && open) ReloadOpenDocument(cur.Kind, cur.FilePath);
                       libVersionsOf = cur;
                       RescanLibrary();
                       libSel = 0;
                       FillLibrary();
                   }),
                   ("Отмена", MonitorUi.NeutralColor, CloseDialog)));
    }

    private void LibNew()
    {
        if (libTab == LibTab.Areas) GuardedArea(() => { CloseSheets(); NewDialog(); });
        else GuardedScenario(() => { CloseSheets(); NewScenarioDialog(); });
    }

    private void LibFromBuiltIn()
    {
        if (libTab != LibTab.Areas) { GuardedScenario(() => { CloseSheets(); BasedOnDialog(); }); return; }
        GuardedArea(() =>
        {
            var buttons = new List<(string, Color, Action)>();
            foreach (WaterArea a in Catalog.Areas)
            {
                WaterArea src = a;
                buttons.Add(($"Встроенная «{a.name}» — копией", MonitorUi.ButtonColor, () => { CloseSheets(); CopyBuiltInAreaDialog(src); }));
            }
            buttons.Add(("Отмена", MonitorUi.NeutralColor, CloseDialog));
            Dialog("Копия встроенной акватории", "Встроенная не меняется — правится её копия.", null, false, buttons.ToArray());
        });
    }

    // ─────────── Корзина ───────────
    private void LibRestore(bool overwrite)
    {
        TrashEntry t = LibTrashSel;
        if (t == null) return;
        LibraryResult r = ScenarioLibrary.Restore(Root, t, overwrite, DateTime.Now);
        if (r.Status == LibraryStatus.NameTaken)
        {
            Dialog("Место занято", r.Message + " Заменить его (занявший файл уйдёт в «История») или восстановить копией с новым id / именем?", null, true,
                   ("Заменить", MonitorUi.WarnColor, () => { CloseDialog(); LibRestore(true); }),
                   ("Копией", MonitorUi.ButtonColor, () => { CloseDialog(); LibRestoreCopy(); }),
                   ("Отмена", MonitorUi.NeutralColor, CloseDialog));
            return;
        }
        LibReport(r);
        RescanLibrary();
        FillLibrary();
    }

    private void LibRestoreCopy()
    {
        TrashEntry t = LibTrashSel;
        if (t == null) return;
        LibReport(ScenarioLibrary.RestoreAsCopy(Root, Catalog, libScan, t));
        RescanLibrary();
        FillLibrary();
    }

    private void LibEmptyTrashAsk()
    {
        int n = libTrash.Count;
        if (n == 0) return;
        Dialog("Очистить корзину", $"Удалить насовсем файлов: {n}? Вернуть их будет нельзя (история версий остаётся в «История»).", null, true,
               ("Удалить насовсем", MonitorUi.StopColor, () =>
               {
                   CloseDialog();
                   try
                   {
                       int k = ScenarioLibrary.EmptyTrash(Root);
                       LibReport(LibraryResult.Of(LibraryStatus.Done, $"Корзина очищена: удалено файлов {k}."));
                   }
                   catch (Exception e) { LibReport(LibraryResult.Of(LibraryStatus.Failed, "Корзина не очищена: " + e.Message)); }
                   RescanLibrary();
                   FillLibrary();
               }),
               ("Отмена", MonitorUi.NeutralColor, CloseDialog));
    }

    // ─────────── Флешка ───────────
    private List<LibraryEntry> AllOwnOk()
    {
        var list = new List<LibraryEntry>();
        foreach (LibraryEntry e in libScan.Areas) if (e.Ok) list.Add(e);
        foreach (LibraryEntry e in libScan.Scenarios) if (e.Ok) list.Add(e);
        return list;
    }

    /// <summary>Выбор флешки: съёмные диски кнопками, «Путь вручную…». import — импорт, иначе экспорт entries.</summary>
    private void FlashDialog(bool import, List<LibraryEntry> entries)
    {
        List<FlashDrive> drives = ScenarioLibrary.RemovableDrives();
        var buttons = new List<(string, Color, Action)>();
        foreach (FlashDrive d in drives)
        {
            if (buttons.Count >= 5) break;
            string rootPath = d.Root;
            buttons.Add((d.Title, MonitorUi.ButtonColor, () => { CloseDialog(); FlashGo(import, entries, rootPath); }));
        }
        buttons.Add(("Путь вручную…", MonitorUi.NeutralColor, () =>
        {
            CloseDialog();
            string def = lastFlashPath.Length > 0 ? lastFlashPath : drives.Count > 0 ? drives[0].Root : "E:\\";
            Dialog(import ? "Импорт: папка" : "Экспорт: папка", "Диск или папка (например, E:\\ или D:\\Перенос):", def, true,
                   ("Дальше", MonitorUi.ButtonColor, () => { string p = (DialogInput() ?? "").Trim(); CloseDialog(); FlashGo(import, entries, p); }),
                   ("Отмена", MonitorUi.NeutralColor, CloseDialog));
        }));
        buttons.Add(("Обновить список дисков", MonitorUi.NeutralColor, () => { CloseDialog(); FlashDialog(import, entries); }));
        buttons.Add(("Отмена", MonitorUi.NeutralColor, CloseDialog));
        string what = import ? "Импорт с флешки" : entries != null && entries.Count == 1 ? $"Экспорт «{entries[0].Name}»" : $"Экспорт своих файлов ({entries?.Count ?? 0})";
        string text = (drives.Count == 0 ? "Съёмных дисков не найдено — вставьте флешку и нажмите «Обновить», или укажите путь. " : "") +
                      (import ? $"Ищутся файлы в корне и в «{ScenarioLibrary.FlashFolder}\\Мои сценарии | Мои акватории»; перед копированием — проверка."
                              : $"Файлы лягут в «<диск>\\{ScenarioLibrary.FlashFolder}\\Мои сценарии | Мои акватории».");
        Dialog(what, text, null, false, buttons.ToArray());
    }

    private void FlashGo(bool import, List<LibraryEntry> entries, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lastFlashPath = path;
        if (!import)
        {
            LibReport(ScenarioLibrary.Export(Root, entries ?? new List<LibraryEntry>(), path));
            FillLibrary();
            return;
        }
        RescanLibrary();
        ImportScanResult res = ScenarioLibrary.ImportScan(path, Root, Catalog, libScan);
        if (res.Error.Length > 0) { LibReport(LibraryResult.Of(LibraryStatus.Invalid, res.Error)); FillLibrary(); return; }
        int fresh = 0, conflicts = 0, same = 0, bad = 0;
        var lines = new StringBuilder($"На «{path}» найдено файлов {res.Items.Count}:\n");
        foreach (ImportItem i in res.Items)
        {
            if (!i.Ok) bad++;
            else if (i.Same) same++;
            else if (i.Conflict == ImportConflict.None) fresh++;
            else conflicts++;
            lines.AppendLine($"  {(i.Ok ? i.Same ? "=" : i.Conflict == ImportConflict.None ? "+" : "!" : "×")} {i.FileName} — {ScenarioLibrary.ConflictText(i)}");
        }
        libMessageText = lines.ToString().TrimEnd();
        FillLibrary();
        if (fresh + conflicts == 0)
        {
            Dialog("Импорт с флешки", res.Items.Count == 0 ? "Файлов тренажёра не найдено." : $"Импортировать нечего: уже есть {same}, с ошибками {bad} (список — внизу листа).",
                   null, true, ("Понятно", MonitorUi.NeutralColor, CloseDialog));
            return;
        }
        Dialog("Импорт с флешки", $"Новых {fresh}, с совпадением имени {conflicts} (спрошу по каждому), уже есть {same}, с ошибками {bad} — не импортируются. Список — внизу листа.",
               null, true,
               ("Импортировать", MonitorUi.StartColor, () => { CloseDialog(); ImportNext(res.Items, 0, new List<string>(), path); }),
               ("Отмена", MonitorUi.NeutralColor, CloseDialog));
    }

    // По одному: новые — сразу, совпадение — вопрос «Заменить / Копией / Пропустить»
    private void ImportNext(List<ImportItem> items, int i, List<string> log, string source)
    {
        for (; i < items.Count; i++)
        {
            ImportItem it = items[i];
            if (!it.Ok) { log.Add($"× {it.FileName}: с ошибками — не импортирован"); continue; }
            if (it.Same) { log.Add($"= {it.FileName}: уже есть такой же"); continue; }
            if (it.Conflict == ImportConflict.None) { ImportOne(it, ImportMode.Normal, log); continue; }
            int next = i + 1;
            ImportItem cur = it;
            var buttons = new List<(string, Color, Action)>();
            if (!ScenarioLibrary.CopyOnly(cur.Conflict))
                buttons.Add(("Заменить (прежний — в «История»)", MonitorUi.WarnColor, () => { CloseDialog(); ImportOne(cur, ImportMode.Replace, log); ImportNext(items, next, log, source); }));
            buttons.Add(("Копией (новый id / имя)", MonitorUi.ButtonColor, () => { CloseDialog(); ImportOne(cur, ImportMode.Copy, log); ImportNext(items, next, log, source); }));
            buttons.Add(("Пропустить", MonitorUi.NeutralColor, () => { CloseDialog(); log.Add($"– {cur.FileName}: пропущен"); ImportNext(items, next, log, source); }));
            buttons.Add(("Пропустить остальные", MonitorUi.NeutralColor, () =>
            {
                CloseDialog();
                for (int k = next - 1; k < items.Count; k++) log.Add($"– {items[k].FileName}: пропущен");
                ImportFinish(log, source);
            }));
            Dialog("Импорт: совпадение", $"{cur.FileName} — {ScenarioLibrary.KindTitle(cur.Kind)} «{cur.Name}»: {ScenarioLibrary.ConflictText(cur)}.", null, false, buttons.ToArray());
            return;
        }
        ImportFinish(log, source);
    }

    private void ImportOne(ImportItem it, ImportMode mode, List<string> log)
    {
        RescanLibrary();   // id и имена — с учётом уже импортированных
        LibraryResult r = ScenarioLibrary.Import(Root, Catalog, libScan, it, mode, DateTime.Now);
        log.Add((r.Done ? "+ " : "× ") + r.Message);
        Debug.Log("[Библиотека] Импорт: " + r.Message);
    }

    private void ImportFinish(List<string> log, string source)
    {
        libMessageText = $"Импорт с «{source}»:\n" + string.Join("\n", log);
        Debug.Log($"[Библиотека] Импорт с «{source}» закончен: {log.Count} файлов — " + string.Join("; ", log));
        LoadFolder();
        RescanLibrary();
        FillLibrary();
    }
}
