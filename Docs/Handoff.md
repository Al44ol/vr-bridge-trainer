# Передача контекста в следующий чат

Обновляется в конце каждого чата. Новый чат начинать так:

> Перечитай CLAUDE.md, Docs/Handoff.md и Docs/ThesisNotes.md. Двигаемся по Docs/Plan.md: запрос N. Сначала план, код — после моего «ок».

C3 (запрос 11) — код закоммичен: разбор в шлеме (вкладки «Итог / Критерии / Схема»), report.html после каждого упражнения (фон), отчёт для любой сессии и сводка по папке в редакторе, Debrief Report для ВКР. Вне Unity: 168 тестов, сборки без ошибок и предупреждений; пример report.html просмотрен в браузере. Сцену C3 не правит (Setup не нужен). **Ожидают:** Test Runner 168/168, Debrief Report, проверка разбора в шлеме (чек-лист C3 ниже), коммит данных `ThesisData/Debrief/`.

C2 (запрос 10) закрыт: тифон звучит (повторный Setup Horn Button, коллайдер не триггер — в логе и в сцене), Test Runner 158/158, Assessment Report `20261011_224407` — цифры совпали с табл. 10.3. Исправлено оформление `summary.md` (пороги/веса округлялись до целых, «|XTE|» ломал таблицы) — **Assessment Report перезапустить** и закоммитить новую папку (старую `20261011_224407` можно удалить, она не в git). Итоговая панель C2 в шлеме заменена разбором C3.

### Чек-лист проверки C3 (запрос 11)
1. После компиляции: Test Runner → EditMode → **168/168** (если 158 — смотреть консоль: ошибка компиляции; новые файлы: `Core/Debrief/*.cs`, `Core/Recording/SessionArea.cs`, `Scripts/Scenarios/DebriefMapGraphic.cs`, `Editor/DebriefReport.cs`, `Tests/Editor/DebriefTests.cs`; .meta созданы вручную).
2. `Tools/VR Bridge/Debrief Report` → `ThesisData/Debrief/<дата>/`: в `summary.md` таблица прогонов (B3-01: 45,3 / 95,5 / 64,4 / 74,7; B2-01: 99,5 / 65,1; построение — десятки мс), сводная таблица «6 строк», у каждого прогона «совпадает с оценкой в шлеме»; открыть `sessions/B3-01_port/report.html` в браузере (без интернета), Ctrl+P → A4: таблицы и рисунки не режутся.
3. Шлем, B3-01: пройти до конца (или «Завершить досрочно»). Панель разбора: вкладка «Итог» — оценка, причина незачёта, сближения строками, нарушения с правилами; «Критерии» — таблица, листание «< стр. 1 / N >»; «Схема» — сначала, возможно, «Схема строится…» (доли секунды), затем схема: маршрут и коридор, буи, мели, траектории (своё — голубая, цели — оранжевая/розовая/жёлтая), кружок «манёвр (T2)», отрезок наименьшей дистанции с подписью; «< весь маршрут / сближение с T2 … >». Читается ли, не мелко ли (подписи 14), высота панели удобна?
4. В консоли `[Разбор] …\report.html (N мс, фоновый поток)`; в папке сессии `report.html` открывается.
5. Session Replay: у старой сессии (до C3) — «Отчёт (report.html)» строит отчёт (акватория из ассета); «Сводка по папке…» → `ThesisData/Summary/<дата>/sessions_summary.csv` открывается в Excel (разделитель «;», точка).
6. F8 при открытой вкладке «Схема» — 90 Гц (по желанию).
- Тонкая настройка: `ScenarioPanel.mapHeight` (500), `criteriaPerPage` (10), `maxViolationsOnSummary` (6), `criteriaColumns`; цвета — `DebriefMapGraphic`.

### Чек-лист проверки C2 (запрос 10)
1. `Tools/VR Bridge/Setup C2 Assessment`. В консоли должно быть:
   - `+ Assets/Ocean/Scenarios/Assessment_Default.asset: 20 критериев`;
   - `B2-01`/`B3-01`: узкость на 2 участках, брифинг B2-01 — правая сторона, T1/T3 — CPA_доп 0,05;
   - `+ ExerciseAssessmentMonitor`, `ScenarioPanel → ExerciseAssessmentMonitor`.

   Затем `Tools/VR Bridge/Setup Horn Button`: «старый диск отключён», модель, HornButtonVisual. **Ctrl+S.**
2. Test Runner → EditMode → **158/158**. Если 141 — смотреть консоль: ошибка компиляции где-то в проекте (в первый раз была в SessionReplayWindow — исправлено).
3. `Tools/VR Bridge/Assessment Report` → `ThesisData/Assessment/<дата>/`. Цифры summary.md должны совпасть с табл. 10.3 ThesisNotes:
   - B3-01 «правильно» — 95,5, зачёт; «влево» — 64; «поздно» — 75; без манёвра — провал;
   - B2-01 — 99,5 / 65;
   - «Совпало критериев N из N», assessment.csv «совпадает».
4. Шлем, B3-01 (ночь):
   - в консоли `[Упражнение]` «Обнаружена цель «T1/T2»», «T2: пересечение курсов, цель справа — уступить дорогу…»;
   - отвернуть вправо от T2 → после расхождения меню **Y** → «Текущий балл»: панель с баллом, критериями T2 и CPA/TCPA целей;
   - в фарватере держаться правее оси, T3 обгонять левым бортом;
   - в конце — панель результатов с «Оценка: … из 100 — ЗАЧЁТ/НЕЗАЧЁТ» и списком критериев (не слишком ли высокая панель?);
   - «Завершить досрочно» — тоже итог, R4/R5 «не оценивался»;
   - поворот влево на T2 — E4/E5 «×».
5. Session Replay: у новой сессии — блок «Оценка по записи (C2)», строка «совпадает с оценкой в шлеме»; в папке сессии `assessment.csv` (Excel), в session.json — `assessment` и `assessment_settings`.
6. Кнопка «ТИФОН»: корпус на пульте (не висит, не утоплен — иначе подвинуть `HornButton/HornButtonModel` по Y), колпачок утапливается при нажатии, подсвечивается при наведении, светится при гудке, ночью тлеет; гудок звучит как раньше.
7. F8 (по желанию): ночью в B3-01 и во время перехода день → ночь (меню A «Обстановка» → время суток) — в `frametiming_summary.csv` столбец note: «… -> …, переход освещения: да, перерисовок пробы: N». Цифры — в табл. 9.2.

### Запрос для следующего чата (закрыть C3, затем запрос 12, C4) — скопировать целиком

```
Перечитай CLAUDE.md, Docs/Handoff.md и Docs/ThesisNotes.md.

Сначала закрываем C3 (чек-лист C3 в Handoff): результаты Test Runner (ждём 168/168), Debrief Report и перезапущенный Assessment Report — посмотри сам (TestResults.xml, ThesisData/Debrief, ThesisData/Assessment); что я сообщу по шлему (разбор: вкладки, схема, читаемость) — исправь и допиши в ThesisNotes раздел 11; закоммить данные отчётов.

Затем двигаемся по Docs/Plan.md: запрос 12 (C4, ограниченная видимость и туманные сигналы). Сначала план, код — после моего «ок».

Уточнения по итогам C2–C3:
- Видимость сценария уже есть полем (visibilityMeters) и учитывается в обнаружении (DetectionModel, WeatherVisibility); правило 19 в оценке пока не реализовано (ThesisNotes 10.14, п. 6) — в тумане нет привилегированного судна.
- Гудок сейчас фиксированные 3 с (ShipHorn, события HornStarted/HornEnded) — «гудок, пока держат» и типы сигналов по правилам 32–35.
- Новые критерии — в AssessmentCatalog + текст нарушения в DebriefText (тест это проверяет).

В конце: список файлов, ручные шаги, что проверить в шлеме, запись в ThesisNotes.md, коммит и push.
```

### Чек-лист проверки B4 (для повторной проверки, если понадобится)
1. `Tools/VR Bridge/Setup B4 Night` → в консоли: URP «… → Per Pixel, на объект … → 4» (по каждому ассету конвейера), «ссылка на запечённые данные … снята», «Reflection Probe / Light Probe Group отключён», Moon, EnvironmentProbe, BridgeLighting (лампы, отсветы, панелей 2, сигнализаций 1, шкал — по числу секторов барабана телеграфа), тумблер, SettingsMenu → **Ctrl+S**. Test Runner → EditMode → **141/141**.
2. **День** (умеренное): картинка близка к прежней; рассеянный свет теперь из цветов пресета (был от шаблона) — если мостик/вода заметно другие, подстроить `ambientSky/ambientGround` пресетов. Огней нет. Шторм днём — огни горят (видимость 0,62 мили).
3. **Клавиша 4 / «Ночь» / B3-01**: переход ≈ 16 с через сумерки (тёплое свечение у горизонта, первые звёзды), без вспышек и кувырка света. Ночью: тёмное небо со звёздами (не мерцают), луна слева по носу (330°, 22°), лунная дорожка на воде, **дневного блика нет**, жёлтой точки на горизонте нет (буи тёмные, огни буёв мигают). Огни T1/T2/T3 хорошо видны.
4. **Мостик ночью**: красный приглушённый свет, не слепит; NavDisplay тусклый, но читается; шкала телеграфа светится; полоса глубины ярче. Меню **Y** «Параметры мостика»: «Свет рубки: < выкл / красный / белый >», «Подсветка приборов: < 30 % >», «Экспозиция ночи: < 1,0 >» (подобрать: темно, но не чёрно). Тумблер «СВЕТ» у кнопки гудка: нажатие — свет вкл/выкл, рычажок перекидывается, индикатор горит/тлеет. Меню **A**: группа «Обстановка» — «Время суток», «Погода» (ночь + штиль, ночь + шторм — тучи закрывают звёзды и луну).
5. Отсвет своих огней: белый над баком, красный/зелёный на крыльях — если не там, подвинуть `Ship/BridgeLighting/Own*Glow` в Scene View; так же лампы `CabinLamp_*` и тумблер.
6. F8 ночью в B3-01 — 90 Гц (табл. 9.2 ThesisNotes, сравнить с 7.7); F8 во время перехода день → ночь (перерисовка пробы).
7. Запись: смена времени суток в меню во время упражнения → в events.csv `TimeOfDayChanged`.
- Тонкая настройка по желанию: `WeatherController.nightProfile` (луна, рассеянный, звёзды, блик воды, сумерки), `timeOfDayTransitionSeconds` (8 с); `BridgeLighting` (`redIntensity` 0,6, `backlight` 0,3, `alarmMinimum` 0,6, `daylightShare` 0,15); `EnvironmentReflection.minInterval` (0,25 с).

---
## Состояние на 11.10.2026 (после запросов 1–10)

| Запрос | Статус |
|---|---|
| 1. Правила журнала, SPI, дождь | Правила и SPI — готово, замер до/после в ThesisNotes 1.1. **Дождь отложен** |
| 2. A3.1 Стенд манёвренных испытаний | Готово, тесты 18/18 зелёные в Unity Test Runner |
| 3. A3.2 Калибровка | Готово: Mariner (основной) + KCS (проверка), Норрбин, потеря скорости; 32/32 теста вне Unity; ThesisNotes, раздел 3 |
| 4. Качка от реальных волн | Готово: проверено в шлеме, стенд запущен в редакторе (`ThesisData/Seakeeping/`) |
| 5. B1 Учебная акватория | Готово: проверено в шлеме, **Test Runner 67/67** (все тесты проекта), коммит |
| 6. B2 Движок сценариев | Готово: проверено в шлеме, **Test Runner 78/78**, коммит |
| 7. C1 Запись упражнения | Готово: проверено в шлеме, **Test Runner 89/89**, 90 Гц с записью, коммит |
| 8. B3 Другие суда и огни | Готово: **Test Runner 125/125**, Traffic Report, проверено в Play Mode; замечания (высота панели, стрелки 1/3/6 миль) исправлены, коммит |
| 9. B4 Ночь | Готово: **Test Runner 141/141**, проверено в шлеме, F8 — 89,9 кадр/с, коммит |
| 10. C2 Оценка действий | Готово: **Test Runner 158/158**, тифон звучит, Assessment Report сверен с табл. 10.3 (перезапустить после исправления summary.md) |
| 11. C3 Дебрифинг и отчёт | Код в коммите; вне Unity 168/168; ждут Test Runner, Debrief Report, проверка в шлеме |
| 12. C4 Ограниченная видимость | Следующий (после проверки C3) |

### Итоги запроса 11 (подробно — ThesisNotes, раздел 11)
- Core `Core/Debrief/` (8 файлов): `DebriefText`, `DebriefMap`, `MapGeometry`, `DebriefSvg`, `SvgPlot` (перенесён из `Editor/TrialsReport.cs`, вывод сверен со старой версией до символа), `SessionReport`, `ExperimentSummary`, `DebriefBuild`; `Core/Recording/SessionArea`. Сцена: `DebriefMapGraphic`. Editor: `DebriefReport` (Debrief Report, Sessions Summary…, `BuildReport`, `FindArea`). Тесты `DebriefTests` (10).
- Правки: `SessionFormat` (`ReportFile`, `scenario.area`, report.html в списке файлов), `AssessmentResult`/`ExerciseAssessor` (`EncounterSummary.TargetKey`), `ExerciseRecorder` (`buildDebrief`, `LastDebrief`, лог `[Разбор]`), `ScenarioPanel` (вкладки разбора), `SessionReplayWindow` (кнопки отчёта и сводки, `FindArea` → DebriefReport), `AssessmentReport` (`B2`/`B3` — internal; формат summary.md), `TrialsReport` (без SvgPlot).
- Решения пользователя: разбор — только в программе (вкладки), снимок акватории в записи, report.html автоматически, сводка — пороги из записи / текущий ассет.
- Решения по ходу: в шлеме отчёт — по файлам записи + готовый итог оценки (офлайн-оценка создаёт ScriptableObject — только главный поток); панель проверяет, что разбор от текущей сессии (папка); у схем/графиков уникальные `clipPath`; легенда схемы — в свободном углу; столбец `folder` в сводке (у прогонов вне Unity одинаковый session_id).
- Вне Unity: `scratchpad/coretest` (+ `Sample.cs` — примеры отчётов и замер табл. 11.3, `SvgOld.cs`/`SvgCompare.cs` — сверка SvgPlot), `scratchpad/unitybuild` (новые файлы добавлены, пути — прямыми слэшами: обратные в sed портятся). PowerShell 5.1 читает .ps1 без BOM как ANSI — кириллицу в скриптах правок не передавать (правки — через Edit).
- Просмотр HTML в браузере панели: файлы с диска — только статичный снимок; поднимался временный сервер `scratchpad/serve.ps1` (HttpListener, localhost:8765).
- .meta новых файлов созданы вручную.

### Итоги запроса 10 (подробно — ThesisNotes, раздел 10)
- Core `Core/Assessment/` (9 файлов): каталог 20 критериев и настройки, обнаружение, классификация, оценка, контекст, результат, оценка в шлеме и по записи, прогон за обучающегося. Сцена: `ExerciseAssessmentMonitor`, `HornButtonVisual`. Editor: `SetupAssessment` (Setup C2 Assessment, Setup Horn Button), `AssessmentReport`. Тесты `AssessmentTests` (17).
- Правки: `ScenarioDefinition` (`detectionRangeNm`, `RouteWaypoint.narrowChannel`), `TrafficDefinition` (`requiredCpaNm`), `SessionFormat` (`Quantize`, `assessment_settings`, `assessment`, `narrow_channel`, `detection_range_nm`, `required_cpa_nm`, `assessment.csv`), `CsvFormat` (`Round`, `Clean`), `SessionRecorder` (`WriteAssessment`), `ExerciseEvents` (`TargetDetected`, `EncounterStarted` в конце), `ExerciseSummary` (`ToPanelTextShort`), `AssessmentCriteria` (настройки), `DemoScenario` (узкость, брифинг — правая сторона; `assessment` больше не обнуляется), `DemoTrafficScenario` (CPA_доп T1/T3), `ExerciseRecorder` (общий срез, оценка в файлы), `ScenarioPanel` (итог, «Текущий балл» на Y), `WeatherController` (туман из `WeatherVisibility`), `EnvironmentReflection` (`Renders`), `FrameTimingCapture` (note собирается сам, `extraNote`), `ScenarioDefinitionEditor` (узкость по кнопке), `SessionReplayWindow` (оценка по записи).
- Решения пользователя: итог со списком — в конце (и при досрочном), «Текущий балл» — меню Y; правило 9 — критерий; пороги — предварительные; веса — на усмотрение (0,6 / 0,1 / 0,3, проходной 70); замер табл. 9.2 — ночь, B3-01.
- Решения по ходу (найдены прогонами B3-01): переклассификация при повороте цели или своего судна по маршруту (T1 в колене); «окончательно прошла» — по траверзу (T3); манёвр расхождения не штрафуется маршрутом; E1 — в сторону манёвра до CPA; E2 не оценивается при позднем возникновении опасности; R6 — от смещения на входе в зону; E9 — от возникновения опасности; несколько сближений с одной целью.
- Ошибка в первой сборке в Unity: `SessionReplayWindow` — символ BOM испортился при правке через perl (CS1012), из-за неё Unity не собрал новый код и Test Runner показывал 141. Исправлено.
- Вне Unity: `scratchpad/coretest` (+ `C2Print.cs`, `C2Measure.cs` — печать цифр и замер), `scratchpad/unitybuild` (копии .csproj, новые файлы внесены вручную).
- `.meta` для `AssessmentReport.cs` создан вручную.

### Итоги запроса 9 (подробно — ThesisNotes, раздел 9)
- Причина неудачной ночи: сцена ссылалась на запечённые данные шаблона VR Template (SampleScene/LightingData.asset) — рассеянный свет (SH), световые пробы и отражения дневные и не пересчитывались; цвета рассеянного света пресетов не действовали (режим Skybox); baked Reflection Probe; кубмапа неба лишь затемнялась; солнце на −15° и интерполяция через eulerAngles (кувырок); URP без постобработки и без дополнительных источников. Жёлтая точка — северный кардинальный буй под дневными пробами.
- Core `Core/Environment/EnvironmentModel.cs` (WeatherLook, NightProfile, EnvironmentLighting, Combine, LightsRequired). Сцена `Scripts/Environment/`: `EnvironmentReflection`, `BridgeLighting`, `BridgeLightSwitch`. Editor `SetupNight` (Setup B4 Night). Тесты `EnvironmentTests` (16).
- Правки: `WeatherController` (две оси, луна, Trilight, `_OceanNightSpec`, меню «Обстановка» и «Экспозиция ночи», без чтения состояния из сцены), `SkyWeather` (ночные глобалы), `SkyboxHorizonFog.shader` (ночной слой, звёзды, луна, сумерки), `OceanSurface.shader` (лунная дорожка), `NavLightField` (условие «темно» по Darkness), `BridgeMenu` (несколько меню, `InMenu`, `SettingsMenuId`), `HeightAdjust` (комментарий X/Y), `ScenarioRunner` (SetWeather + SetTimeOfDay), `ScenarioText` (`Conditions`, тексты пресета «Ночь»), `ScenarioPanel`, `ScenarioDefinition` (подписи), `ExerciseEvents` (`TimeOfDayChanged` в конце enum), `ControlActionDetector` (`ControlState.TimeOfDay`), `BridgeActionMonitor`.
- Решения пользователя: меню «Параметры» на Y + тумблер на пульте; дневную картинку проверить после Setup B4; прожекторы не делать (отложены до сценариев «неосвещённый знак / человек за бортом»), проблесковых огней у механического судна нет; манёвровый огонь (правило 34 б) — кандидат в C4; дополнительные источники URP — на моё усмотрение (включены Per Pixel, до 4).
- Решения по ходу: экспозиция — экранный профиль с одним коэффициентом (без постобработки); звёзды процедурные (без внешних ассетов); луна на 330° — в стороне от пеленгов целей B3-01; цепочка минимумов «сумерки ≤ день, ночь ≤ сумерки» — без вспышек в шторм.
- Вне Unity: 141/141 (`scratchpad/coretest` этого чата — копия из чата B3), сборки Assembly-CSharp и Editor компилируются (`scratchpad/unitybuild`, в копии .csproj добавлены новые файлы). Шейдеры компилирует только Unity — смотреть консоль после импорта.
- .meta новых файлов созданы вручную (Unity был в фоне).
- Не тестируется вне Unity: `BridgeMenu` (MonoBehaviour) — проверка меню Y только в шлеме.

### Итоги запроса 8 (подробно — ThesisNotes, раздел 8)
- Core `Core/Traffic/`: `TrafficDefinition` (TrafficShipDefinition перенесён из ScenarioDefinition.cs, поля B2 сохранены), `TrafficPlanner`, `CpaCalculator`, `HeadingAutopilot` + `RouteFollower`, `TrafficVessel` + `HullContact`, `NavigationLights` + `HullProfile` + `LightCharacter`, `DemoTrafficScenario`. Сцена `Scripts/Traffic/`: `TrafficController`, `TrafficShipView`, `TrafficMeshes`, `NavLightField`, `AidLightsView`; `Scripts/Scenarios/ExerciseFailureNotice`. Шейдер `Shaders/NavLight.shader`. Editor: `SetupTraffic` (Setup B3 Traffic, Rebuild Demo Traffic Scenario), `TrafficReport` (Traffic Report). Тесты `TrafficTests` (36).
- Правки: `ScenarioDefinition.Validate` (цели), `ExerciseEvents` (значения Collision, описание), `ExerciseSummary` (столкновения, `Failed`, `ToPanelText(markFailure)`), `ScenarioText` (тип судна, сторона, курсовой угол; ночь — 11,5 км), `SessionFormat` (`scenario.traffic`, `summary.collisions`), `ExerciseTargets` (комментарий), `ScenarioPanel` (`startAnchor`, провал красным, число судов на брифинге), `WaterAreaView` (`BuoyCount`, `BuoyTransform`), `WeatherController` (туман ночи 0,00015), `ScenarioDefinitionEditor`, `ScenarioRunnerEditor` (гизмо и ручки целей), `SessionReplayWindow` (траектории целей), `ShipParameters_Sources.md`.
- Решения пользователя: ночной туман ослаблен; танкер — только вид (параметры Mariner); ширина Mariner/KCS из источников; мостики **не трогаем** (оба остаются); после столкновения цель — «Стоп», оповещение «Упражнение провалено» (общее для мели и столкновения, переключатель в меню «Сценарий»); панель выбора при запуске — в фиксированной точке; огни знаков МАМС — сразу в B3.
- Вне Unity: 125/125 тестов (`scratchpad/coretest` этого чата: Stub.cs + Runner.cs, без UnityEngine), сборки Assembly-CSharp и Editor компилируются (`scratchpad/unitybuild` — копии .csproj с абсолютными путями + новые файлы).
- .meta новых файлов созданы вручную (Unity был в фоне).
- После проверки: Setup B3 выполнен (сцена, материалы `Materials/Traffic/`, `Scenario_B3-01_Traffic.asset`, B/d Mariner и KCS), Test Runner 125/125, Traffic Report → `ThesisData/Traffic/20261011_172124/` (цифры совпали с расчётом вне Unity). По замечаниям: панель выбора на уровне поручней (1,15 м, наклон к глазам; меню `Scenario Panel To Handrail Height`), отладочная цель — переключатель «< 1 / 3 / 6 миль >» (`BridgeMenu.RegisterStepper`).
- **Чек-лист проверки (для повторной проверки, если понадобится):**
  1. Tools/VR Bridge/Setup B3 Traffic → Ctrl+S; в консоли — материалы, Traffic, NavLights, AidLightsView, ScenarioPanelAnchor, B3-01, Mariner/KCS B. Test Runner → EditMode → 125/125. Tools/VR Bridge/Traffic Report → папка `ThesisData/Traffic/<дата>/`.
  2. При запуске панель выбора — в фиксированной точке перед игроком, читается; из меню A — перед камерой. Если точка неудобна — выделить `Ship/ScenarioPanelAnchor` и подвинуть в Scene View.
  3. B3-01: брифинг (ночь, «Другие суда в районе: 3»), «Начать» → в консоли `[Суда] … T1 … T2 … T3`. Ночью видны огни: T1 почти прямо по носу ≈ 3,6 мили — два топовых и красный (идёт 240°, к нам левым бортом), после её поворота в фарватере на юг — два топовых и оба бортовых; T2 справа ≈ 1,2 мили — топовые и красный (идёт на запад, к нам левым бортом); T3 впереди ≈ 2 мили — кормовой огонь (дальность 2 мили, ночью ≈ 1,7 — появится через 1–2 мин); огни буёв мигают по характеристикам.
  4. Не маневрировать: через ≈ 4 мин столкновение с T2 — удар, тряска, красная надпись «УПРАЖНЕНИЕ ПРОВАЛЕНО: столкновение с «T2»», результаты с красной строкой. Перезапустить и уступить дорогу T2 (вправо / сбавить ход) — без столкновения.
  5. Меню A → «Отладка» → «Проверка огней: облёт цели»: каждые 3 с — новый курсовой угол, в консоли — какие огни должны быть видны; сверить глазами (нос — оба бортовых, траверз ПБ — зелёный, корма — только кормовой). «Цель на 1 / 3 / 6 миль» — огни видны на 1 и 3 милях, на 6 — нет (ночь, V ≈ 6,2 мили: топовый ≈ 4,2).
  6. Огни не видны сквозь стены и крышу рубки; видны через окна. Днём (умеренное) огней нет («Огни: авто»), цели видны корпусами, качаются на волне; в шторм огни включаются.
  7. Край океана ночью (туман 0,00015) не виден; если виден — плотность ночи 0,0002 (WeatherController, пресет night).
  8. F8 во время B3-01 ночью — 90 Гц (сравнить с табл. 7.7). `TrafficController.stepTimeUs` в инспекторе.
- Тонкая настройка по желанию: `NavLightField.angularDiameterDeg` (0,15°) и `brightness` (1,5) — размер и яркость огней; `AidLightsView.nominalRangeNm` (3 мили); `ExerciseFailureNotice.showSeconds/upOffset`.

### Итоги запроса 7 (подробно — ThesisNotes, раздел 7; формат файлов — 7.4)
- Core `Core/Recording/`: `CsvFormat`, `SessionFormat` (+ `SessionMeta` для session.json), `SessionFileWriter` (фоновый поток), `SessionRecorder` + `TrackSampler`, `SessionStorage` (ротация, `keep`), `SessionReader` (`RecordedSession`, `StateAt`), `ControlActionDetector`, `ExerciseTargets`. Сцена `Scripts/Recording/`: `ExerciseRecorder` (объект Scenario, −84), `BridgeActionMonitor` (ShipRoot, −87). Editor: `SetupRecording` (`Tools/VR Bridge/Setup C1 Recording`), `SessionReplayWindow` (`Tools/VR Bridge/Session Replay`). Тесты `RecordingTests` (11).
- Правки: `ExerciseEvents` — 6 новых типов действий (в конец enum) и `Describe()` (`ToString` через него, вывод в консоль прежний); `ExerciseSummary` — число действий (строка на панели результатов); `ScenarioText` — `WeatherTitle`, `OrderFromSpeedFactor`, `Rudder`; `EngineTelegraph.IsGrabbed/LastChangeFromCode`; `ShipHorn.IsSounding`; `WeatherController.CurrentPresetId`; `ExerciseConsoleLog` — действия вне упражнения не печатает; `ScenarioPanel` — строка ID + VR-клавиатура на брифинге, папка записи на результатах; `.gitignore` — `/ThesisData/Sessions/`.
- Решения пользователя: VR-клавиатура ID (если будут проблемы — переделать; запасной путь — поле `traineeId` в инспекторе); сессии в git не коммитим; ротация последних 10 (метка «Сохранить» и `maxSessions = 0` — защита данных эксперимента); дополнительные столбцы `xte_limit_m` и `grounded` в track.csv оставлены.
- Вне задачи (обсуждено): графики записанной сессии в шлеме возможны и в автономном режиме — рисовать по данным (`SessionReader` в Core, без редактора) на VR-панели один раз после упражнения, не SVG; место — дебрифинг C3 / планшет D2.
- Замер вне Unity (.NET 10): строка траектории 1,3 мкс, событие 0,7 мкс, передача фоновому потоку 2,1 мкс, запись на диск до 1,7 мс (фон). Сессия B2-01 (1191,7 с) — 5960 строк, 462 КБ.
- .meta новых файлов созданы вручную (Unity был в фоне) — Unity их примет.
- Setup C1 выполнен, сцена в коммите. Проверено в шлеме (пользователь: «всё работает»), Test Runner 89/89, замер F8 во время записи — 90,0 Гц, 11,115 мс (без записи 11,114 мс; ThesisNotes, табл. 7.7). Первая сессия из шлема — фрагмент в ThesisNotes 7.10.
- Мелочь на будущее: в строке t = 0 track.csv телеграф «Стоп» (SF модели до первого шага); можно брать SF с телеграфа — правка в одну строку `ExerciseRecorder.SampleTrack`, не делалась.
- Проверялось в шлеме: брифинг — строка «Обучающийся», «Ввести ID обучающегося» → клавиатура (цифры, Стереть, Очистить, Готово/Отмена), ID отображается; «Начать» → в консоли `[Запись] Сессия … → …`; поработать штурвалом, телеграфом (провести рычаг через несколько положений), гудком, сменить погоду — в консоли события действий (одна команда телеграфа на перевод рычага); завершить (прибытие / посадка / «Завершить досрочно») — на результатах строка «Запись: …» и «Действия: …»; в `ThesisData/Sessions/<папка>` 4 файла, track.csv открывается в Excel (через «Из текста/CSV», разделитель — точка); выход из Play Mode во время упражнения → session.json с `completed: false`, `Interrupted`; `Tools/VR Bridge/Session Replay` — траектория, перемотка, события, «Сохранить»; замер F8 с `ExerciseRecorder.record` вкл/выкл — частота одинаковая (цифры — в ThesisNotes 7.6).
### Итоги запроса 6 (подробно — ThesisNotes, раздел 6)
- Core `Core/Scenarios/`: `ScenarioDefinition`, `RouteTracker`, `ExerciseEvents` (шина), `ExerciseSummary`, `ScenarioText`, `DemoScenario`, `AssessmentCriteria` (заготовка). Сцена `Scripts/Scenarios/`: `ScenarioRunner`, `ScenarioPanel`, `ExerciseConsoleLog`. Editor: `SetupScenarios` (+ `ScenarioLibrarySync`), `ScenarioDefinitionEditor`, `ScenarioRunnerEditor` (гизмо и ручки маршрута).
- Правки: `ShipDynamicsModel.Teleport(..., u, δ?)` (без новых аргументов — как раньше), `ShipNavigation.TeleportTo(..., уз, руль)`, `Paused`, `SetRudderOrder`; `EngineTelegraph.SetSpeedFactor` (`SetStop` через него); `SteeringWheel.SetRudderAngle`; `WeatherController.SetPreset(id, instant)`; `GroundingMonitor.Raise` → шина; `BridgeMenu.SetGroupOrder` + `hideWhenUnavailable`.
- Решения: на брифинге судно стоит, «Начать» ставит начальные условия заново; после завершения — свободное плавание; клавиатуры нет (п. 4 Plan.md про клавиатуру — в D1); время суток/видимость — только поля (B4/C4); XTE доп. 250/150 м и лимит 30 мин — предварительные.
- Находка для C2: без упреждения поворота (wheel-over) судно сцены выносит на 147 м из 150 допустимых, с упреждением 150 м — 23 м (табл. 6.4): «max XTE на повороте» и момент перекладки — кандидаты в критерии.
- .meta новых файлов созданы вручную (Unity был в фоне) — Unity их примет; ассет сценария и объекты сцены появятся после Setup B2.
- Setup B2 выполнен, сцена и ассет сценария в коммите. Проверено в шлеме (пользователь: «всё работает»). По логу из шлема исправлен порядок доставки событий шины: публикация во время рассылки (Grounded → ExerciseEnded) — в очередь, доставка после текущего (тест `Bus_NestedPublish_DeliveredAfterCurrentEvent`; важно для записи C1). Курс 0° печатался как 360,0° — исправлено.
- **Проверить в шлеме:** панель выбора при старте; брифинг (текст читается, судно стоит в (0, −2500) курсом 000°, погода «умеренное»); «Начать» — судно идёт 15 уз; в консоли — прохождение точек и выход/возврат в коридор (`[Упражнение]`); меню A: группа «Сценарий» вверху, «Отладка» внизу, лишние команды скрыты; «Завершить досрочно» и «Перезапустить» работают; посадка (например, «Отладка → 800 м до поворота» и не поворачивать) завершает упражнение и показывает результаты; прибытие в последнюю точку — результаты.

### Итоги запроса 5 (подробно — ThesisNotes, раздел 5)
- Core: `WaterArea` (ассет акватории), `IalaMarks`, `Polygon2D`, `GroundingDetector`, `DemoWaterArea`; `WorldPosition` перенесён в Core. `ShipDynamicsModel`: режим `Grounded` (при выключенном — бит в бит прежний шаг), `Teleport`.
- Сцена: `WaterAreaView` (буи на волнах, суша, отладочный слой), `GroundingMonitor` (ShipRoot), `GroundingCameraShake` (Camera Offset), `GroundingSounds` (заглушки), `BridgeMenu` (кнопка A, реестр команд — сюда же добавлять команды B2/D1).
- Решения пользователя: при посадке судно «наползает» (τ_ост 1 с) и трясётся **камера** (не корпус); предупреждение видимое + звук; **без клавиатуры** — вызываемое VR-меню на A, прыжок шаблона отключён; съём задним ходом и командой инструктора.
- Для B2: старт демо (0, −2500) курс 000°, ось фарватера `DemoWaterArea.Axis()` (3 точки), ширина 370,4 м; события посадки — C# `GroundingMonitor.WarningStarted/WarningCleared/Grounded/Refloated` (`GroundingEventInfo`, время симуляционное) → подключить к шине ExerciseEvents; `ShipNavigation.TeleportTo` — для начальных условий.
- После проверки в шлеме: туман пресетов пересчитан (было 1,7 км / 0,6 км / 70 м — буи не видно; сейчас штиль 0,0003 ≈ 5,8 км, умеренное 0,0004 ≈ 4,3 км, шторм 0,0015 ≈ 1,2 км, ночь 0,0004; край океана 10 км скрыт), рябь `detailStrength` 0,6 → 0,12 (`Tools/VR Bridge/Weaker Ripple`; запасное 0,35). В меню A — отладочные команды «переставить судно» (старт демо / вход в фарватер / 800 м до поворота).
- Тесты калибровки (14) раньше не попадали в сборку Unity (в Test Runner было 35, затем 53): файл переименован `ShipCalibrationTests.cs` → `CalibrationTests.cs` (класс прежний) — теперь 67/67. Если в будущем тестов «не хватает» — сверить список `Compile Include` в `VRBridge.Core.Tests.csproj`.
- `Assets/TextMesh Pro/.../LiberationSans SDF - Fallback.asset` меняется в Play Mode (динамический атлас дописывает кириллицу) — в коммиты не включать, как `OceanMaterial.mat`.
- Стенд вне Unity этого чата: `scratchpad/coretest` (заглушка UnityEngine `Stub.cs`, `Runner.cs`, `Measure.cs` — цифры таблицы 5.4), `scratchpad/unitybuild` (копии .csproj с абсолютными путями — сборка Assembly-CSharp/Editor через `dotnet build`).

### Итоги запроса 4 (подробно — ThesisNotes, раздел 4)
- `Core/Seakeeping/`: `OceanWaveField` (поле волн, сэмплер с обратной задачей Герстнера), `SeakeepingModel` (сетка 5×3, МНК, звенья 2-го порядка, точная дискретизация), `SeakeepingTrials` (стенд).
- `OceanWaves` переведён на `OceanWaveField`, фазы — в FixedUpdate + перенос на время отображения. `ShipMotion`: режим Waves (по умолчанию) / Sinusoidal, комфорт.
- Решения пользователя: сетка 15 точек (автономный Quest 3 в планах — нагрузка ~3 мкс/шаг на ПК); ζ_φ 0,10, ζ_z = ζ_θ 0,25, пределы 20°/8°, комфорт 1; правка шейдера у борта — **после** проверки в шлеме.
- Стенд показал резонанс бортовой качки в шторм на 15 уз при q = 120° (T_e 7,8 с ≈ T_φ 8,2 с, σ_φ 16,9°) — учебная ситуация, а не ошибка. Шаг модели качки — 4,7 мкс в редакторе.
- Для B1/B3: высота воды — `OceanWaves.Instance.SampleHeight(WorldPosition)` / `SampleWaterLevel(Vector3)`.
- Проверка тестов вне Unity: проект `scratchpad/coretest` этого чата (UnityEngine-заглушка + Runner через рефлексию; `TestContext` работает, если перед вызовом задать `TestExecutionContext.CurrentContext.CurrentResult`).

### Итоги запроса 3 (подробно — ThesisNotes, раздел 3)
- Прототип — сухогруз **Mariner** (USS Compass Island, L = 160,93 м, 15 уз). Эталон — нелинейная модель Абковица из MSS `mariner.m` (Chislett & Strøm-Tejsen 1965), перенесена в `Core/Reference/MarinerModel.cs`. **KCS** (EFD MARIN по Nath 2025, Yun 2025) — независимая проверка.
- Линейный Номото не попадает одновременно в 10° и 35° (ошибки 60–85 %) → введён **Норрбин** (переключатель `yawModel`), J меньше в 21 раз. Ассет Mariner: K' = 3,485; T' = 3,970; α' = 53,74; C_r' = 0,806; τ_разг = 95,2 с (из X'_u); ω_δ = 5 °/с. τ_торм и k_зх — заглушки.
- Ограничения: нет дрейфа (прямое смещение +16…+37 %), нет T'₃, только устойчивые на курсе суда (KCS: 2-е углы зарыскивания до +37 %), торможение — заглушки.
- Найдена и исправлена ошибка в ветке потери скорости при «полном назад» (тест `SpeedLoss_WithoutTurn_CrashStopUnchanged`).

## Договорённости о графиках для ВКР (одобрены)

- Графики строятся **на C# в SVG** (`TrialsReport.cs`, класс `SvgPlot`), Python/matplotlib **не используем** (у пользователя были проблемы с установкой; Python на ПК нет).
- Стиль одобрен для ВКР: чёрно-белые, линии разного типа (сплошная / штрих / пунктир), подписи на русском, десятичная запятая, минус «−», шрифт Times New Roman 14, белый фон, сетка светло-серая, легенда в правом верхнем углу.
- Новые графики в следующих запросах (калибровка, траектории сценариев, отчёты C3) делать **тем же `SvgPlot`**, в том же стиле, рядом с CSV в `ThesisData/...`.
- Образцы: `ThesisData/Trials/ShipParameters_Default_20261010_184154/*.svg` (turning_35, zigzag_10_10, zigzag_20_20, speed); калибровка — `ThesisData/Calibration/<судно>_<дата>/*.svg`.
- `SvgPlot.LegendBottomRight` — легенда справа внизу, если линии уходят в правый верхний угол (использована в `steady_turn_rate.svg`); по умолчанию — справа вверху, как одобрено.

## Полезное про окружение

- Лог Unity читается напрямую: `%LOCALAPPDATA%\Unity\Editor\Editor.log` — пользователю не нужно пересылать консоль.
- Результаты Test Runner: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\VR_BridgeTrainer_1\TestResults.xml`. Окно Test Runner иногда показывает «No tests» после перезагрузки домена — тесты при этом есть, смотреть XML.
- Проверка компиляции без Unity: копии `.csproj` в scratchpad с абсолютными путями + `dotnet build` (SDK 10). Пути менять через PowerShell `-replace` и в `Include="…"`, и в `<HintPath>` (Library\PackageCache — иначе ошибки XR Hands / NUnit); sed/perl ломаются на обратных слэшах. Новый файл, который Unity ещё не внёс в .csproj, добавлять в копию вручную.
- Модель из `VRBridge.Core` можно гонять вне Unity с заглушкой `UnityEngine` (ScriptableObject, Mathf, Vector2, Application.dataPath, атрибуты). Тесты — тот же код + `Library/PackageCache/com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll` и запуск через рефлексию по [Test]/[TestCase]. Так получены цифры калибровки до запуска в редакторе.
- PDF из интернета: в Git Bash есть `pdftotext` (`-layout`, `-table`); poppler (pdftoppm) нет, поэтому Read PDF не работает.
- Веб-источники: DTIC (apps.dtic.mil) был на обслуживании 10.10.2026; archive.org недоступен из сети сессии.
- `OceanMaterial.mat` меняется в Play Mode погодой — в коммиты не включать.
- `zaprosy_claude_code_etap2.md` в корне — копия `Docs/Plan.md`, не трогать и не коммитить.
- Python нет; batchmode не запускать (Unity всегда открыт).

## Открытые хвосты

1. **Дождь** (запрос 1, часть 3): частицы есть (~3900), в шлеме не видны. Исправлены текстура (Mathf.SmoothStep давал альфу 0.2) и очередь 3100, но `Tools/VR Bridge/Setup Rain` после исправления **не перезапускался**. Начать с: Setup Rain → Ctrl+S → Play → Шторм; если не видно — Frame Debugger, временно отключить RainShelter.
2. **Замер SPI**: для итоговой таблицы ВКР желательно по 3 прогона на режим в одном пресете при одном ракурсе (сейчас Multi-pass 1 прогон в шторм, SPI 2 прогона).
3. **Натурные данные Mariner**: отчёт Morse & Price 1961 (DTIC AD0271369, https://apps.dtic.mil/sti/pdfs/AD0271369.pdf) — когда DTIC заработает, скачать в проект. Из него взять циркуляции, Z-манёвры, разгон и торможение: добавить строки сравнения «натура» и, если есть торможение, заменить заглушки τ_торм / k_зх (тогда, возможно, понадобится баланс тяги и сопротивления).
4. **Судно на сцене**: `ShipNavigation` по-прежнему на `Model45m` (мостик 45 м). Ставить ли `ShipParameters_Mariner` на сцену — отдельное решение (мостик визуально не соответствует судну 161 м). Норрбин на малых скоростях устойчив (экспонента с g ≥ 1), но в шлеме не проверялся.
5. **Развитие модели** (ограничения раздела 3.8): дрейф (v), Номото 2-го порядка или MMG для судов с малой курсовой устойчивостью, асимметрия винта.
6. **Вода у борта (запрос 4)**: шейдер гасит волны у корпуса к нулю (`_HullDampMin` 0,25 в полосе 8 м). Если в шлеме корпус расходится с водой у борта — передать в шейдер плоскость ватерлинии судна (`_OceanShipPlane`) и в полосе гашения тянуть воду к ней, а не к нулю (при выключенном ShipMotion — прежний результат).
7. **Мореходные параметры Mariner/KCS** не заполнены (заглушки по умолчанию); данные KCS для заполнения — в `ShipParameters_Sources.md`.
8. **Источники раздела 4** (IS Code 2008 п. 2.3.4, Journée & Massie, Lloyd, Himeno) не сверены с первоисточниками.
9. **B1 — тонкая настройка по желанию** (в шлеме проверено, замечаний нет): размер буёв (`WaterAreaView.buoyScale`), амплитуда тряски (`GroundingCameraShake`), вид суши (`showLand`), положение меню (`BridgeMenu.distanceFromHead/downOffset`).
10. **B1 — источники** раздела 5 (IALA MBS, российские правила ограждения) не сверены; значения Δ = 0,5 м и D_пред = 1 кбт — предварительные (обосновать в главе 2).
11. **B2 — тонкая настройка по желанию** (в шлеме проверено): размер и положение панели (`ScenarioPanel.canvasScale/distanceFromHead/downOffset`), задержка показа при старте (`startDelay`).
12. **B2 — обоснование допусков** XTE доп. (250/150 м) и лимита времени — глава 2 (раздел 6.11 ThesisNotes).
13. **C1 — пороги действий** (ε_δ 0,5°, ε_ст 0,2°, T_уст 1 с, T_тел 0,5 с) — предварительные, обосновать в главе 2 (ThesisNotes, табл. 7.5). Строгий замер F8 с записью/без в одном сеансе (по 3 прогона) — по желанию, вместе с хвостом 2.
14. **C1 — VR-клавиатура ID**: если неудобна в шлеме — заменить (поле в инспекторе остаётся; в D1 — ввод в окне инструктора).
15. **C2 — пороги и веса оценки** — предварительные (ThesisNotes 10.13, три списка: нормативные / сверить с литературой / обосновать). Для обоснования — анализ чувствительности по записям эксперимента (офлайн-оценка с другими порогами).
16. ~~C2 — высота панели результатов~~ — решено в C3 (вкладки, таблица по страницам).
17. **C2 — радар/САРП** не моделируются (обнаружение визуальное); правило 18 и правило 19 (туман) — C4.
18. **C3 — подписи на схеме** не раздвигаются (наложения при близких отметках); масштабирования схемы рукой нет — только готовые виды. Прогоны `ThesisData/Debrief/<дата>/sessions/` весят ~0,5 МБ каждый — при коммите решить, все ли нужны (report.html + session.json достаточно для иллюстраций).
