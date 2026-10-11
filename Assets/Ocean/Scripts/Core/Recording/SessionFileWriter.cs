// ============================================================
// SessionFileWriter.cs — Запись файлов сессии в фоновом потоке (C1)
//
// Главный поток только ставит куски текста в очередь (Append — дописать,
// Replace — заменить файл целиком). Диск — в отдельном фоновом потоке,
// чтобы запись не задерживала кадр и шаг модели.
//   • Файл создаётся с BOM (UTF-8), дальше дописывается без BOM.
//   • Каждый кусок — открыть, дописать, закрыть: всё, что отдано писателю,
//     окажется на диске и при аварийном завершении приложения.
//   • Replace — через временный файл, чтобы session.json не остался
//     недописанным.
//   • Flush(timeout) ждёт, пока очередь опустеет (конец упражнения, выход).
// background = false — запись сразу в вызывающем потоке (тесты, запасной режим).
// ============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

public sealed class SessionFileWriter : IDisposable
{
    private struct Item
    {
        public string File;
        public string Text;
        public bool Replace;
    }

    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

    public string Folder { get; }
    public bool Background { get; }
    /// <summary>Последняя ошибка записи (null — ошибок не было).</summary>
    public Exception LastError { get; private set; }
    public long BytesWritten => Interlocked.Read(ref bytesWritten);
    /// <summary>Наибольшее время одной записи на диск, мс (фоновый поток).</summary>
    public double MaxWriteMs { get; private set; }

    private readonly Queue<Item> queue = new Queue<Item>();
    private readonly object sync = new object();
    private readonly AutoResetEvent wake = new AutoResetEvent(false);
    private readonly ManualResetEvent idle = new ManualResetEvent(true);
    private readonly Thread thread;
    private volatile bool stopping;
    private long bytesWritten;

    public SessionFileWriter(string folder, bool background = true)
    {
        Folder = folder;
        Background = background;
        Directory.CreateDirectory(folder);
        if (!background) return;
        thread = new Thread(Run) { IsBackground = true, Name = "VRBridge.SessionWriter" };
        thread.Start();
    }

    /// <summary>Дописать текст в файл (файл создаётся с BOM).</summary>
    public void Append(string file, string text) => Enqueue(new Item { File = file, Text = text, Replace = false });

    /// <summary>Заменить файл целиком (UTF-8 с BOM).</summary>
    public void Replace(string file, string text) => Enqueue(new Item { File = file, Text = text, Replace = true });

    private void Enqueue(Item item)
    {
        if (string.IsNullOrEmpty(item.Text) && !item.Replace) return;
        if (!Background)
        {
            Write(item);
            return;
        }
        lock (sync)
        {
            queue.Enqueue(item);
            idle.Reset();
        }
        wake.Set();
    }

    /// <summary>Дождаться записи всего, что поставлено в очередь. false — не успели за timeout.</summary>
    public bool Flush(int timeoutMs = 2000) => !Background || idle.WaitOne(timeoutMs);

    /// <summary>Записать остаток и остановить поток.</summary>
    public bool Close(int timeoutMs = 2000)
    {
        bool done = Flush(timeoutMs);
        if (Background && !stopping)
        {
            stopping = true;
            wake.Set();
            thread.Join(Math.Max(100, timeoutMs));
        }
        return done;
    }

    public void Dispose() => Close();

    private void Run()
    {
        while (true)
        {
            wake.WaitOne(500);
            while (true)
            {
                Item item;
                lock (sync)
                {
                    if (queue.Count == 0)
                    {
                        idle.Set();
                        break;
                    }
                    item = queue.Dequeue();
                }
                Write(item);
            }
            if (stopping) return;
        }
    }

    private void Write(Item item)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            string path = Path.Combine(Folder, item.File);
            byte[] data = Utf8NoBom.GetBytes(item.Text ?? "");
            if (item.Replace)
            {
                string tmp = path + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    fs.Write(Bom, 0, Bom.Length);
                    fs.Write(data, 0, data.Length);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                Interlocked.Add(ref bytesWritten, data.Length + Bom.Length);
            }
            else
            {
                using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    if (fs.Length == 0) { fs.Write(Bom, 0, Bom.Length); Interlocked.Add(ref bytesWritten, Bom.Length); }
                    fs.Write(data, 0, data.Length);
                }
                Interlocked.Add(ref bytesWritten, data.Length);
            }
        }
        catch (Exception e) { LastError = e; }
        double ms = sw.Elapsed.TotalMilliseconds;
        if (ms > MaxWriteMs) MaxWriteMs = ms;
    }
}
