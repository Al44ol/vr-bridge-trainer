// ============================================================
// EditHistory.cs — Отмена и повтор правок редактора (запрос 21.2а)
//
// Состояние документа — текст файла (ScenarioFile.WriteArea / Write): снимок после каждой
// законченной правки (отпустили мышь, закончили ввод поля). Отмена — вернуть прежний снимок,
// повтор — следующий. Снимок совпал с текущим — не пишется. Глубина — Limit шагов.
// ============================================================
using System.Collections.Generic;

public sealed class EditHistory
{
    public int Limit = 100;
    private readonly List<string> states = new List<string>();
    private int index = -1;

    public string Current => index >= 0 ? states[index] : null;
    public bool CanUndo => index > 0;
    public bool CanRedo => index >= 0 && index + 1 < states.Count;
    public int Count => states.Count;

    /// <summary>Начать заново (открыли или создали документ).</summary>
    public void Reset(string state)
    {
        states.Clear();
        states.Add(state);
        index = 0;
    }

    /// <summary>Правка закончена: новое состояние (то же — ничего). Повторы после отмены теряются.</summary>
    public bool Push(string state)
    {
        if (index >= 0 && states[index] == state) return false;
        if (index + 1 < states.Count) states.RemoveRange(index + 1, states.Count - index - 1);
        states.Add(state);
        index = states.Count - 1;
        if (states.Count > Limit)
        {
            states.RemoveAt(0);
            index--;
        }
        return true;
    }

    public string Undo() => CanUndo ? states[--index] : null;
    public string Redo() => CanRedo ? states[++index] : null;
}
