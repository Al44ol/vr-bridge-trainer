// ============================================================
// FieldJson.cs — Поля объекта ↔ JSON по правилам сериализации Unity (запрос 21.1)
//
// Пишутся те же поля, что Unity сохраняет в ассете: открытые поля экземпляра и [SerializeField],
// кроме static / const / readonly / [NonSerialized]; свойства — нет. Поэтому новое поле
// ScenarioDefinition или WaterArea попадает в файл само, без правки формата, и файл ↔ ассет
// сходятся бит в бит.
//   • float, double — текстом "R" (точно), int, bool, string;
//   • перечисление — словом ("Moderate"; флаги — "Arrival, Grounding"), при чтении — и числом;
//   • WorldPosition — [восток, север];
//   • List<T>, T[] — массив; класс/структура [Serializable] — объект;
//   • ссылка на ассет (ScriptableObject: судно, набор критериев, акватория) — имя ассета строкой;
//     разрешение имён — снаружи (ScenarioFile), через делегаты.
// Чтение: отсутствующее поле — значение по умолчанию; неизвестное — предупреждение; неверный тип — ошибка.
// ============================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

public sealed class FieldJson
{
    /// <summary>Ссылка на ассет → имя (null — поле пустое).</summary>
    public Func<UnityEngine.Object, string> RefName = o => o != null ? o.name : null;
    /// <summary>Имя → ассет нужного типа (null — не найден: ошибка).</summary>
    public Func<Type, string, UnityEngine.Object> Resolve = (t, n) => null;
    /// <summary>Известные имена данного типа — в сообщение об ошибке (null — не перечислять).</summary>
    public Func<Type, string> Known;

    public readonly List<string> Errors = new List<string>();
    public readonly List<string> Warnings = new List<string>();

    // ─────────── Поля ───────────
    /// <summary>Сериализуемые поля типа (от базового к производному, в порядке объявления), как в Unity.</summary>
    public static List<FieldInfo> Fields(Type t)
    {
        var chain = new List<Type>();
        for (Type x = t; x != null && x != typeof(object) && x != typeof(ScriptableObject) && x != typeof(UnityEngine.Object); x = x.BaseType)
            chain.Insert(0, x);
        var list = new List<FieldInfo>();
        foreach (Type x in chain)
            foreach (FieldInfo f in x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.IsInitOnly || f.IsLiteral || f.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                if (!f.IsPublic && !f.IsDefined(typeof(SerializeField), false)) continue;
                if (f.Name.Contains("<")) continue;   // поля автосвойств
                list.Add(f);
            }
        return list;
    }

    private static bool IsAsset(Type t) => typeof(UnityEngine.Object).IsAssignableFrom(t);

    // ─────────── Запись ───────────
    /// <summary>Объект (класс [Serializable] или ScriptableObject) → JSON-объект.</summary>
    public JsonValue Write(object o)
    {
        JsonValue r = JsonValue.NewObject();
        foreach (FieldInfo f in Fields(o.GetType())) r.Add(f.Name, WriteValue(f.FieldType, f.GetValue(o), f.Name));
        return r;
    }

    private JsonValue WriteValue(Type t, object v, string path)
    {
        if (IsAsset(t)) return JsonValue.Of(RefName((UnityEngine.Object)v));
        if (t == typeof(string)) return JsonValue.Of((string)v ?? "");
        if (t == typeof(float)) return Num((float)v);
        if (t == typeof(double)) return Num((double)v);
        if (t == typeof(int)) return JsonValue.Of((long)(int)v);
        if (t == typeof(long)) return JsonValue.Of((long)v);
        if (t == typeof(bool)) return JsonValue.Of((bool)v);
        if (t.IsEnum) return JsonValue.Of(v.ToString());
        if (t == typeof(WorldPosition))
        {
            var p = (WorldPosition)v;
            return JsonValue.NewArray().Add(Num(p.East)).Add(Num(p.North));
        }
        Type elem = ElementType(t);
        if (elem != null)
        {
            JsonValue a = JsonValue.NewArray();
            if (v != null) foreach (object x in (IEnumerable)v) a.Add(WriteValue(elem, x, path + "[]"));
            return a;
        }
        if (t.IsClass || t.IsValueType && !t.IsPrimitive)
        {
            if (v == null) v = Activator.CreateInstance(t);   // как Unity: пустой класс сериализуется значениями по умолчанию
            return Write(v);
        }
        throw new NotSupportedException($"FieldJson: тип {t.Name} поля {path} не поддерживается");
    }

    // NaN и бесконечность — строкой (в JSON их нет)
    private static JsonValue Num(double d) => double.IsNaN(d) || double.IsInfinity(d) ? JsonValue.Of(d.ToString(CultureInfo.InvariantCulture)) : JsonValue.Of(d);
    private static JsonValue Num(float f) => float.IsNaN(f) || float.IsInfinity(f) ? JsonValue.Of(f.ToString(CultureInfo.InvariantCulture)) : JsonValue.Of(f);

    private static Type ElementType(Type t)
    {
        if (t.IsArray) return t.GetElementType();
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return t.GetGenericArguments()[0];
        return null;
    }

    // ─────────── Чтение ───────────
    /// <summary>JSON-объект → поля существующего объекта (отсутствующие поля не трогаются). where — путь для сообщений.</summary>
    public void Read(JsonValue json, object target, string where)
    {
        if (json == null || json.Kind != JsonKind.Object) { Errors.Add($"{where}: ожидался объект, а не {json?.Describe() ?? "ничего"}"); return; }
        List<FieldInfo> fields = Fields(target.GetType());
        foreach (KeyValuePair<string, JsonValue> kv in json.Fields)
        {
            FieldInfo f = fields.Find(x => x.Name == kv.Key);
            if (f == null) { Warnings.Add($"{where}: неизвестное поле «{kv.Key}» (строка {kv.Value.Line}) — пропущено"); continue; }
            if (ReadValue(f.FieldType, kv.Value, f.GetValue(target), $"{where}.{kv.Key}", out object v)) f.SetValue(target, v);
        }
    }

    private bool ReadValue(Type t, JsonValue j, object current, string path, out object v)
    {
        v = null;
        string at = j.Line > 0 ? $" (строка {j.Line})" : "";
        if (IsAsset(t))
        {
            if (j.IsNull || j.Kind == JsonKind.String && string.IsNullOrWhiteSpace(j.Text)) return true;
            if (j.Kind != JsonKind.String) return Bad(path, "имя строкой", j, at);
            v = Resolve(t, j.Text);
            if (v == null)
            {
                string known = Known?.Invoke(t);
                Errors.Add($"{path}: «{j.Text}» — {AssetKind(t)}{at}" + (string.IsNullOrEmpty(known) ? "" : $"; есть: {known}"));
                return false;
            }
            return true;
        }
        if (t == typeof(string))
        {
            if (j.IsNull) { v = ""; return true; }
            if (j.Kind != JsonKind.String) return Bad(path, "строка", j, at);
            v = j.Text;
            return true;
        }
        if (t == typeof(float) || t == typeof(double) || t == typeof(int) || t == typeof(long))
        {
            string text = j.Kind == JsonKind.Number || j.Kind == JsonKind.String ? j.Text : null;
            if (text == null || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return Bad(path, "число", j, at);
            if (t == typeof(float)) v = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            else if (t == typeof(double)) v = d;
            else
            {
                if (Math.Abs(d - Math.Round(d)) > 1e-9) { Errors.Add($"{path}: нужно целое число, а не {text}{at}"); return false; }
                if (t == typeof(int)) v = (int)Math.Round(d); else v = (long)Math.Round(d);
            }
            return true;
        }
        if (t == typeof(bool))
        {
            if (j.Kind != JsonKind.Bool) return Bad(path, "true или false", j, at);
            v = j.Bool;
            return true;
        }
        if (t.IsEnum)
        {
            if (j.Kind == JsonKind.Number && long.TryParse(j.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)) { v = Enum.ToObject(t, n); return true; }
            if (j.Kind != JsonKind.String) return Bad(path, "слово из списка", j, at);
            try { v = Enum.Parse(t, j.Text.Trim(), false); return true; }
            catch (ArgumentException)
            {
                Errors.Add($"{path}: «{j.Text}» — нет такого значения; допустимо: {string.Join(", ", Enum.GetNames(t))}{at}");
                return false;
            }
        }
        if (t == typeof(WorldPosition))
        {
            if (j.Kind != JsonKind.Array || j.Items.Count != 2 || j.Items[0].Kind != JsonKind.Number || j.Items[1].Kind != JsonKind.Number)
                return Bad(path, "[восток, север] в метрах", j, at);
            v = new WorldPosition(double.Parse(j.Items[0].Text, NumberStyles.Float, CultureInfo.InvariantCulture),
                                  double.Parse(j.Items[1].Text, NumberStyles.Float, CultureInfo.InvariantCulture));
            return true;
        }
        Type elem = ElementType(t);
        if (elem != null)
        {
            if (j.IsNull) j = JsonValue.NewArray();
            if (j.Kind != JsonKind.Array) return Bad(path, "массив [ … ]", j, at);
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elem));
            bool ok = true;
            for (int k = 0; k < j.Items.Count; k++)
            {
                object init = elem.IsClass && elem != typeof(string) && !IsAsset(elem) ? Activator.CreateInstance(elem) : null;
                if (ReadValue(elem, j.Items[k], init, $"{path}[{k}]", out object x)) list.Add(x);
                else ok = false;
            }
            if (t.IsArray)
            {
                Array arr = Array.CreateInstance(elem, list.Count);
                list.CopyTo(arr, 0);
                v = arr;
            }
            else v = list;
            return ok;
        }
        if (t.IsClass || t.IsValueType && !t.IsPrimitive)
        {
            // Класс [Serializable]: поверх значений по умолчанию (а не текущих) — как при загрузке ассета
            object o = Activator.CreateInstance(t);
            if (j.IsNull) { v = o; return true; }
            int before = Errors.Count;
            Read(j, o, path);
            v = o;
            return Errors.Count == before;
        }
        Errors.Add($"{path}: тип {t.Name} не поддерживается");
        return false;
    }

    private bool Bad(string path, string want, JsonValue j, string at)
    {
        Errors.Add($"{path}: нужно {want}, а не {j.Describe()}{at}");
        return false;
    }

    private static string AssetKind(Type t)
    {
        if (t == typeof(ShipParameters)) return "нет такого судна";
        if (t == typeof(WaterArea)) return "нет такой акватории";
        if (t == typeof(AssessmentCriteria)) return "нет такого набора оценки";
        return "нет такого: " + t.Name;
    }
}
