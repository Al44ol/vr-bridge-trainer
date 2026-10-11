// ============================================================
// CsvFormat.cs — Формат CSV записи упражнения (C1)
//
// Правила проекта (CLAUDE.md): числа — CultureInfo.InvariantCulture (точка),
// разделитель «;», UTF-8 с BOM, первая строка — заголовки с единицами.
// Текст — по RFC 4180: поле в кавычках, если в нём есть «;», кавычка или
// перевод строки; кавычка удваивается. Переводы строк в тексте заменяются
// пробелом (одна запись — одна строка файла). NaN и бесконечность — пустая
// ячейка (Python читает как NaN, Excel — пусто).
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public static class CsvFormat
{
    public const char Separator = ';';
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] Formats = { "0", "0.0", "0.00", "0.000", "0.0000", "0.00000", "0.000000" };

    /// <summary>Число с заданным числом знаков после точки (NaN — пусто).</summary>
    public static void Num(StringBuilder b, double v, int decimals)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return;
        b.Append(v.ToString(Formats[Math.Max(0, Math.Min(Formats.Length - 1, decimals))], Inv));
    }

    public static void Int(StringBuilder b, int v) => b.Append(v.ToString(Inv));

    /// <summary>Текстовое поле (в кавычках при необходимости).</summary>
    public static void Text(StringBuilder b, string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        if (s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0) s = s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        if (s.IndexOf(Separator) < 0 && s.IndexOf('"') < 0)
        {
            b.Append(s);
            return;
        }
        b.Append('"').Append(s.Replace("\"", "\"\"")).Append('"');
    }

    public static void Sep(StringBuilder b) => b.Append(Separator);

    /// <summary>Разбор строки CSV на поля (кавычки RFC 4180).</summary>
    public static List<string> Split(string line)
    {
        var fields = new List<string>();
        if (line == null) return fields;
        var f = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { f.Append('"'); i++; }
                    else quoted = false;
                }
                else f.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == Separator) { fields.Add(f.ToString()); f.Clear(); }
            else f.Append(c);
        }
        fields.Add(f.ToString());
        return fields;
    }

    /// <summary>Число из ячейки (пусто или ошибка — NaN).</summary>
    public static double ParseDouble(string s) =>
        !string.IsNullOrEmpty(s) && double.TryParse(s, NumberStyles.Float, Inv, out double v) ? v : double.NaN;

    /// <summary>Целое из ячейки (пусто или ошибка — fallback).</summary>
    public static int ParseInt(string s, int fallback = -1) =>
        !string.IsNullOrEmpty(s) && int.TryParse(s, NumberStyles.Integer, Inv, out int v) ? v : fallback;
}
