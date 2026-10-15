# VR_BridgeTrainer — контекст для Claude

VR-тренажёр судоводительского мостика. Прототип сделан в бакалаврской ВКР, сейчас развивается в магистерской:
«Разработка модуля навигационного моделирования и сценарной оценки действий обучающегося для VR-тренажёра судоводительского мостика».

## План работ
- `Docs/Plan.md` — 20 запросов (этап 2: A3, B1–B4, C1–C4, D1–D3; с C3 добавлены 15 — быстроходный катер, 16 — мониторы на пульте МОСТИКИ, в самом конце; 12.10.2026 — 17 графики в разборе в шлеме, 18 операции катера, 19 буксир и швартовка (19.1–19.3), 20 автономная версия Quest 3). По одному: план → «ок» → код → проверка в шлеме → коммит.
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
  + **SkyWeather**: затемнение неба по интенсивности солнца, тучи при seaState > 0.45 (глобалы `_Sky*`); с B4 — ночное небо (звёзды, луна, сумерки) по итоговому освещению WeatherController
  + с B4 WeatherController: погода и время суток раздельно (EnvironmentModel), moonLight = Lighting/Moon
- `WorldOrigin` — **WorldOrigin** (плавающее начало координат, A1)
- `OceanRadial` (y = −5, в корне, не дочерний к Ship) — **OceanRadialMesh** (радиальный меш R = 10 км, ~98 тыс. вершин) + **OceanWaves** (→ OceanMaterial, WorldOrigin) + **ShipWaterInteraction** (→ Hull, BowTip, WorldOrigin)
- `Ocean` — старый океан, **отключён** (OceanSetup, плоскость 500×500 м)
- `ShipRoot` (0,0,0) — **ShipNavigation** (A2): поворачивает ShipRoot по курсу (yaw = ψ − 180°), позицию отдаёт в WorldOrigin
- `ShipRoot/Ship` — **ShipMotion** (seaState, качка), **AmbientSounds** (→ ShipMotion)
  - `Ship_Bridge_3D_Model`, `МОСТИКИ` (`Assets/Models/Bridge/bridge2/МОСТИКИ.fbx`) — модели мостика. С 11.10.2026 `Ship_Bridge_3D_Model` опущен пользователем под воду (y −32,92), рабочий мостик — МОСТИКИ; палка старого телеграфа `static` удалена пользователем
  - `Wheel` — XRSimpleInteractable + **SteeringWheel** (крутится сам объект, масштаб 0.3×0.1×0.3). Хват — только `WheelGrip` (16 сфер по ободу, не вращается), штурвал идёт за рукой (`rimTracking`), автовозврат выкл. Свой BoxCollider выключен.
  - `Telegraph` — XRSimpleInteractable + **EngineTelegraph** (масштаб 0.04×0.25×0.04 — не класть детей!). Свой BoxCollider выключен, старый `LeverVisual` скрыт.
  - `NavDisplay` — временная панель A2 (BridgeNavDisplay), лежит на пульте на месте панели погоды (погода — меню Y «Погода (панель)…», до D1 — кнопка B): курс, скорость, ROT, руль заданный/фактический, телеграф, шкала руля ±35°
  - `TelegraphHead` содержит и сам `Telegraph` (A2.2) — двигать только TelegraphHead. Рукоятка и обод штурвала подсвечиваются (GrabHighlight); штурвал и рычаг следят за точкой хвата интерактора (при хвате лучом — точка попадания)
  - `TelegraphHead` — модель телеграфа (масштаб 0.5, стоит на пульте справа от кнопки «Шторм») (Setup Telegraph Model): корпус, барабан со шкалой, `LeverPivot` (= EngineTelegraph.leverVisual, ось X, шаг 15°) с рукояткой `Knob` — единственный коллайдер взаимодействия. Рычаг следует за рукой (`angleTracking`).
  - `HornButton` — XRSimpleInteractable + **ShipHorn** + AudioSource; selectEntered → ShipHorn.PlayHorn; с C4 гудок звучит, пока кнопку держат (selectExited → StopHorn — подписка в коде ShipHorn); с C2 — модель «ТИФОН» `HornButtonModel` (Setup Horn Button) + **HornButtonVisual**, старый диск `Cylinder` отключён
  - `WeatherPanel` — Canvas + **PanelToggle** (с D1 — меню Y «Погода (панель)…»; кнопка B / Tab — только при `controllerButton`); кнопки сеткой 2×2: Штиль | Шторм / Волнение | Ночь → WeatherController.SetCalm/SetModerate/SetStorm/SetNight
  - `XR Origin Hands (XR Rig)` — префаб `VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Hands Variant` + добавлен **HeightAdjust** (с D1 — меню Y «Рост игрока…»; кнопка **X** левого контроллера / H — только при `controllerButton`). С B1: **JumpProvider отключён** (прыжок был на A), на `Camera Offset` — **GroundingCameraShake**
  - `BridgeMenu` — **BridgeMenu** (B1): VR-меню команд на кнопке **A** правого контроллера; модули регистрируют команды сами (`BridgeMenu.Register`); с B2 — `SetGroupOrder`, флаг `hideWhenUnavailable`; с D1 — подменю: группа = кнопка «Группа »» со своей страницей и «« Назад», `SetGroupExpanded` — группа прямо на главной (A: «Сценарий» с паузой, «Сигнализация»; Y: «Сценарий» (текущий балл), «Общее» — рост, панель погоды). Разделы A: Вводные, Обстановка, Судно, Отладка; Y: Свет мостика, Монитор инструктора, Оповещения, Замер. Меню после команды НЕ закрывается; закрывается только у команд с окном (`.OpensWindow()`). Панели роста и погоды — кнопка «« Назад» → `BridgeMenu.OpenMenu(SettingsMenuId)`
  - `ScenarioPanel` — **ScenarioPanel** (B2): VR-панель выбора сценария / брифинга / результатов + команды меню группы «Сценарий»; с C3 результаты — разбор на вкладках
  - `Hull`, `BowTip`, `Cube` — примитивы корпуса/палубы. Hull/BowTip удлинены вниз: осадка 3 м под воду (Setup A1.1)
- `Lighting` — Directional Light (Sun), Post Process Volume (выключен); с B4: **Moon** (направленный, без теней), **EnvironmentProbe** (Reflection Probe «только небо», Realtime, 40 км + **EnvironmentReflection**); старые Light Probe Group и Reflection Probe **отключены** (данные шаблона)
- `Ship/BridgeLighting` — **BridgeLighting** (B4): лампы рубки (CabinLamp_*), подсветка приборов, отсвет своих огней (Own*Glow); `Ship/BridgeLightSwitch` — тумблер «СВЕТ» (**BridgeLightSwitch**, XRSimpleInteractable) рядом с HornButton; `Ship/SettingsMenu` — второй **BridgeMenu** («Параметры», кнопка **Y** левого контроллера — `{LeftHand}/secondaryButton`)
- `Rain` — **RainEffect** (Setup Rain): частицы `RainParticles` создаются в рантайме, излучатель над камерой; интенсивность задаёт WeatherController (только storm)
- `ShipRoot/Ship/RainShelter` — BoxCollider-триггер (слой Ignore Raycast) = объём рубки, капли внутри уничтожаются. Размер подгоняется вручную в Scene View, Setup Rain его не перезаписывает
- `WaterArea` — **WaterAreaView** (B1): буи/суша из ассета `WaterArea_Demo`, отладочный слой мелей; редактирование — выделить объект, ручки в Scene View
- `ShipRoot` также несёт **GroundingMonitor** (B1): контроль глубины, посадка на мель, сигнализация; с C1 — **BridgeActionMonitor** (−87): действия обучающегося → шина
- `Scenario` — **ScenarioRunner** (B2, порядок −88) + **ExerciseConsoleLog**: сценарий, маршрут, шина событий → консоль; с C1 — **ExerciseRecorder** (−84): запись упражнения в файлы; с C2 — **ExerciseAssessmentMonitor** (−83): оценка действий
- `Traffic` — **TrafficController** (B3, −86): другие суда по сценарию, столкновения, отладка огней; цели — дочерние объекты с **TrafficShipView** (−70)
- `NavLights` — **NavLightField** (B3, −95): все навигационные огни одним мешем (шейдер `VRBridgeTrainer/NavLight`), режим огней «авто/вкл/выкл»
- `WaterArea` также несёт **AidLightsView** (B3, −55): огни знаков МАМС; `ScenarioPanel` — **ExerciseFailureNotice** (B3); `Ship/ScenarioPanelAnchor` — точка панели выбора при запуске
- `PerfCapture` — **FrameTimingCapture** (Setup Frame Timing): меню Y → «Замер» (F8 — только при `keyboardShortcuts`) — замер времени кадра CPU/GPU 30 с → `ThesisData/Performance/`; с 12.10.2026 — индикатор в шлеме (надпись «ЗАМЕР N с» над штурвалом или `indicatorAnchor`, итог 4 с, тоны; флаги `showIndicator`, `tones`), «Прервать замер (осталось N с)», при выходе из Play Mode набранное сохраняется; в сводке столбцы `frames_over_20ms`, `max_frame_dt_ms` (ThesisNotes 13.16)
- `NavPlotter` — **NavPlotter** (D2, запрос 14): навигационный планшет в левой руке, кнопка **X** (у HeightAdjust кнопка X выключена)
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
- Тест циркуляции: меню A «Отладка» или контекстное меню ShipNavigation (F5 — только при `keyboardShortcuts`); лог — выдвиг, тактический диаметр, D (теория при умолчаниях: 614 м = 4.1 L).
- Пункты меню: Setup A1 Ocean → Setup A1.1 Hull Water → Setup Telegraph Model → Setup A2 Navigation → Setup Console and Sky → Setup A2.2 Controls → Setup Rain, Setup Frame Timing (запрос 1), Setup Seakeeping (запрос 4), Setup B1 Water Area + Rebuild Demo Water Area + Ship To Demo Start (запрос 5), Setup B2 Scenarios + Rebuild Demo Scenario (запрос 6), Setup C1 Recording (запрос 7), Setup B3 Traffic + Rebuild Demo Traffic Scenario + Traffic Report + Scenario Panel To Handrail Height (запрос 8), Setup B4 Night (запрос 9), Setup C2 Assessment + Setup Horn Button + Assessment Report (запрос 10), Debrief Report + Sessions Summary… (запрос 11, без Setup — сцену не правит), Setup C4 Fog + Rebuild Demo Fog Scenario (запрос 12, сцену не правит), Setup D1 Instructor (запрос 13), Setup D2 Plotter (запрос 14), Setup Fast Boat (запрос 15). Окна: Session Replay (запрос 7), Manoeuvring Trials (запрос 2; с 19.1 — модель 3 СС, малые скорости, сравнение моделей), Model Calibration (запрос 3), Seakeeping Trials (запрос 4) → `ThesisData/Seakeeping/`.

## Учебная акватория (запрос 5, B1)
- Данные — ScriptableObject `WaterArea` (`Core/WaterArea/`): знаки МАМС регион A (`AidType`, внешний вид — `IalaMarks`), мели (многоугольник + наименьшая глубина), суша, фарватеры (ось + ширина), границы, глубина по умолчанию. `WorldPosition` перенесён в Core (`Core/WorldPosition.cs`).
- Демо — `Assets/Ocean/Settings/WaterArea_Demo.asset` (`DemoWaterArea.Fill`): фарватер 2 кбт, колено 000° → поворот вправо 60° у (0, 3200) → 060°; старт сценария (0, −2500), курс 000°.
- Сцена: `WaterAreaView` (порядок −60) — модели (`WaterAreaMeshes`, процедурные), позиция через `WorldToScene` каждый кадр, высота/наклон — `OceanWaves.SampleHeight` (3 точки); > 3,5 км — раз в 10 кадров.
- Посадка: `GroundingDetector` (Core) — контур L×B по курсу, опасно h < d + Δ (Δ 0,5 м) и суша; приближение D_пред 1 кбт с гистерезисом 10 %. `GroundingMonitor` (ShipRoot, порядок −85): `ShipDynamicsModel.SetGrounded` — наползание τ_ост 1 с, руль не действует, съём ходом от мели (k_снят 0,3); удар/скрежет (`GroundingSounds`, заглушки), тряска камеры; события C# + UnityEvent; снятие инструктором (меню) — на позицию ≈30 с назад, `ShipNavigation.TeleportTo`, `EngineTelegraph.SetStop`.
- Индикация: полоса над NavDisplay (глубина / «ОПАСНО: МЕЛЬ x кбт» / «НА МЕЛИ»), тоны; квитирование — меню.
- Клавиатура в B1 не используется (решение пользователя): всё — через меню A. С D1 — и кнопки X, B, клавиши F2/F5/F8/1–4/H/Tab отключены флагами (`keyboardShortcuts` / `controllerButton`): станция автономна, всё — из меню A и Y. Отладочные команды меню: показать мели, переставить судно (старт демо / вход в фарватер / 800 м до поворота).
- Туман погоды пересчитан (d ≈ 1,73/ρ, ExponentialSquared): штиль 0,0003 (≈5,8 км), умеренное 0,0004 (≈4,3 км), шторм 0,0015 (≈1,2 км), ночь 0,0004 (≈4,3 км) — край океана 10 км скрыт, буи видны. Было 1,7 км / 0,6 км / 70 м. Рябь: `Tools/VR Bridge/Weaker Ripple` (detailStrength 0,12; запасное значение 0,35).
- Трекинг рук не нужен (решение пользователя).
- Тесты: `WaterAreaTests` (18); всего в Test Runner 67 (тесты калибровки — файл `CalibrationTests.cs`, переименован в B1: под старым именем Unity его не компилировал).

## Сценарии (запрос 6, B2)
- Core `Core/Scenarios/`: `ScenarioDefinition` (ассет, Create → VR Bridge → Scenario; `Validate()`), `RouteTracker` (XTE > 0 — правее; прохождение — биссектриса / линия финиша; коридор с гистерезисом η 0,05), `ExerciseEvents` (статическая шина: `Publish`, `Raised`, `Log`, `Time` — время упражнения ставит шина; Value/Value2 по типам — в шапке файла), `ExerciseSummary`, `ScenarioText` (русские названия, SF телеграфа), `DemoScenario`, `AssessmentCriteria` (заготовка C2).
- Сцена `Scripts/Scenarios/`: `ScenarioRunner` — Idle → Briefing (судно стоит, `ShipNavigation.Paused`) → Running → Finished; начальные условия — `TeleportTo(e, n, курс, уз, руль)` + `GroundingMonitor.ResetState` + `EngineTelegraph.SetSpeedFactor` + `ShipNavigation.SetRudderOrder`; погода — `WeatherController.SetPreset(id, instant)`. Посадку/столкновение runner берёт из шины. `ScenarioPanel` — UI и команды меню; `ExerciseConsoleLog` — подписчик шины.
- `GroundingMonitor.Raise` публикует события мели в шину (прежние C#-события/UnityEvent сохранены).
- Первый сценарий `Assets/Ocean/Scenarios/Scenario_B2-01_Fairway.asset`: старт (0, −2500) 000° 15 уз «Полный вперёд», маршрут старт → (0, −300) → (0, 3200) → (3031, 4950), XTE доп. 250/150 м, умеренное, день, лимит 30 мин (значения предварительные).
- Библиотека сценариев (`ScenarioRunner.scenarios`) собирается сама при входе в Play Mode (`ScenarioLibrarySync` в SetupScenarios.cs). Маршрут виден только в Scene View (гизмо; ручки — выделить объект Scenario), в шлеме — нет (будет на планшете D2).
- Видимость сценария применяется с C4 (раздел ниже); критерии — с C2, время суток — с B4, другие суда — B3.
- Тесты: `ScenarioTests` (11); всего 78 (Unity Test Runner 78/78). Шина: публикация во время рассылки — в очередь, доставка после текущего события.

## Запись упражнения (запрос 7, C1)
- Core `Core/Recording/`: `CsvFormat`, `SessionFormat` (столбцы, `session.json` = `SessionMeta`, имя папки), `SessionFileWriter` (фоновый поток), `SessionRecorder` + `TrackSampler`, `SessionStorage` (список, ротация, метка `keep`), `SessionReader` (→ `RecordedSession`, события обратно в `ExerciseEvent`, `StateAt(t)`) — офлайн-вход для C2/C3, `ControlActionDetector`, `ExerciseTargets` (реестр целей для B3 → targets.csv).
- Сессия = упражнение (ExerciseStarted → ExerciseEnded). Папка `<yyyyMMdd_HHmmss>_<сценарий>_<ID>`: track.csv (5 Гц по времени упражнения), targets.csv, events.csv (вся шина + `description`), session.json (сценарий с маршрутом, судно, приложение, итог). Формат — ThesisNotes, раздел 7.4.
- Где: редактор — `ThesisData/Sessions/` (**в .gitignore**; нужную сессию — `git add -f`), сборка — Документы/VRBridgeTrainer/Sessions, Quest — persistentDataPath/Sessions. Ротация `maxSessions` = 10 (0 — без ротации, для эксперимента); метка «Сохранить» в Replay (файл `keep`) защищает сессию.
- Действия обучающегося — новые типы шины (в конец enum): HelmStarted/HelmEnded, TelegraphChanged, HornStarted/HornEnded, WeatherChanged; источник — `BridgeActionMonitor` (ShipRoot), пороги в инспекторе (0,5° / 0,2° / 1 с / 0,5 с, предварительные). Органам управления добавлены только свойства: `EngineTelegraph.IsGrabbed/LastChangeFromCode`, `ShipHorn.IsSounding`, `WeatherController.CurrentPresetId`. С C4 гудок — «пока держат», у HornEnded тип звука (Index), сигналы — SoundSignal.
- ID обучающегося — цифры, без ФИО: VR-клавиатура на брифинге («Ввести ID обучающегося», ScenarioPanel) или `ExerciseRecorder.traineeId`; без ID — `noid`.
- Replay: `Tools/VR Bridge/Session Replay` (без Play Mode): карта, шкала времени ×1…×60, состояние в момент t, события.
- Тесты: `RecordingTests` (11); всего 89 (Unity Test Runner 89/89). Проверено в шлеме; 90 Гц с записью.

## Другие суда и огни (запрос 8, B3)
- Core `Core/Traffic/`: `TrafficShipDefinition` (поле `ScenarioDefinition.traffic`; позиция — абсолютная / пеленг+дистанция / «точка встречи» CPA+TCPA+сторона; постоянный курс или маршрут; тип — только вид), `TrafficPlanner` (старт и путь), `CpaCalculator` (CPA/TCPA, обратная задача), `HeadingAutopilot` (ПД, размещение полюсов ω_n' 0,75, ζ 0,7) + `RouteFollower` (LOS, упреждение R·tg(Δχ/2) + U·δ/ω_δ), `TrafficVessel` (`ShipDynamicsModel` + авторулевой, `ITrackedTarget`), `HullContact` (столкновение), `NavigationLights` + `HullProfile` + `LightCharacter` (правила 21–23, Приложение I, Аллард, Кошмидер, огни знаков), `DemoTrafficScenario` (B3-01).
- Сцена `Scripts/Traffic/`: `TrafficController`, `TrafficShipView` (качка — `SeakeepingModel`), `TrafficMeshes` (сухогруз/танкер/малое), `NavLightField`, `AidLightsView`; `Scripts/Scenarios/ExerciseFailureNotice` (меню A «Сценарий» → «Оповещение о провале»). `ScenarioPanel.startAnchor` — панель выбора при запуске в фиксированной точке.
- Огни горят (авто): с B4 — `WeatherController.Darkness` ≥ 0,5 (сумерки, ночь; до B4 — `ambientIntensity` < 0,3) или видимость < 2 мили; яркость — закон Алларда с K = 0,05^(1852/V), V = 1,73/ρ. Туман пресета «Ночь» — 0,00015 (≈ 6,2 мили; было 0,0004), запасное 0,0002.
- Столкновение → `Collision` (Subject — id цели, Value — отн. скорость, уз, Value2 — курсовой угол), цель — «Стоп», удар и тряска. Отладка в меню A: «Огни: авто/вкл/выкл», «Проверка огней: облёт цели», «Огни, цель: < 1/3/6 миль >» (переключатель `BridgeMenu.RegisterStepper` — строка со стрелками, меню не закрывается).
- Панель выбора при запуске — `Ship/ScenarioPanelAnchor`, центр на 1,15 м над полом (уровень поручней), наклон к глазам.
- Параметры целей по умолчанию: сухогруз и танкер — Mariner, малое — Model45m. Mariner B 23,16 м (осадка 8 м — заглушка), KCS B 32,2 / d 10,8 м (Setup B3).
- Сценарий `Scenario_B3-01_Traffic.asset` (ночь; T1 встречный в фарватере, T2 пересекающий справа CPA 0 TCPA 4 мин, T3 обгоняемый). session.json → `scenario.traffic`; Replay рисует цели.
- Тесты: `TrafficTests` (36); всего 125. Данные для ВКР — `Tools/VR Bridge/Traffic Report` → `ThesisData/Traffic/`.

## Ночь (запрос 9, B4)
- Причина прежней «дневной» ночи: сцена ссылалась на запечённые данные шаблона (`Assets/Scenes/SampleScene/LightingData.asset`) — рассеянный свет, пробы и отражения дневные; Setup B4 ссылку снимает (ассет не трогать), Light Probe Group и старый Reflection Probe **отключены**, рассеянный свет — Trilight.
- Core `Core/Environment/EnvironmentModel.cs`: `WeatherLook` (погода) × фаза времени суток s (0 день / 1 сумерки / 2 ночь) → `EnvironmentLighting`; `NightProfile` — экранные значения ночи и сумерек, экспозиция E (линейная яркость × E). День — бит в бит пресет (тест). Без вспышек: сумерки ≤ день, ночь ≤ сумерки (покомпонентный минимум). `LightsRequired`: Darkness ≥ 0,5 или V < 2 мили.
- `WeatherController` — единственный писатель RenderSettings: солнце + **луна** (отдельный Light, азимут 330°, 22°), Trilight, туман, `_OceanNightSpec` (лунная дорожка; днём 0). Переход погоды 3 с, времени суток 8 с на фазу. `SetPreset` — как до B4; `SetWeather` / `SetTimeOfDay` — раздельно (ScenarioRunner, меню A «Обстановка»). Событие шины `TimeOfDayChanged` (C1-детектор).
- Небо `SkyboxHorizonFog`: `_SkyNight`, процедурные звёзды (512² ячеек на грань, плотность 0,0032), луна, свечение сумерек — глобалы ставит `SkyWeather`. Отражения — `EnvironmentProbe` + `EnvironmentReflection` (перерисовка только на переходах, по грани за кадр).
- Мостик: `BridgeLighting` — свет рубки выкл/красный/белый (лампы × lerp(0,15; 1; Darkness)), подсветка приборов 0–100 % (шторки поверх NavDisplay и WeatherPanel, сигнализация ≥ 60 %, emission шкалы телеграфа), отсвет своих огней. Тумблер «СВЕТ» на пульте; меню «Параметры» на Y: свет рубки, подсветка, экспозиция ночи. Места ламп/тумблера — приблизительные, правятся в Scene View.
- URP (`Performance URP Config`, `Quality URP Config`): дополнительные источники **Per Pixel, до 4 на объект** (с B4; вода их не считает).
- Тесты: `EnvironmentTests` (16); всего 141.
## Оценка действий (запрос 10, C2)
- Core `Core/Assessment/`: `AssessmentSettings` (`AssessmentCatalog` — 20 критериев E1–E9, R1–R8b, A1–A2; пороги full/pass/zero, веса, обязательность — предварительные), `DetectionModel` + `WeatherVisibility` (плотности тумана пресетов — единственный источник, их берёт и WeatherController), `EncounterClassifier` (правила 13–15, 17), `ExerciseAssessor`, `AssessmentContext` (из SessionMeta), `AssessmentResult` (панель, assessment.csv), `LiveAssessment` + `SessionAssessment` (в шлеме и по записи — бит в бит одинаково: срезы 5 Гц тем же кодом, что track.csv, округление `SessionFormat.Quantize`), `ExerciseSimulation` + `DemoAssessmentRuns` (прогоны за обучающегося). Ассет `AssessmentCriteria` (Scenarios/) — набор; `Assessment_Default.asset` создаёт Setup C2.
- Сцена: `ExerciseAssessmentMonitor` (Scenario, −83) — события `TargetDetected`, `EncounterStarted` в шину; итог — панель результатов (и при досрочном), `assessment.csv` + `session.json → assessment`, снимок порогов `assessment_settings`; меню Y «Текущий балл». Срез состояния — `ExerciseRecorder.BuildTrackSample/BuildTargetSamples` (общий для записи и оценки).
- Сценарий: `detectionRangeNm`, у точки — `narrowChannel` (правило 9), у цели — `requiredCpaNm` (B3-01: T1, T3 — 0,05 мили). Маршрутные критерии не учитывают манёвр расхождения; ситуация переопределяется при повороте цели или своего судна по маршруту; с одной целью — несколько сближений.
- Отчёт `Tools/VR Bridge/Assessment Report` → `ThesisData/Assessment/<дата>/`; Session Replay — пересчёт оценки по записи и сверка с assessment.csv. F8: примечание (note) собирается само (сценарий, погода, время суток, переход, перерисовки пробы, запись).
- Тесты: `AssessmentTests` (17); всего 158. Методика, формулы, пороги — ThesisNotes, раздел 10.

## Разбор и отчёт (запрос 11, C3)
- Core `Core/Debrief/`: `DebriefText` (тексты нарушений по кодам каталога — новый критерий без текста ловит тест; строки и страницы таблицы критериев), `DebriefMap` (описание схемы: фигуры в восток/север, виды «весь маршрут» + окно на каждое завершённое сближение), `MapGeometry`/`MapRect` (отсечение, триангуляция, прореживание), `DebriefSvg` (схема SVG, ч/б), `SvgPlot` (**перенесён из Editor/TrialsReport.cs**, вывод прежних графиков тот же; NaN — разрыв линии, `AddMark`, `ClipId`), `SessionReport` (`report.html`, A4, без внешних ресурсов; `BuildForFolder` — офлайн, только главный поток), `ExperimentSummary` (сводные CSV), `DebriefBuild` (фоновое построение). `Core/Recording/SessionArea` — снимок акватории в `session.json` (`scenario.area`).
- В шлеме: `ExerciseRecorder` после закрытия записи запускает `DebriefBuild` (файлы записи + готовый итог оценки; офлайн-оценка в фоне нельзя — создаёт ScriptableObject) → `LastDebrief`, `report.html` в папке сессии; флаг `buildDebrief`. `ScenarioPanel` — результаты на вкладках «Итог / Критерии / Схема» (`criteriaPerPage`, `mapHeight`, `maxViolationsOnSummary`, `criteriaColumns`); схема — `DebriefMapGraphic` (MaskableGraphic, цвет вершин, без текстур).
- `EncounterSummary.TargetKey` — id цели в targets.csv (`TargetId` — подпись, «T3 (2)»).
- Редактор: Session Replay → «Отчёт (report.html)», «Сводка по папке…»; `Tools/VR Bridge/Sessions Summary…` → `ThesisData/Summary/<дата>/`; `Tools/VR Bridge/Debrief Report` → `ThesisData/Debrief/<дата>/` (прогоны с report.html, схемы SVG, сводка). Окно D1 — через `SessionReport.BuildForFolder`.
- Тесты: `DebriefTests` (11); всего 169 до C4. Подробно — ThesisNotes, раздел 11.
- 12.10.2026, по проверке в шлеме: вкладка «Схема» ставила Unity на паузу (нет `CanvasRenderer` — базовый `Graphic` в этой uGUI его не требует → `[RequireComponent]`), затем вешала Unity (бесконечный цикл штриховки → `MapGeometry.DashRuns`, тест `Geometry_DashRuns_TerminateAtRoundingEdge`). После исправлений проверено в шлеме, 190/190 — C3 закрыт. Номера разделов report.html — сквозные (`SessionReport.NumberSections`).

## Ограниченная видимость и туманные сигналы (запрос 12, C4)
- Видимость сценария (`visibilityMeters`) / инструктора (меню A «Обстановка» → «Видимость: < по погоде / 4 / 2 / 1 / 0,5 / 0,25 мили >») → `WeatherController.SetVisibility`: плотность тумана ρ = 1,73/V (`WeatherVisibility.DensityFor`) заменяет пресет; 0 — по погоде (как до C4). Событие `VisibilityChanged`. Пресеты не менялись; пресет «Туман» и слой тумана на небе — после просмотра в шлеме.
- Огни: порог освещённости днём 10⁻³ лк, ночью/в сумерках 2·10⁻⁷ (`NavigationLights.ThresholdFor(Darkness)`, `NavLightField.ThresholdLux`) — днём в тумане огонь не виден дальше корпуса; ночь и сумерки бит в бит прежние.
- Тифон: `ShipHorn.holdToSound` — звучит, пока держат (петля `HornSynth.Loop`, нарастание 0,12 с, спад 0,3 с, защита 30 с); звук 180 Гц не менялся (решение пользователя). Core `Core/Signals/SoundSignals`: тип звука (короткий 0,3…1,5 + Δ с, продолжительный 4 − Δ…6 + Δ с, Δ = 0,5 — параметр оценки), сигнал — звуки с паузой ≤ 3 с; распознавание правил 34, 35. `HornEnded.Index` — тип звука, событие `SoundSignal`.
- Сигналы целей: `Core/Signals/FogSignals` + `FogSignalSystem` (сцена — `TrafficController`, прогон — `ExerciseSimulation`): при V < 2 миль «—» на ходу / «— —» без хода раз в 110 с (первый — 20 + 35·i с), слышимость и частота по Приложению III, задержка d/340 с; событие `TargetSoundSignal` в момент прихода; звук с мачты (`TrafficShipView.PlaySignal`): громкость −30 дБ на границе слышимости, фильтр 5000 → 1500 Гц, без HRTF (только лево/право).
- Оценка: группа `Visibility` (вес 0,3) — F1 интервал своих сигналов ≤ 120 с (обяз.), F2 тип по ходу, F3/F4 безопасная скорость (`ScenarioDefinition.safeSpeedKnots`), F5 «Самый малый вперёд» после сигнала цели впереди траверза (19 д), F6 не влево на невидимую цель (19 г). Цель «видна» (`TargetTrack.Seen`) ≠ «обнаружена» (радар сценария); опасность с невидимой целью — `EncounterSituation.RestrictedVisibility` (нет привилегированного). Перекладки, пока известная цель не видна, маршрутом не штрафуются.
- `ScenarioDefinition.hideTargetsOnPlotter` (session.json `hide_targets`) — CPA/TCPA не показываются в «Текущем балле» (и на планшете D2); `safe_speed_kn` — в session.json, линия на графике скорости report.html.
- Сценарий `Scenario_C4-01_Fog.asset` (Setup C4 Fog): день, штиль, 926 м, U_без 6 уз, старт (0, −3000) «Средний вперёд», маршрут до входа в фарватер; T1 — Mariner справа, CPA 0, TCPA 6 мин, CPA_доп 0,2 мили. Прогоны `DemoAssessmentRuns.C4Variants` (правильно 100 / без сигналов 85,7 / не снизил 77 / влево 69,9).
- Тесты: `FogTests` (21); всего 190 (с тестом штриховки схемы). Проверено в шлеме (корпус цели выходит из тумана около V) — C4 закрыт 12.10.2026. Подробно — ThesisNotes, раздел 12.

## Окно инструктора и вводные (запрос 13, D1)
- Монитор: `Instructor` (корень) — **InstructorStation** (меню Y «Монитор инструктора»: окно инструктора ⇄ зеркало шлема, PiP; в режиме окна зеркало выключается `XRDisplaySubsystem.SetPreferredMirrorBlitMode(None)` — его читает URP — и `XRSettings.gameViewRenderMode = None`, своя камера без сцены, не XR) + **InstructorWindow** (Screen Space Overlay, 1920×1080: верхняя строка — сценарий, ID, Начать/Пауза/Перезапуск/Завершить/Свободное; карта — `DebriefMapGraphic` (`DebriefMap.BuildStatic`) + **InstructorMapGraphic** (суда, следы, векторы 3 мин), `MapViewport` (Core: масштаб к курсору, сдвиг, следование с перецентровкой 0,6); справа — состояние, цели (CPA/TCPA, ситуация из оценки), вводные, погода/время суток, события, «Текущий балл») + **InstructorPip** (вид из шлема 480×270, 10 Гц, по кнопке; по замеру оставлен — с PiP 90 Гц, табл. 13.2; режим «окно» без PiP дал провалы ~100 мс — не выяснено). Обновление 5 Гц. Мышь — `XRUIInputModule.m_EnableMouseInput` (Setup D1).
- Вводные: `Ship/InstructorInputs` — отказ ГД (`ShipNavigation.EngineFailed` → `ShipDynamicsModel.EngineFailed`: u_зад = 0, как «Стоп»), отказ руля (`SteeringFailed`: δ заклинило), видимость; события `EngineFailure/Restored`, `SteeringFailure/Restored` (ByInstructor); снимаются при загрузке/начале/конце упражнения. Оповещение обучающегося — меню Y «Оповещения» → «Оповещение об отказах: вкл/выкл» (сигнализация 1200 Гц до квитирования, надпись, строка на NavDisplay). Меню A: подменю «Вводные», «Пауза (инструктор)» — на главной («Сценарий»), «Квитировать сигнализацию отказа» — на главной, пока звучит; «Оповещение об отказах» — меню Y «Оповещения».
- Пауза: `ScenarioRunner.Pause/Resume/IsPaused/PauseChanged` (через `ShipNavigation.Paused`), события `ExercisePaused/Resumed`; надпись «ПАУЗА» в шлеме. Пока фокус в поле ID — клавиши 1–4 погоды не действуют (`InstructorStation.TextInputFocused`).
- Оценка: N1 (отказ руля → снижение хода), N2 (отказ ГД → «Стоп»), группа Reaction, 15/30/60 с; время отказа исключено из R1–R3, R8a. Прогоны `DemoAssessmentRuns.FailureVariants` (B2-01) — в Assessment Report. Вводные — на графиках и схеме report.html (`MapLayer.Failure`).
- Тесты: `InstructorTests` (10); всего 200 (Unity Test Runner 200/200). Проверено в шлеме — D1 закрыт 12.10.2026. Подробно — ThesisNotes, раздел 13 (итог — 13.15).

## Навигационный планшет (запрос 14, D2; D3 — без вибрации на события)
- `NavPlotter` (корень сцены, Setup D2 Plotter): холст строится в коде и крепится к `Left Controller` XR Rig (0,30 × 0,20 м, 1 ед. = 0,5 мм; `handOffset`, `faceHead` — лицом к глазам). Кнопка **X** левого контроллера / меню Y «Общее» → «Навигационный планшет»; пока левая рука держит орган управления — скрыт сам. Скрытый ничего не обновляет.
- Карта — те же слои, что окно инструктора: `DebriefMapGraphic` (`DebriefMap.BuildStatic`) + `InstructorMapGraphic`; суда и следы — общий `LiveMapTracks` (вынесен из InstructorWindow). Шкала 0,25…8 мили (от центра до верха окна), «Север вверх» / «Курс вверх» (поворот `RectTransform` слоя вокруг судна, слой больше окна — `PlotterMath.Layout`), «Весь маршрут». 5 Гц, данные 2 Гц. Ночь — `BridgeLighting.RegisterInstrumentPanel`.
- Цели: в брифинге и упражнении — только обнаруженные (оценка C2), при `hideTargetsOnPlotter` — никаких; свободное плавание и итог — все. «Все цели» — окно инструктора (кнопка «Планшет: …») и меню A «Отладка»; событие `PlotterRevealTargets` (в конец enum) в журнал.
- Core: `Core/Plotter/PlotterMath` (шкала, правило показа целей, точка, опасная цель, раскладка), `MapViewport.Fit(r, pad, w, h)` / `SetMetersPerPixel`. Замер: в note — «планшет: скрыт / север вверх / курс вверх».
- D3: вибрация на события не делается (решение пользователя, ThesisNotes 14.9); встроенная вибрация XRI при наведении/захвате — без изменений.
- Вкладка «Задание» (кнопка «Задание ⇄ Карта»): ход упражнения, условия — `ExerciseTasks.BriefingFacts` (общий текст с панелью брифинга ScenarioPanel), брифинг целиком, листание; без подсказок (ситуации МППСС, безопасная скорость числом, таймер туманного сигнала). «+» — крупнее, «−» — мельче.
- Тесты: `PlotterTests` (7, из них 1 × 6 случаев); всего 216. Проверено в шлеме — запрос 14 закрыт 12.10.2026. Подробно — ThesisNotes, раздел 14 (правки — 14.13, итог — 14.14).

## Графики в разборе (запрос 17)
- Core `Core/Debrief/DebriefPlots.cs`: `DebriefPlots.Build(RecordedSession, AssessmentResult)` → 4 `DebriefPlot` (курс / скорость / XTE / руль; ряды, отметки манёвров, аварий, вводных, **паузы**), `PlotAxes` — диапазоны и деления осей (их же берёт `SvgPlot.Render`). `SessionReport` рисует раздел «Графики» по ним (`ToSvg`); `DebriefBuild.Plots` — для панели. report.html без паузы — бит в бит прежний (14 прогонов сверены).
- Сцена: `Scripts/Scenarios/DebriefPlotGraphic.cs` (MaskableGraphic, как схема); `ScenarioPanel` — 4-я вкладка «Графики», один график на поле `mapHeight`, листание «< курс · 1 / 4 >». На планшете графиков нет (решение пользователя). Setup не нужен.
- Тесты: `DebriefTests` +2 (`Plots_SeriesFromTrack_BreaksMarksSafeSpeed`, `Plots_ReportAndPanelSameSeriesAndAxes`); всего 218. Проверено в шлеме — закрыт 12.10.2026. Подробно — ThesisNotes, раздел 17 (итог — 17.8).

## Модель 3 СС для малых скоростей (запрос 19.1 — буксир и швартовка, часть 1)
- Запрос 19 разбит: **19.1** модель 3 СС (код готов 15.10.2026), **19.2** причал, кранцы, концы (перед началом — решить, какое судно швартуем), **19.3** буксиры, ветер, оценка (Plan.md).
- Core `Core/Manoeuvring/Manoeuvring3Dof.cs`: `ClarkeDerivatives` (регрессия Clarke 1983, MSS clarke83.m), `HullForces` (Хёрнер, Söding), `Manoeuvring3DofCoefficients` — согласование: k_N (демпфирование рыскания) и Y'_δ по K', T' ассета; k_∞ — множитель C_d на ходу (≥ 0,5·V_эксп, переход от 0,25·V_эксп) по диаметру установившейся циркуляции 35° модели ассета; кэш по значениям параметров (построение 3–90 мс). Центробежные −m·u·r добавлены явно; N_v — с u со знаком (с |u| было ложное вращение при дрейфе). `LowSpeedTrials` — дрейф, разворот на месте, «толчок» с обдувом руля и без, задний ход; `CompareMotionModels`.
- `ShipDynamicsModel`: `V` (поперечная, > 0 — ПБ), `MotionOverride` / `ActiveMotionModel` / `Uses3Dof`, `AddForce(fx, fy, xPoint)` / `AddMoment` (сбрасываются после шага; Номото их игнорирует), `VelocityEast/North`, `SpeedThroughWater`. При Course1Dof — бит в бит прежняя модель.
- `ShipParameters`: `motionModel` (по умолчанию Course1Dof у всех ассетов — цели и прогоны не меняются), `yawGyrationRadiusL` 0,25, `centreOfGravityX` 0, `crossFlowDragCoeff` 0 (= Хёрнер), `matchTurningCircle` вкл., `rudderLeverL` −0,5, `rudderSlipstreamFactor` 0,5, `propellerSideForceFactor` 0,05.
- Сцена (Setup не нужен): `ShipNavigation.motionModel` (AsAsset), меню A «Судно» → «Модель движения: Номото / 3 СС» (вне упражнения), «Отладка» → «Сила в борт», «Пара сил» (3 СС, вне упражнения; `debugForceTonnes`, 0 — 0,002·Δ·g); NavDisplay — строка «лагом»; `session.json → ship.motion_model`; `cog_deg` — по скорости относительно грунта при V ≠ 0.
- Стенд: окно Manoeuvring Trials — «Модель движения», «Малые скорости», «Сравнение моделей» → `lowspeed_*`, `comparison_3dof.*`, `model_3dof.md`.
- Тесты: `LowSpeedModelTests` (14 с TestCase); всего 232. Подробно — ThesisNotes, раздел 19.

## Сборка (после запроса 17)
- Пока только PC (Windows) — пробная сборка для проверки окна инструктора, PiP и замера вне редактора; инструкция — `Docs/Build.md`. В сборке: сессии — `Документы/VRBridgeTrainer/Sessions`; замеры — `<папка сборки>/ThesisData/Performance` (путь от `Application.dataPath`); лог — `%USERPROFILE%/AppData/LocalLow/DefaultCompany/VR_BridgeTrainer_1/Player.log`; список сценариев — сохранённый в сцене (`ScenarioLibrarySync` работает только при входе в Play Mode).
- Уровень качества: редактор играет на «Low» (`Performance URP Config`), а по умолчанию для Windows стоял «Ultra» (`Quality URP Config`, VSync) — для сборки выставить «Low», чтобы сборка совпадала с замерами в редакторе.
- Автономная версия (Quest 3, Android) — запрос 20 в Plan.md (черновик).

## Быстроходный катер (запрос 15)
- Core: `ShipPresets.FillFastBoat` (единственный источник значений: L 18 м, 35 уз, B 4,8, d 1,1, K'/T' 1,0/1,2, руль 10 °/с; источники — `ShipParameters_Sources.md`), `Scenarios/ScenarioVariants` (копия сценария на другом судне: id «-FB», начальная скорость v₀·U_уст'(SF)/U_уст(SF), исходник для перезапуска, кэш).
- Сцена: `Ship` — **ShipTypeSwitcher** (меню A → «Судно »» → «Тип судна: < модель 45 м / катер >», только вне упражнения; в брифинге сценарий перезагружается вариантом). Катер: `Ship/FastBoat` (модель `BoatMeshes`, ассет `Assets/Ocean/Materials/FastBoat/`, коробки HullBox/BowBox для воды, коллайдер палубы; выключен, включает переключатель); от мостика остаётся рулевая стойка (органы управления — те же объекты), остальные рендеры скрываются (`hideOnBoat`, коллайдеры остаются — у XR Rig гравитация), лампы рубки и RainShelter выключаются; судно опускается (`ShipMotion.SetHullFrame`: палуба ≈ 1,2 м над водой вместо ≈ 5 м), центр качки — мидель катера; отсветы огней — на места огней катера.
- `ScenarioRunner.Load` берёт `ShipTypeSwitcher.VariantFor(сценарий)`.
- Setup: `Tools/VR Bridge/Setup Fast Boat` → Ctrl+S; стойка из МОСТИКИ выделяется геометрически (лог: пол под игроком, найденные части и «почти попавшие»); правка — выделить в Scene View → `Tools/VR Bridge/Fast Boat — оставить / скрыть выделенное` (запоминается в `forceKeep` / `forceHide`), просмотр — `Fast Boat — предпросмотр стойки (Scene View)`. На катере гашение волн у борта 90 % / 2 м (у мостика 25 % / 8 м — с низкой палубы виден «срез»), при выходе из Play Mode возвращается.
- Тесты: `FastBoatTests` (4); всего 204 (Unity Test Runner 204/204). Setup Fast Boat выполнен, сцена сохранена, проверено в шлеме — закрыт 12.10.2026. Подробно — ThesisNotes, раздел 15 (итог — 15.9). Ограничение: модель Номото без глиссирования — катер для отладки, не для оценки.
## Качка на волнах (запрос 4)
- Поле волн — чистый класс `Core/Seakeeping/OceanWaveField` (единый источник для шейдера и C#). `OceanWaves` заполняет его (`ConfigureField`) и шлёт те же значения в шейдер. Фазы θ продвигаются в **FixedUpdate**; в кадре — перенос на время отображения `OceanWaves.RenderTimeOffset` = −(1 − α)·Δt (та же α, что в ShipNavigation).
- Порядок выполнения (D1-скрипты — по умолчанию, 0): WorldOrigin −100 → NavLightField −95 (B3) → ShipNavigation −90 → ScenarioRunner −88 (B2) → BridgeActionMonitor −87 (C1) → TrafficController −86 (B3) → GroundingMonitor −85 (B1) → ExerciseRecorder −84 (C1) → ExerciseAssessmentMonitor −83 (C2) → OceanWaves −80 → ShipMotion −70, TrafficShipView −70 (B3) → WaterAreaView −60 (B1) → AidLightsView −55 (B3).
- Высота воды для других объектов (буи B1, суда B3): `OceanWaves.Instance.SampleHeight(WorldPosition)` / `SampleWaterLevel(Vector3 сцены)` — обратная задача Герстнера, 3 итерации.
- Мореходные поля ShipParameters (B, d, GM, C_B, C_WP, T_φ/T_z/T_θ, ζ) заполнены только у `Model45m` (Setup Seakeeping, допущения помечены в `ShipParameters_Sources.md`).
- Шейдер у борта по-прежнему гасит волны к нулю (`_HullDampMin`): если в шлеме корпус «отрывается» от воды — правка «вода у борта → плоскость ватерлинии судна» (отложена до проверки).
- Дождь (запрос 1) **отложен**: частицы есть (~3900), но в шлеме не видны. После исправления текстуры/очереди Setup Rain ещё не перезапускался — начинать с этого.

## Ключевые скрипты (`Assets/Ocean/Scripts`)
- `ShipMotion` — качка (запрос 4). Режим `Waves` (по умолчанию): в FixedUpdate вода сэмплируется в сетке 5×3 точек корпуса, плоскость МНК → звенья 2-го порядка (`Core/Seakeeping/SeakeepingModel`, периоды и ζ — в ShipParameters), отображение интерполируется как в навигации, поворот вокруг `pivotLocal` (мидель, ватерлиния). Комфорт: `comfortScale`, мягкие пределы `maxRollDeg`/`maxPitchDeg`. Режим `Sinusoidal` — прежние синусоиды. Каждый кадр пишет `_WaveIntensity` = seaState в материал. Время шага — `stepTimeUs` в инспекторе.
- `SteeringWheel` — угол руля ±35° (`RudderAngle`), пока никем не читается.
- `EngineTelegraph` — 9 положений, `SpeedFactor` −1..1, пока никем не читается.
- `WeatherController` — пресеты calm/moderate/storm/night (клавиши 1–4 — только при `keyboardShortcuts`, D1), плавный переход; меняет свет, туман, `_WaveIntensity`, `_WaveSpeed`. Туман ночи с B3 — 0,00015. С B4 — две оси: погода (`SetWeather`) и время суток (`SetTimeOfDay`), `SetPreset` — как до B4 (Night → ночь, остальные → день); единственный писатель RenderSettings (см. раздел B4).
- `WorldOrigin`, `OceanWaves`, `OceanRadialMesh` — см. разделы выше/ниже.
- `ShipHorn`, `AmbientSounds`, `PanelToggle`, `HeightAdjust` — окружение и UI.
- `WeatherController` также ведёт `rain` (0..1) в пресетах: storm = 1, остальные 0 → `RainEffect.Intensity`, тем же SmoothStep, что и погода.
- `RainEffect` — дождь: мировое пространство, излучатель над камерой, скорость капли = ветер (OceanWaves.WindDirection · 15 м/с) − ход судна + падение 6.5 м/с; материал `Assets/Ocean/Materials/Rain.mat` (очередь 3100 — после воды).
- `FrameTimingCapture` — замер производительности (FrameTimingManager + ProfilerRecorder).

## Вода
- Шейдер `Assets/Ocean/Shaders/OceanSurface.shader` (`VRBridgeTrainer/OceanSurface`): 4 волны Герстнера. Параметры волн — в `OceanWaves` (C#), передаются глобалами `_OceanWave*`; фазы (смещение судна + время) считаются в C# в double mod 2π. Затухание `_WaveFadeStart`/`_WaveFadeEnd` (1.5→6 км) + подавление волн мельче шага сетки/пикселя. Нормали попиксельно. Туман URP поддерживается.
- Ветер: `OceanWaves.windFromDeg` (морской пеленг «откуда», по умолчанию 160°), волны веером вокруг него. Влияние `_WaveSpeed` ограничено `speedInfluence` = 0.25 (физическая дисперсия).
- Рябь: 4 волны λ 4.1…0.75 м только в нормалях. Сила — `detailStrength` (0.6 в коде; в сцене 0.12 после `Weaker Ripple`, B1). Барашки: шум 240 м, привязан к воде, только на гребнях; два слоя попеременно гаснут/появляются (`whitecapLifetime` 7 с), покрытие растёт с волнением (`_FoamAmount`).
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
- **Реестр «закадровых» материалов** — `Docs/ThesisArtifacts.md`: генераторы, папки ThesisData, CSV/SVG/HTML и фоновые процессы, которых не видно в шлеме. После каждого запроса дописывать туда новые.
- **Формат CSV**: числа — `CultureInfo.InvariantCulture` (точка), разделитель `;`, кодировка UTF-8 с BOM, первая строка — заголовки с единицами (`heading_deg`, `speed_kn`, `t_s`…).
- **Время в симуляции — симуляционное** (FixedUpdate / Time.fixedDeltaTime), не реальное. Исключение — замеры производительности (FrameTimingCapture).
