// ============================================================
// SessionStorage.cs — Папка сессий и ротация (C1)
//
// Сессия — папка с session.json формата VRBridge.Session. Имена папок
// начинаются с даты и времени (yyyyMMdd_HHmmss), поэтому порядок по имени —
// порядок по времени.
//
// Ротация: перед новой записью удаляются самые старые сессии, чтобы вместе
// с новой их было не больше maxSessions. Сессии с меткой «keep» (файл keep
// в папке; кнопка «Сохранить» в окне Replay) не удаляются и в лимит не входят.
// maxSessions = 0 — без ротации (режим педагогического эксперимента).
// Удаляются только папки, в которых есть session.json нашего формата.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;

public static class SessionStorage
{
    /// <summary>Папки сессий в корне, новые первыми.</summary>
    public static List<string> List(string root)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return list;
        foreach (string dir in Directory.GetDirectories(root))
            if (IsSession(dir)) list.Add(dir);
        list.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)));
        return list;
    }

    public static bool IsSession(string dir)
    {
        string meta = Path.Combine(dir, SessionFormat.MetaFile);
        if (!File.Exists(meta)) return false;
        try
        {
            using (var r = new StreamReader(meta))
            {
                char[] head = new char[256];
                int n = r.Read(head, 0, head.Length);
                return new string(head, 0, n).Contains(SessionFormat.FormatName);
            }
        }
        catch (IOException) { return false; }
    }

    public static bool IsKept(string dir) => File.Exists(Path.Combine(dir, SessionFormat.KeepFile));

    public static void SetKept(string dir, bool keep)
    {
        string f = Path.Combine(dir, SessionFormat.KeepFile);
        if (keep) File.WriteAllText(f, "Сессия не удаляется при ротации (VR Bridge, Session Replay).\n");
        else if (File.Exists(f)) File.Delete(f);
    }

    /// <summary>
    /// Освободить место под новую сессию: удалить самые старые незащищённые,
    /// чтобы их осталось не больше maxSessions − 1. Возвращает удалённые папки.
    /// </summary>
    public static List<string> Rotate(string root, int maxSessions)
    {
        var removed = new List<string>();
        if (maxSessions <= 0) return removed;
        List<string> rotating = List(root).FindAll(d => !IsKept(d));
        for (int i = rotating.Count - 1; i >= maxSessions - 1 && i >= 0; i--)
        {
            try
            {
                Directory.Delete(rotating[i], true);
                removed.Add(rotating[i]);
            }
            catch (Exception) { /* папка открыта (Excel) — удалим в следующий раз */ }
        }
        return removed;
    }

    /// <summary>Новая папка сессии (если имя занято — суффикс _2, _3…).</summary>
    public static string CreateFolder(string root, string name)
    {
        Directory.CreateDirectory(root);
        string dir = Path.Combine(root, name);
        for (int k = 2; Directory.Exists(dir); k++) dir = Path.Combine(root, $"{name}_{k}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
