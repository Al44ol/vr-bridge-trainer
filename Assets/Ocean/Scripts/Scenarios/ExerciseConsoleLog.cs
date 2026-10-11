// ============================================================
// ExerciseConsoleLog.cs — Вывод событий упражнения в консоль (B2)
//
// Первый потребитель шины ExerciseEvents: подписан только на шину и ничего
// не знает об источниках (маршрут, мели, в будущем — цели и вводные).
// Так же будут подключены запись (C1), оценка (C2) и тактильный отклик (D3).
// ============================================================
using UnityEngine;

public class ExerciseConsoleLog : MonoBehaviour
{
    [Tooltip("Выводить и события вне упражнения (свободное плавание)")]
    public bool logOutsideExercise = true;

    void OnEnable() => ExerciseEvents.Raised += Log;
    void OnDisable() => ExerciseEvents.Raised -= Log;

    private void Log(ExerciseEvent e)
    {
        if (!e.InExercise && !logOutsideExercise
            && e.Type != ExerciseEventType.ScenarioLoaded && e.Type != ExerciseEventType.ExerciseEnded) return;

        string text = (e.InExercise ? "[Упражнение] " : "[Вне упражнения] ") + e;
        switch (e.Type)
        {
            case ExerciseEventType.CorridorExit:
            case ExerciseEventType.ShoalWarningStarted:
            case ExerciseEventType.Grounded:
            case ExerciseEventType.Collision:
                Debug.LogWarning(text, this);
                break;
            default:
                Debug.Log(text, this);
                break;
        }
    }
}
