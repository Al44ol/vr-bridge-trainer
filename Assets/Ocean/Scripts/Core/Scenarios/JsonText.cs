// ============================================================
// JsonText.cs — Разбор и запись JSON для файлов сценариев и акваторий (запрос 21.1)
//
// Свой маленький JSON вместо JsonUtility: тот пишет ссылки на ассеты как instanceID и
// перечисления числами, а вне Unity (тесты Core) ведёт себя иначе. Здесь — одинаково везде:
//   • значение — объект (порядок полей сохраняется), массив, строка, число (хранится текстом,
//     чтобы float/double шли бит в бит), true/false, null;
//   • разбор с номером строки и столбца в сообщении об ошибке;
//   • запись с отступом 2 пробела, кириллица — как есть (файл UTF-8, читается в Блокноте).
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public enum JsonKind { Null, Bool, Number, String, Array, Object }

public sealed class JsonValue
{
    public JsonKind Kind { get; private set; }
    /// <summary>Строка — значение; число — текст числа (InvariantCulture).</summary>
    public string Text { get; private set; }
    public bool Bool { get; private set; }
    public readonly List<JsonValue> Items = new List<JsonValue>();
    public readonly List<KeyValuePair<string, JsonValue>> Fields = new List<KeyValuePair<string, JsonValue>>();
    /// <summary>Строка исходного текста (1…), где начинается значение; 0 — значение создано кодом.</summary>
    public int Line { get; private set; }

    private JsonValue(JsonKind kind) { Kind = kind; }

    public static readonly JsonValue Null = new JsonValue(JsonKind.Null);
    public static JsonValue Of(bool b) => new JsonValue(JsonKind.Bool) { Bool = b };
    public static JsonValue Of(string s) => s == null ? Null : new JsonValue(JsonKind.String) { Text = s };
    public static JsonValue Number(string text) => new JsonValue(JsonKind.Number) { Text = text };
    public static JsonValue Of(double d) => Number(d.ToString("R", CultureInfo.InvariantCulture));
    public static JsonValue Of(float f) => Number(f.ToString("R", CultureInfo.InvariantCulture));
    public static JsonValue Of(long n) => Number(n.ToString(CultureInfo.InvariantCulture));
    public static JsonValue NewArray() => new JsonValue(JsonKind.Array);
    public static JsonValue NewObject() => new JsonValue(JsonKind.Object);

    public bool IsNull => Kind == JsonKind.Null;

    /// <summary>Поле объекта (null — нет поля или не объект).</summary>
    public JsonValue this[string key]
    {
        get
        {
            if (Kind != JsonKind.Object) return null;
            foreach (KeyValuePair<string, JsonValue> f in Fields) if (f.Key == key) return f.Value;
            return null;
        }
    }

    public JsonValue Add(string key, JsonValue v)
    {
        Fields.Add(new KeyValuePair<string, JsonValue>(key, v ?? Null));
        return this;
    }

    public JsonValue Add(JsonValue v)
    {
        Items.Add(v ?? Null);
        return this;
    }

    public string Describe()
    {
        switch (Kind)
        {
            case JsonKind.Null: return "null";
            case JsonKind.Bool: return Bool ? "true" : "false";
            case JsonKind.Number: return "число";
            case JsonKind.String: return "строка";
            case JsonKind.Array: return "массив";
            default: return "объект";
        }
    }

    // ─────────── Запись ───────────
    public override string ToString()
    {
        var b = new StringBuilder();
        Write(b, 0);
        return b.ToString();
    }

    private void Write(StringBuilder b, int indent)
    {
        switch (Kind)
        {
            case JsonKind.Null: b.Append("null"); break;
            case JsonKind.Bool: b.Append(Bool ? "true" : "false"); break;
            case JsonKind.Number: b.Append(Text); break;
            case JsonKind.String: WriteString(b, Text); break;
            case JsonKind.Array:
                if (Items.Count == 0) { b.Append("[]"); break; }
                // Короткие массивы чисел (позиция и т. п.) — в одну строку
                if (Items.TrueForAll(i => i.Kind == JsonKind.Number) && Items.Count <= 4)
                {
                    b.Append('[');
                    for (int i = 0; i < Items.Count; i++) { if (i > 0) b.Append(", "); b.Append(Items[i].Text); }
                    b.Append(']');
                    break;
                }
                b.Append("[\n");
                for (int i = 0; i < Items.Count; i++)
                {
                    b.Append(' ', indent + 2);
                    Items[i].Write(b, indent + 2);
                    b.Append(i + 1 < Items.Count ? ",\n" : "\n");
                }
                b.Append(' ', indent).Append(']');
                break;
            default:
                if (Fields.Count == 0) { b.Append("{}"); break; }
                b.Append("{\n");
                for (int i = 0; i < Fields.Count; i++)
                {
                    b.Append(' ', indent + 2);
                    WriteString(b, Fields[i].Key);
                    b.Append(": ");
                    Fields[i].Value.Write(b, indent + 2);
                    b.Append(i + 1 < Fields.Count ? ",\n" : "\n");
                }
                b.Append(' ', indent).Append('}');
                break;
        }
    }

    private static void WriteString(StringBuilder b, string s)
    {
        b.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default:
                    if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
                    else b.Append(c);
                    break;
            }
        }
        b.Append('"');
    }

    // ─────────── Разбор ───────────
    /// <summary>Разобрать текст; ошибка — исключение FormatException «строка N, столбец M: …».</summary>
    public static JsonValue Parse(string text)
    {
        var p = new Parser(text ?? "");
        p.SkipSpace();
        JsonValue v = p.Value();
        p.SkipSpace();
        if (!p.End) throw p.Error("после конца данных лишний текст");
        return v;
    }

    private sealed class Parser
    {
        private readonly string s;
        private int i, line = 1, lineStart;

        public Parser(string text)
        {
            s = text;
            if (s.Length > 0 && s[0] == '﻿') i = 1;   // BOM
        }

        public bool End => i >= s.Length;

        public FormatException Error(string what) =>
            new FormatException($"строка {line}, столбец {i - lineStart + 1}: {what}");

        public void SkipSpace()
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\n') { line++; i++; lineStart = i; }
                else if (c == ' ' || c == '\t' || c == '\r') i++;
                else break;
            }
        }

        public JsonValue Value()
        {
            if (End) throw Error("данные оборвались");
            int at = line;
            char c = s[i];
            JsonValue v;
            if (c == '{') v = ObjectValue();
            else if (c == '[') v = ArrayValue();
            else if (c == '"') v = Of(StringValue());
            else if (c == '-' || c >= '0' && c <= '9') v = NumberValue();
            else if (Word("true")) v = Of(true);
            else if (Word("false")) v = Of(false);
            else if (Word("null")) return Null;
            else throw Error($"неожиданный символ «{c}»");
            v.Line = at;
            return v;
        }

        private bool Word(string w)
        {
            if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
            i += w.Length;
            return true;
        }

        private JsonValue ObjectValue()
        {
            var o = NewObject();
            i++;
            SkipSpace();
            if (!End && s[i] == '}') { i++; return o; }
            while (true)
            {
                SkipSpace();
                if (End || s[i] != '"') throw Error("ожидалось имя поля в кавычках");
                string key = StringValue();
                SkipSpace();
                if (End || s[i] != ':') throw Error($"после имени «{key}» ожидалось «:»");
                i++;
                SkipSpace();
                o.Add(key, Value());
                SkipSpace();
                if (End) throw Error("объект не закрыт «}»");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return o; }
                throw Error("ожидалось «,» или «}» (пропущена запятая?)");
            }
        }

        private JsonValue ArrayValue()
        {
            var a = NewArray();
            i++;
            SkipSpace();
            if (!End && s[i] == ']') { i++; return a; }
            while (true)
            {
                SkipSpace();
                a.Add(Value());
                SkipSpace();
                if (End) throw Error("массив не закрыт «]»");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return a; }
                throw Error("ожидалось «,» или «]» (пропущена запятая?)");
            }
        }

        private string StringValue()
        {
            var b = new StringBuilder();
            i++;
            while (true)
            {
                if (End) throw Error("строка не закрыта кавычкой");
                char c = s[i++];
                if (c == '"') return b.ToString();
                if (c == '\n') throw Error("перенос строки внутри строки (нужно \\n)");
                if (c != '\\') { b.Append(c); continue; }
                if (End) throw Error("строка оборвалась после «\\»");
                char e = s[i++];
                switch (e)
                {
                    case '"': b.Append('"'); break;
                    case '\\': b.Append('\\'); break;
                    case '/': b.Append('/'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'n': b.Append('\n'); break;
                    case 'r': b.Append('\r'); break;
                    case 't': b.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            throw Error("неверная последовательность \\u");
                        b.Append((char)code);
                        i += 4;
                        break;
                    default: throw Error($"неизвестная последовательность «\\{e}»");
                }
            }
        }

        private JsonValue NumberValue()
        {
            int start = i;
            if (s[i] == '-') i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
            string t = s.Substring(start, i - start);
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                throw Error($"неверное число «{t}» (дробная часть — через точку)");
            return Number(t);
        }
    }
}
