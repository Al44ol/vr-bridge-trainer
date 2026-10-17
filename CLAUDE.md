# VR_BridgeTrainer — контекст для Claude

VR-тренажёр судоводительского мостика. Прототип сделан в бакалаврской ВКР, сейчас развивается в магистерской:
«Разработка модуля навигационного моделирования и сценарной оценки действий обучающегося для VR-тренажёра судоводительского мостика».

## Документы
- `Docs/Plan.md` — план запросов (этап 2). По одному: план → «ок» → код → проверка в шлеме → коммит.
- `Docs/Handoff.md` — текущее состояние, запрос для следующего чата, чек-листы, хвосты; старое — в `Docs/HandoffArchive.md`.
- **`Docs/Architecture.md` — подробности по каждому запросу** (полная иерархия сцены, классы, поля, решения). Читать нужный раздел, когда работаешь с модулем; дописывать туда, а сюда — только если меняется карта модулей, иерархия или порядок выполнения.
- `Docs/ThesisNotes.md` (≈ 740 КБ) — журнал ВКР; читать только нужные разделы (`grep -n "^## \|^### "`).
- `Docs/ThesisArtifacts.md` — реестр закадровых материалов; `Docs/TheoryNotes.md` — модели для теории; `Docs/Build.md` — сборка PC.

## Стек
- Unity 6000.4.6f1, URP 17.4 (Render Graph), Linear color space
- OpenXR 1.16.1 (+ Meta OpenXR 2.5.0), XR Interaction Toolkit 3.4.1, XR Hands 1.7.3
- Новый Input System 1.19.0
- Целевое железо: Meta Quest 3, PC-VR через Virtual Desktop, 90 Гц; 14 станций с RTX 5070 Ti
- OpenXR Standalone: render mode = **Single Pass Instanced** (с запроса 1; было Multi-pass). Переключатель — `Tools/VR Bridge/OpenXR Render Mode`. Play Mode в редакторе берёт настройки **Standalone**, даже если активная платформа — Android (OpenXRSettings.cs: GetInstance). Android — SPI.
- Самописные шейдеры обязаны поддерживать SPI: `multi_compile_instancing`, `UNITY_VERTEX_INPUT_INSTANCE_ID`, `UNITY_VERTEX_OUTPUT_STEREO`, `UNITY_SETUP_INSTANCE_ID` / `UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO` в vert, `UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX` в frag. Камеру брать через `GetWorldSpaceNormalizeViewDir` / `_WorldSpaceCameraPos` (в SPI — свои для каждого глаза).
- Проверка в шлеме — Play Mode через Virtual Desktop (сборку не делаем). Python на ПК нет (только заглушка Microsoft Store).
- Контроль версий: git (+ `.plastic` — Unity Version Control, репозиторий `VR_BridgeTrainer_1`).
- Код: `Assets/Ocean/Scripts/Core/` — чистая модель (`VRBridge.Core.asmdef`, без сцены, гоняется вне Unity); `Assets/Ocean/Scripts/<модуль>/` — сцена; `.../Editor/` — Setup, стенды, отчёты; тесты — `Assets/Ocean/Tests/Editor` (`VRBridge.Core.Tests.asmdef`, EditMode). Сценарии и наборы оценки — `Assets/Ocean/Scenarios/`.

## Сцена `Assets/Scenes/BridgeScene.unity` (единственная в Build Settings)
Иерархия кратко (полностью, с полями и историей — Architecture.md, «Иерархия BridgeScene»):
- `EventSystem` (XRUIInputModule) · `WeatherSystem` — WeatherController (единственный писатель RenderSettings), SkyWeather · `Lighting` — Sun, Moon, EnvironmentProbe · `Rain` — RainEffect
- `WorldOrigin` — WorldOrigin (плавающее начало) · `OceanRadial` — OceanRadialMesh, OceanWaves, ShipWaterInteraction · `Ocean` — старый, отключён
- `ShipRoot` — ShipNavigation, GroundingMonitor, BridgeActionMonitor, MooringController, TowingController, OwnTugDrive и TugWorkController (ставятся сами)
  - `Ship` — ShipMotion (качка), AmbientSounds, ShipTypeSwitcher (модель 45 м / катер / буксир); модели мостика (рабочая — `МОСТИКИ`; `Ship_Bridge_3D_Model` опущен под воду), `Hull`/`BowTip` (контур корпуса), `FastBoat` (катер, выключен), `OwnTug`, `TugConsole` и `TugJoystick` (буксир — строятся в Play Mode)
  - органы: `Wheel` (SteeringWheel, хват `WheelGrip`), `TelegraphHead` (модель + `Telegraph` EngineTelegraph — двигать только TelegraphHead, детей в Telegraph не класть), `ThrusterHead` (ThrusterLever), `HornButton` (ShipHorn), `BridgeLightSwitch`
  - меню: `BridgeMenu` (кнопка **A**), `SettingsMenu` (**Y**, «Параметры»), `ControlMenu` (**B**, «Управление» + ShipControlOrders); `Stations` (BridgeStationSwitch — крылья)
  - `NavDisplay` (переполнен — не трогать до запроса 16), `ScenarioPanel`, `ScenarioPanelAnchor`, `WeatherPanel`, `BridgeLighting`, `InstructorInputs`, `RainShelter`
  - `XR Origin Hands (XR Rig)` — дочерний к Ship (игрок качается с судном); HeightAdjust, GroundingCameraShake; JumpProvider отключён
- `Scenario` — ScenarioRunner, ExerciseConsoleLog, ExerciseRecorder, ExerciseAssessmentMonitor, TrainerModeSwitch и ScenarioFiles (ставятся сами)
- `Traffic` — TrafficController (цели — TrafficShipView) · `NavLights` — NavLightField · `WaterArea` — WaterAreaView, AidLightsView · `Tug` — TugView (только пока буксир вызван) · `Client` — ClientShipView (судно-клиент, только в 19-03)
- `Instructor` — InstructorStation, InstructorWindow, InstructorPip · `NavPlotter` (планшет, кнопка X) · `PerfCapture` — FrameTimingCapture · `Hands Permissions Manager`
- `ScenarioEditor` — сцена редактора сценариев, создаётся кодом при переходе (`TrainerScenes.OpenEditor`, BridgeScene выгружается; обратно — загружается заново); ассета нет. Начальное меню `StartMenu` — поверх окна инструктора при первом запуске сборки ПК.
- Прочие сцены: `SampleScene` / `BasicScene` — от VR Template; `Assets/_Recovery/0.unity` — аварийная копия.

## Координаты и время
- Плавающее начало: судно и XR Origin около (0,0,0); истинная позиция — `WorldOrigin.ShipPosition` (`WorldPosition { double East, North }`, м); объекты мира — через `WorldToScene()`.
- **+X = восток, +Z = север**; нос модели — **−Z**. Курс ψ: 0 = N, по часовой; δ > 0 — ПБ. С 19.3б-2 точка модели = мидель видимого корпуса (`ShipNavigation.RootFor`).
- Порядок выполнения: WorldOrigin −100 → NavLightField −95 → ShipNavigation −90 → MooringController −89, TowingController −89, OwnTugDrive −89, TugWorkController −89 (силы — в `ShipNavigation.BeforeStep`, до `model.Step`) → ScenarioRunner −88 → BridgeActionMonitor −87 → TrafficController −86 → GroundingMonitor −85 → ExerciseRecorder −84 → ExerciseAssessmentMonitor −83 → OceanWaves −80 → ShipMotion −70, TrafficShipView −70 → WaterAreaView −60 → AidLightsView −55; остальное — 0.
- Шина событий — `ExerciseEvents` (статическая); новые типы событий и столбцы — **только в конец** enum/таблицы.

## Карта модулей (подробно — Architecture.md, раздел с тем же названием; ThesisNotes — разд. N)
- **A1–A2 Навигация** — `ShipNavigation` → Core `ShipDynamicsModel` (Номото/Норрбин), `ShipParameters` (сцена — `Model45m`). ThesisNotes 0.
- **1 SPI, дождь** — дождь отложен (хвост 1 в Handoff). ThesisNotes 1.
- **2–3 Стенд и калибровка** — `ManoeuvringTrials`, `ShipCalibration`, `MarinerModel`; окна Manoeuvring Trials, Model Calibration → `ThesisData/Trials|Calibration`. ThesisNotes 2–3.
- **4 Качка** — `OceanWaveField`, `SeakeepingModel`, `ShipMotion`; вода — шейдер `OceanSurface`. ThesisNotes 4.
- **5 (B1) Акватория, мель** — `WaterArea` (ассет `WaterArea_Demo`), `GroundingDetector`/`GroundingMonitor`. ThesisNotes 5.
- **6 (B2) Сценарии** — `ScenarioDefinition`, `RouteTracker`, `ScenarioRunner`, `ScenarioPanel`; библиотека собирается при входе в Play Mode (`ScenarioLibrarySync`). ThesisNotes 6.
- **7 (C1) Запись** — `Core/Recording` (track/targets/events.csv, session.json), Session Replay. ThesisNotes 7.
- **8 (B3) Суда и огни** — `Core/Traffic`, `TrafficController`, `NavLightField`. ThesisNotes 8.
- **9 (B4) Ночь** — `EnvironmentModel`, WeatherController (погода × время суток), `BridgeLighting`. ThesisNotes 9.
- **10 (C2) Оценка** — `Core/Assessment` (`AssessmentCatalog`, `ExerciseAssessor`, `LiveAssessment` = `SessionAssessment` бит в бит), прогоны `DemoAssessmentRuns`, Assessment Report. ThesisNotes 10.
- **11 (C3) Разбор** — `Core/Debrief` (report.html, схемы SVG, `SvgPlot`), вкладки ScenarioPanel. ThesisNotes 11.
- **12 (C4) Видимость, сигналы** — `WeatherVisibility`, `SoundSignals`, `FogSignals`, критерии F1–F6. ThesisNotes 12.
- **13 (D1) Инструктор, вводные; режимы тренажёра** — `InstructorWindow`, `InstructorPip`, отказы ГД/руля, `TrainerMode`. ThesisNotes 13 (PiP — 13.18, режимы — 13.19).
- **14 (D2) Планшет** — `NavPlotter`, `PlotterMath`, `LiveMapTracks`. ThesisNotes 14.
- **15 Катер** — `ShipPresets.FillFastBoat`, `ShipTypeSwitcher`, `ScenarioVariants` (копия сценария на другом судне). ThesisNotes 15.
- **17 Графики в разборе** — `DebriefPlots`, `DebriefPlotGraphic`. ThesisNotes 17.
- **19.1 Модель 3 СС** — `Core/Manoeuvring/Manoeuvring3Dof.cs`, `ShipDynamicsModel.AddForce/AddMoment`. ThesisNotes 19.1–19.10.
- **19.2 Причал, швартовка** — `Core/Mooring` (`BerthArea`, `MooringHull`, кранцы, концы, подруливающее), `MooringController`, сценарий 19-01, критерии M1–M9, mooring.csv. ThesisNotes 19.11–19.23.
- **19.3 Буксир, ветер** — `Core/Towing` (`WindLoad`, `TugVessel`, `TowingSystem`), `TowingController`, `TugView`, сценарий 19-02, критерии G1–G4. ThesisNotes 19.24–19.27.
- **19.4 Свой буксир** — тип судна «буксир» (`ShipTypeSwitcher`, вид `TugMeshes` кодом), джойстик → две колонки (Core `Towing/TugJoystick.cs`, `OwnTugDrive`, рукоятка `TugJoystickHandle` на месте телеграфа, меню B «Джойстик», удержание курса — тумблер `TugHeadingSwitch`, 19.4а-2; ступени упора 25 % и табличка `TugJoystickIndicator`, 19.4а-4; пульт буксира `TugConsole` вместо стойки и NavDisplay, 19.4а-5); упражнение 19-03: Core `Towing/TugWork.cs` (клиент, лоцман, задания), `HullToHull`, `TugWorkSimulation`, `DemoTugWorkScenario` (19.4б-1); сцена — `TugWorkController`, `ClientShipView`, `PilotOrderNotice`, тип судна сценария `ownShipType`, mooring.csv по клиенту, раздел отчёта «Работа буксира» (19.4б-2); клиент Mariner у причала №2, лоцман командует упором ступенями, версия демо-сценария `demoVersion` (19.4б-3); критерии G5–G11 (`ExerciseAssessor.EvaluateTugWork`, `TugWorkReaction`), прогоны `TugWorkVariants`, вердикт «без оценки» (`AssessmentResult.NotAssessed`) — 19.4в; «случайное событие» у причала (конец отходит, ближний на стопоре, G12–G13) — 19.4г. ThesisNotes 19.28–19.38.
- **21 Сценарии и карты из файлов** — Core `JsonText`, `FieldJson`, `ScenarioFile` (формат, каталог встроенных по имени), `ScenarioFolder` (Документы\VRBridgeTrainer: «Мои сценарии», «Мои акватории», «Образцы»); сцена `ScenarioFiles` (ставится сам, «Обновить файлы» в окне инструктора), `scenario.json` в записи (21.1). Редактор (21.2а): Core `Core/ScenarioEditor/` (`AreaDocument`, `EditHistory`, `MapGrid`, `AreaChecks`; знаки МАМС — `AidEditing`, 21.2б; мели, суша, фарватеры, причалы — `PolygonEditing`, 21.3), сцена `Scripts/ScenarioEditor/` (`TrainerScenes`, `StartMenu`, `ScenarioEditorApp` + `ScenarioEditorShapes`, `EditorMapOverlay`, `MonitorUi`). ThesisNotes 21.
- **Сборка PC** — `Docs/Build.md`; Architecture.md, «Сборка».
- Пункты `Tools/VR Bridge/…` — полный список в Architecture.md («Навигация (A2)» и разделы запросов). Новые сценарии и ассеты создаются сами при входе в Play Mode (`ScenarioLibrarySync`), без ручного Setup.

## Правила работы
- Unity всегда открыт — **batchmode не запускать**.
- Сцены и префабы **не править вручную в YAML**. Изменения сцены — через Editor-скрипты с пунктом меню `Tools/VR Bridge/...` (в `Assets/.../Editor/`), пользователь запускает их сам.
- Старые компоненты **не удалять, а отключать**.
- Скрипты Unity VR Template / XRI Samples (AnchorVisuals, XRKnob и т.п.) не трогать.
- Комментарии в коде — на русском.
- **Перед каждой задачей — план, код только после подтверждения пользователя («ок»).**
- После каждой задачи: список изменённых файлов, ручные шаги в Unity, что проверить в шлеме, коммит.
- **Журнал ВКР** — `Docs/ThesisNotes.md` (в корне проекта, не в Assets). После каждой задачи — новый раздел: что сделано; принятые решения и почему; формулы; параметры со значениями и единицами; результаты испытаний; ограничения модели. Стиль — чтобы почти напрямую переработать в главу 3 ВКР.
- **Данные для таблиц и графиков ВКР** (CSV) — в `ThesisData/` в корне проекта, не в Assets.
- **Реестр «закадровых» материалов** — `Docs/ThesisArtifacts.md`: генераторы, папки ThesisData, CSV/SVG/HTML и фоновые процессы, которых не видно в шлеме. После каждого запроса дописывать туда новые.
- **Подробности модуля** — в `Docs/Architecture.md` (раздел запроса); CLAUDE.md держать коротким (≈ 10 КБ).
- **Формат CSV**: числа — `CultureInfo.InvariantCulture` (точка), разделитель `;`, кодировка UTF-8 с BOM, первая строка — заголовки с единицами (`heading_deg`, `speed_kn`, `t_s`…).
- **Время в симуляции — симуляционное** (FixedUpdate / Time.fixedDeltaTime), не реальное. Исключение — замеры производительности (FrameTimingCapture).
- Не коммитить: `Assets/XR/Settings/OpenXRPackageSettings.asset`, `Assets/Ocean/OceanMaterial.mat`, `zaprosy_claude_code_etap2.md`.
