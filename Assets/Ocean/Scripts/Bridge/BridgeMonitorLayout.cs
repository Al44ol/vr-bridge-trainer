// ============================================================
// BridgeMonitorLayout.cs — Плоскости экранов пульта в осях Ship (запрос 16а, по проверке 23.10.2026)
//
// Ассет Resources/BridgeMonitorLayout.asset пишет редактор сам при входе в Play Mode (BridgeMonitorLayoutSync):
// по граням сетки каждого экрана (Core ScreenFace) — центр лицевой грани, нормаль к игроку, «верх», ширина и высота.
// Зачем ассет: у модели МОСТИКИ выключен Read/Write, в сборке вершины сетки не прочитать — сборка берёт
// плоскости отсюда. В редакторе без ассета (или для экрана, которого в нём нет) плоскость считается по граням на месте.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

public class BridgeMonitorLayout : ScriptableObject
{
    public const string ResourcePath = "BridgeMonitorLayout";

    [Serializable]
    public class Entry
    {
        public string name = "";
        [Tooltip("Центр лицевой грани, оси Ship, м")] public Vector3 center;
        [Tooltip("Нормаль к игроку, оси Ship")] public Vector3 normal;
        [Tooltip("«Верх» экрана, оси Ship")] public Vector3 up;
        [Tooltip("Ширина и высота грани (без полей), м")] public float width, height;
    }

    public List<Entry> screens = new List<Entry>();

    public Entry Find(string name) => screens.Find(e => e != null && e.name == name);
}
