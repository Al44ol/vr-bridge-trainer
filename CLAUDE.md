# VR_BridgeTrainer — контекст для Claude

VR-тренажёр судоводительского мостика. Прототип сделан в бакалаврской ВКР, сейчас развивается в магистерской:
«Разработка модуля навигационного моделирования и сценарной оценки действий обучающегося для VR-тренажёра судоводительского мостика».

## План работ
- `Docs/Plan.md` — 14 запросов (этап 2: A3, B1–B4, C1–C4, D1–D3). По одному: план → «ок» → код → проверка в шлеме → коммит.
- Журнал для текста ВКР — `Docs/ThesisNotes.md` (см. «Правила работы»).
- Передача контекста между чатами — `Docs/Handoff.md` (состояние, открытые хвосты, договорённости по графикам SVG). Обновлять в конце каждого чата.

## Стек
- Unity 6000.4.6f1, URP 17.4 (Render Graph), Linear color space
- OpenXR 1.16.1 (+ Meta OpenXR 2.5.0), XR Interaction Toolkit 3.4.1, XR Hands 1.7.3
- Новый Input System 1.19.0
- Целевое железо: Meta Quest 3, PC-VR через Virtual Desktop, 90 Гц; 14 станций с RTX 5070 Ti
- OpenXR Standalone: render mode = **Single Pass Instanced** (с запроса 1; было Multi-pass). Переключатель — `Tools/VR Bridge/OpenXR Render Mode`. Play Mode в редакторе берёт настройки **Standalone**, даже если активная платформа — Android (OpenXRSettings.cs: GetInstance). Android — SPI.
- Самописные шейдеры обязаны поддерживать SPI: `multi_compile_instancing`, `UNITY_VERTEX_INPUT_INSTANCE_ID`, `UNITY_VERTEX_OUTPUT_STEREO`, `UNITY_SETUP_INSTANCE_ID` / `UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO` в vert, `UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX` в frag. Камеру брать через `GetWorldSpaceNormalizeViewDir` / `_WorldSpaceCameraPos` (в SPI — свои для каждого глаза).
- Проверка в шлеме — Play Mode через Virtual Desktop (сборку не делаем). Python на ПК нет (только заглушка Microsoft Store).
- Контроль версий: в папке есть `.plastic` (Unity Version Control, репозиторий `VR_BridgeTrainer_1`)

## Сцены
- Рабочая сцена: `Assets/Scenes/BridgeScene.unity` (в Build Settings включена только она)
- `SampleScene` / `BasicScene` — от VR Template; `Assets/_Recovery/0.unity` — аварийная копия сцены

## Иерархия BridgeScene (корень)
- `EventSystem` — XRUIInputModule
- `WeatherSystem` — **WeatherController** → shipMotion = Ship, sunLight = Directional Light, oceanMaterial = OceanMaterial
  + **SkyWeather**: затемнение неба по интенсивности солнца, тучи при seaState > 0.45 (глобалы `_Sky*`)
- `WorldOrigin` — **WorldOrigin** (плавающее начало координат, A1)
- `OceanRadial` (y = −5, в корне, не дочерний к Ship) — **OceanRadialMesh** (радиальный меш R = 10 км, ~98 тыс. вершин) + **OceanWaves** (→ OceanMaterial, WorldOrigin) + **ShipWaterInteraction** (→ Hull, BowTip, WorldOrigin)
- `Ocean` — старый океан, **отключён** (OceanSetup, плоскость 500×500 м)
- `ShipRoot` (0,0,0) — **ShipNavigation** (A2): поворачивает ShipRoot по курсу (yaw = ψ − 180°), позицию отдаёт в WorldOrigin
- `ShipRoot/Ship` — **ShipMotion** (seaState, качка), **AmbientSounds** (→ ShipMotion)
  - `Ship_Bridge_3D_Model`, `МОСТИКИ` (`Assets/Models/Bridge/bridge2/МОСТИКИ.fbx`) — модели мостика
  - `Wheel` — XRSimpleInteractable + **SteeringWheel** (крутится сам объект, масштаб 0.3×0.1×0.3). Хват — только `WheelGrip` (16 сфер по ободу, не вращается), штурвал идёт за рукой (`rimTracking`), автовозврат выкл. Свой BoxCollider выключен.
  - `Telegraph` — XRSimpleInteractable + **EngineTelegraph** (масштаб 0.04×0.25×0.04 — не класть детей!). Свой BoxCollider выключен, старый `LeverVisual` скрыт.
  - `NavDisplay` — временная панель A2 (BridgeNavDisplay), лежит на пульте на месте панели погоды (погода — кнопка B): курс, скорость, ROT, руль заданный/фактический, телеграф, шкала руля ±35°
  - `TelegraphHead` содержит и сам `Telegraph` (A2.2) — двигать только TelegraphHead. Рукоятка и обод штурвала подсвечиваются (GrabHighlight); штурвал и рычаг следят за точкой хвата интерактора (при хвате лучом — точка попадания)
  - `TelegraphHead` — модель телеграфа (масштаб 0.5, стоит на пульте справа от кнопки «Шторм») (Setup Telegraph Model): корпус, барабан со шкалой, `LeverPivot` (= EngineTelegraph.leverVisual, ось X, шаг 15°) с рукояткой `Knob` — единственный коллайдер взаимодействия. Рычаг следует за рукой (`angleTracking`).
  - `HornButton` — XRSimpleInteractable + **ShipHorn** + AudioSource; selectEntered → ShipHorn.PlayHorn
  - `WeatherPanel` — Canvas + **PanelToggle** (кнопка B / Tab); кнопки сеткой 2×2: Штиль | Шторм / Волнение | Ночь → WeatherController.SetCalm/SetModerate/SetStorm/SetNight
  - `XR Origin Hands (XR Rig)` — префаб `VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Hands Variant` + добавлен **HeightAdjust** (кнопка Y / H)
  - `Hull`, `BowTip`, `Cube` — примитивы корпуса/палубы. Hull/BowTip удлинены вниз: осадка 3 м под воду (Setup A1.1)
- `Lighting` — Directional Light (Sun), Light Probe Group, Reflection Probe, Post Process Volume (выключен)
- `Rain` — **RainEffect** (Setup Rain): частицы `RainParticles` создаются в рантайме, излучатель над камерой; интенсивность задаёт WeatherController (только storm)
- `ShipRoot/Ship/RainShelter` — BoxCollider-триггер (слой Ignore Raycast) = объём рубки, капли внутри уничтожаются. Размер подгоняется вручную в Scene View, Setup Rain его не перезаписывает
- `PerfCapture` — **FrameTimingCapture** (Setup Frame Timing): F8 — замер времени кадра CPU/GPU 30 с → `ThesisData/Performance/`
- `Hands Permissions Manager` — префаб шаблона

XR Origin — дочерний объект Ship, игрок качается вместе с судном.

## Плавающее начало координат (A1)
- Судно и XR Origin всегда около (0,0,0). Истинная позиция судна — `WorldOrigin.ShipPosition` (`WorldPosition { double East, North }`, метры).
- Система координат: **+X = восток, +Z = север**. Нос модели судна смотрит в **−Z** (`WorldOrigin.bowLocalAxis`).
- На A2 позицию задаёт навигация через `ShipPosition` / `SetShipPosition`; объекты мира ставить через `WorldToScene()`.
- Временный ход (до A2): `WorldOrigin.telegraph` → скорость = SpeedFactor · maxSpeedKnots (15 уз) · testSpeedMultiplier, по носу, с плавным разгоном. Курс не меняется (ShipMotion перезаписывает поворот Ship).
- Отладка: `debugKeyboard` — стрелки ↑↓ вдоль носа, ←→ поперёк, Shift — 100 м/с.

## Навигация (A2)
- `ShipNavigation` (на ShipRoot), параметры — `ShipParameters_Model45m.asset` (под модель на сцене: L 45 м, τ 30/60 с); `ShipParameters_Default.asset` — среднее торговое судно (заглушка, калибровка в A3): L 150 м, 15 уз, K' 0.8, T' 2.0, τ 90/150 с, k_зх 0.6, δ_max 35°, 2.32 °/с.
- Модель: рулевая машина (ограничение скорости перекладки, СОЛАС II-1/29), du/dt = (u_зад − u)/τ, Номото 1-го порядка T·ṙ + r = K·δ с K = K'·U/L, T = T'·L/U, интегрирование в FixedUpdate (точная экспонента), отображение интерполируется в Update. Формулы — в шапке ShipNavigation.cs.
- **A3.1**: математика — в чистом классе `ShipDynamicsModel` (`Assets/Ocean/Scripts/Core/`, сборка `VRBridge.Core.asmdef`, туда же перенесён `ShipParameters`). ShipNavigation только передаёт штурвал/телеграф в модель и показывает результат. K = K'·u/L по фактической скорости (U_min — только в T): на стопе руль не поворачивает судно.
- Стенд испытаний: `Tools/VR Bridge/Manoeuvring Trials` (без Play Mode, dt = 0.02 с, `ManoeuvringTrials` в Core) — циркуляции, начальная поворотливость, зигзаги, торможение, разгон, критерии IMO MSC.137(76); вывод в `ThesisData/Trials/` (CSV, summary.csv/.md, SVG). Скорость испытаний по умолчанию 0.9·V_эксп.
- EditMode-тесты: `Assets/Ocean/Tests/Editor` (`VRBridge.Core.Tests.asmdef`), Test Runner → EditMode.
- **A3.2 (калибровка)**: в `ShipParameters` переключатели `yawModel` (Nomoto | Norrbin: T·ṙ + r + α·r³ = K·δ, α = α'·(L/U)²) и `turnSpeedLossCoeff` C_r' (du/dt = … − C_r'·L·r²); по умолчанию выключены → прежняя модель бит в бит. Стенд работает с любой `IVesselModel` (`TrialSettings.ModelFactory`). Эталон — `Core/Reference/MarinerModel` (MSS `mariner.m`, Chislett & Strøm-Tejsen 1965), данные KCS — `KcsEfdData`; идентификация — `Core/Calibration/ShipCalibration` (Нелдер–Мид, повтор руля эталона). Окно `Tools/VR Bridge/Model Calibration` → `ThesisData/Calibration/`, ассеты `ShipParameters_Mariner` / `ShipParameters_KCS` (Норрбин), источники каждого значения — `Assets/Ocean/Settings/ShipParameters_Sources.md`. Сцена по-прежнему на `Model45m` (смена — отдельное решение).
- Знак штурвала: Auto — по часовой с места оператора = ПБ. Курс ψ: 0 = N, по часовой; δ > 0 — ПБ.
- Пока ShipNavigation включён, `WorldOrigin.ExternalControl = true` (ход от телеграфа A1 и стрелки отключены).
- Тест циркуляции: F5 или контекстное меню ShipNavigation; лог — выдвиг, тактический диаметр, D (теория при умолчаниях: 614 м = 4.1 L).
- Пункты меню: Setup A1 Ocean → Setup A1.1 Hull Water → Setup Telegraph Model → Setup A2 Navigation → Setup Console and Sky → Setup A2.2 Controls → Setup Rain, Setup Frame Timing (запрос 1), Setup Seakeeping (запрос 4). Окна: Manoeuvring Trials (запрос 2), Model Calibration (запрос 3), Seakeeping Trials (запрос 4) → `ThesisData/Seakeeping/`.

## Качка на волнах (запрос 4)
- Поле волн — чистый класс `Core/Seakeeping/OceanWaveField` (единый источник для шейдера и C#). `OceanWaves` заполняет его (`ConfigureField`) и шлёт те же значения в шейдер. Фазы θ продвигаются в **FixedUpdate**; в кадре — перенос на время отображения `OceanWaves.RenderTimeOffset` = −(1 − α)·Δt (та же α, что в ShipNavigation).
- Порядок выполнения: WorldOrigin −100 → ShipNavigation −90 → OceanWaves −80 → ShipMotion −70.
- Высота воды для других объектов (буи B1, суда B3): `OceanWaves.Instance.SampleHeight(WorldPosition)` / `SampleWaterLevel(Vector3 сцены)` — обратная задача Герстнера, 3 итерации.
- Мореходные поля ShipParameters (B, d, GM, C_B, C_WP, T_φ/T_z/T_θ, ζ) заполнены только у `Model45m` (Setup Seakeeping, допущения помечены в `ShipParameters_Sources.md`).
- Шейдер у борта по-прежнему гасит волны к нулю (`_HullDampMin`): если в шлеме корпус «отрывается» от воды — правка «вода у борта → плоскость ватерлинии судна» (отложена до проверки).
- Дождь (запрос 1) **отложен**: частицы есть (~3900), но в шлеме не видны. После исправления текстуры/очереди Setup Rain ещё не перезапускался — начинать с этого.

## Ключевые скрипты (`Assets/Ocean/Scripts`)
- `ShipMotion` — качка (запрос 4). Режим `Waves` (по умолчанию): в FixedUpdate вода сэмплируется в сетке 5×3 точек корпуса, плоскость МНК → звенья 2-го порядка (`Core/Seakeeping/SeakeepingModel`, периоды и ζ — в ShipParameters), отображение интерполируется как в навигации, поворот вокруг `pivotLocal` (мидель, ватерлиния). Комфорт: `comfortScale`, мягкие пределы `maxRollDeg`/`maxPitchDeg`. Режим `Sinusoidal` — прежние синусоиды. Каждый кадр пишет `_WaveIntensity` = seaState в материал. Время шага — `stepTimeUs` в инспекторе.
- `SteeringWheel` — угол руля ±35° (`RudderAngle`), пока никем не читается.
- `EngineTelegraph` — 9 положений, `SpeedFactor` −1..1, пока никем не читается.
- `WeatherController` — пресеты calm/moderate/storm/night (клавиши 1–4), плавный переход; меняет свет, туман, `_WaveIntensity`, `_WaveSpeed`.
- `WorldOrigin`, `OceanWaves`, `OceanRadialMesh` — см. разделы выше/ниже.
- `ShipHorn`, `AmbientSounds`, `PanelToggle`, `HeightAdjust` — окружение и UI.
- `WeatherController` также ведёт `rain` (0..1) в пресетах: storm = 1, остальные 0 → `RainEffect.Intensity`, тем же SmoothStep, что и погода.
- `RainEffect` — дождь: мировое пространство, излучатель над камерой, скорость капли = ветер (OceanWaves.WindDirection · 15 м/с) − ход судна + падение 6.5 м/с; материал `Assets/Ocean/Materials/Rain.mat` (очередь 3100 — после воды).
- `FrameTimingCapture` — замер производительности (FrameTimingManager + ProfilerRecorder).

## Вода
- Шейдер `Assets/Ocean/Shaders/OceanSurface.shader` (`VRBridgeTrainer/OceanSurface`): 4 волны Герстнера. Параметры волн — в `OceanWaves` (C#), передаются глобалами `_OceanWave*`; фазы (смещение судна + время) считаются в C# в double mod 2π. Затухание `_WaveFadeStart`/`_WaveFadeEnd` (1.5→6 км) + подавление волн мельче шага сетки/пикселя. Нормали попиксельно. Туман URP поддерживается.
- Ветер: `OceanWaves.windFromDeg` (морской пеленг «откуда», по умолчанию 160°), волны веером вокруг него. Влияние `_WaveSpeed` ограничено `speedInfluence` = 0.25 (физическая дисперсия).
- Рябь: 4 волны λ 4.1…0.75 м только в нормалях. Сила — `detailStrength` (0.6). Барашки: шум 240 м, привязан к воде, только на гребнях; два слоя попеременно гаснут/появляются (`whitecapLifetime` 7 с), покрытие растёт с волнением (`_FoamAmount`).
- Корпус: ShipWaterInteraction передаёт контуры Hull/BowTip — внутри вода не рисуется (clip), у борта волны гасятся (`_HullDampMin`/`_HullDampWidth`); след за кормой (полоса + усы Кельвина 19.47°) и бурун ∝ `WorldOrigin.ShipSpeed` (`_ShipFoam`).
- Волны (OceanWaves): λ 90/45/20/9 м, амплитуда = amplitude · _WaveIntensity^exponent (зыбь растёт только в сильное волнение). Пена — по крутизне гребня Σ k·a·sin f.
- `_WaveSpeed` в шейдере не используется напрямую — его читает OceanWaves.
- Скайбокс `Assets/Ocean/SkyboxHorizonFog.mat` (`VRBridgeTrainer/SkyboxHorizonFog`): кубмап + смешивание с fogColor у горизонта. Старый `skybox01_openGL` не удалён. Тучи (процедурный fbm) и затемнение — от SkyWeather.
- Материал `Assets/Ocean/OceanMaterial.mat` — общий ассет, его меняют ShipMotion и WeatherController в рантайме.

## Камера
- Камера из `Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab`: near 0.01; far = 12 000 м — override в сцене (Setup A1).

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
- **Формат CSV**: числа — `CultureInfo.InvariantCulture` (точка), разделитель `;`, кодировка UTF-8 с BOM, первая строка — заголовки с единицами (`heading_deg`, `speed_kn`, `t_s`…).
- **Время в симуляции — симуляционное** (FixedUpdate / Time.fixedDeltaTime), не реальное. Исключение — замеры производительности (FrameTimingCapture).
