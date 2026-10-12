# Источники параметров ShipParameters (A3.2, калибровка)

Ассеты `ShipParameters_Mariner.asset` и `ShipParameters_KCS.asset` создаёт окно
`Tools/VR Bridge/Model Calibration` → «Сохранить ассет» (этап «Норрбин»).
Значения ниже — из прогона 10.10.2026. Отчёт с таблицами и графиками лежит в
`ThesisData/Calibration/<судно>_<дата>/`.

Обозначения: **И** — взято из источника; **В** — выведено формулой из данных
источника; **П** — подобрано (идентификация, метод выходной ошибки);
**З** — ЗАГЛУШКА (данных нет).

## ShipParameters_Mariner — сухогруз класса Mariner (USS Compass Island)

| Поле | Значение | Тип | Источник / формула |
|---|---|---|---|
| `length` | 160,93 м | И | MSS `mariner.m` (L = 160.93) [1]; коэффициенты модели — [2] |
| `serviceSpeedKnots` | 15,00 уз (7,7175 м/с) | И | MSS `mariner.m`: U0 = 7.7175 м/с [1]. На этой скорости подхода проводилось большинство натурных испытаний [3]. MARAD указывает для класса эксплуатационную скорость 20 уз, но модель Абковица линеаризована около 15 уз, поэтому берётся 15 уз |
| `accelerationTimeConstant` | 95,2 с | В | τ_u = m'₁₁·L/(−X'_u·U0), где m'₁₁ = m' − X'_u̇ = 798·10⁻⁵ + 42·10⁻⁵, X'_u = −184·10⁻⁵ [1, 2]. Это постоянная продольного движения при неизменных оборотах. Для разгона с места — экстраполяция |
| `decelerationTimeConstant` | 150 с | **З** | В эталоне нет модели винта и заднего хода; отчёт о натурных испытаниях [4] недоступен (DTIC) |
| `asternFactor` | 0,6 | **З** | То же |
| `yawModel` | Norrbin | П | Выбор по данным: линейный Номото не попадает одновременно в 10° и 35° (ThesisNotes, раздел 3) |
| `nomotoKPrime` | 3,4845 | П | Идентификация по эталонным манёврам [1, 2] |
| `nomotoTPrime` | 3,9699 | П | То же |
| `norrbinAlphaPrime` | 53,74 | П | То же |
| `turnSpeedLossCoeff` | 0,8063 | П | По u(t) эталонных циркуляций 10°, 20°, 35° |
| `maxRudderAngle` | 35° | И | MSC.137(76): циркуляция с рулём 35° [5]. В `mariner.m` δ_max = 40° |
| `rudderRate` | 5 °/с | И | `mariner.m`: Ddelta_max = 5 °/с [1]. В эталоне это ещё и апериодическое звено с T = 1 с, в тренажёре — только ограничение скорости |
| `minCalcSpeed` | 0,05 м/с | — | Численная защита от деления на ноль, не физический параметр |

Справочно, моделью не используются: B = 76 ft = 23,16 м — MARAD, тип C4-S-1a [6];
∇ ≈ 16 630 м³ — пересчёт из m' = 798·10⁻⁵: ∇ = m'·L³/2. **Осадка T — не найдена.**

## ShipParameters_KCS — контейнеровоз KCS (независимая проверка)

| Поле | Значение | Тип | Источник / формула |
|---|---|---|---|
| `length` | 230 м | И | Lpp натуры [7, табл. I] |
| `serviceSpeedKnots` | 24,00 уз (12,35 м/с) | В | Fn = 0,26 [7, 8]: U = Fn·√(g·L) |
| `maxRudderAngle` | 35° | И | Программа испытаний SIMMAN 2014 [9] |
| `rudderRate` | 2,32 °/с | И | [8, табл. 1] |
| `yawModel` | Norrbin | П | Меньшая ошибка J (0,454 против 0,665 у Номото) |
| `nomotoKPrime` | 4,5784 | П | Идентификация по показателям EFD MARIN [7, табл. III, IV] |
| `nomotoTPrime` | 3,1820 | П | То же |
| `norrbinAlphaPrime` | 5,819 | П | То же |
| `turnSpeedLossCoeff` | 0 | **З** | Числовых данных о скорости на циркуляции в источниках нет |
| `accelerationTimeConstant` | 90 с | **З** | Торможение и разгон KCS не опубликованы |
| `decelerationTimeConstant` | 150 с | **З** | То же |
| `asternFactor` | 0,6 | **З** | То же |

Справочно: B = 32,2 м, T = 10,8 м, ∇ = 52 057 м³ [7, табл. I]; Cb = 0,651 [8, табл. 1];
масштаб модели 1:37,89.

## Мореходность (запрос 4): ширина, осадка, остойчивость, периоды качки

Поля `beam`, `draft`, `metacentricHeight`, `blockCoefficient`, `waterplaneCoefficient`,
`rollPeriod`, `heavePeriod`, `pitchPeriod`, `rollDamping`, `heaveDamping`, `pitchDamping`.
Используются только моделью качки (`SeakeepingModel`), на манёвренность не влияют.

### ShipParameters_Model45m (судно сцены) — заполняет `Tools/VR Bridge/Setup Seakeeping`

| Поле | Значение | Тип | Источник / формула |
|---|---|---|---|
| `beam` | 8,71 м | В | Ширина корпуса сцены (примитивы Hull и BowTip, повёрнутый на 45°) |
| `draft` | 3,00 м | В | Уровень воды (OceanRadial, y = −5) минус днище Hull (Setup A1.1) |
| `metacentricHeight` | 0,8 м | **З** | Допущение: GM ≈ 0,09·B — типично для небольших судов; нормативный минимум 0,15 м (IS Code 2008, ч. A, 2.2.4) |
| `blockCoefficient` | 0,6 | **З** | Допущение |
| `waterplaneCoefficient` | 0,75 | **З** | Допущение |
| `rollPeriod` | 8,19 с | В | T_φ = 2·C·B/√GM, C = 0,373 + 0,023·B/d − 0,043·L/100 (IS Code 2008, ч. A, 2.3.4) |
| `heavePeriod` | 4,40 с | В | T_z = 2π·√(2·C_B·d/(C_WP·g)), a₃₃ ≈ m (Journée & Massie, 2001) |
| `pitchPeriod` | 4,40 с | В | T_θ ≈ T_z (Lloyd, 1989) |
| `rollDamping` | 0,10 | **З** | Эквивалентное линейное затухание судна с боковыми килями (порядок величины — Himeno, 1981) |
| `heaveDamping`, `pitchDamping` | 0,25 | **З** | Порядок величины для вертикальной и килевой качки |

### ShipParameters_Mariner, ShipParameters_KCS — ширина и осадка (B3), остальное — заглушки

С B3 (другие суда) Mariner и KCS используются как цели: ширина нужна для контура
столкновения (L × B) и модели корпуса, осадка — для проверки «старт цели не на мели».
`Tools/VR Bridge/Setup B3 Traffic` записывает:

| Ассет | `beam` | `draft` | Источник |
|---|---|---|---|
| Mariner | 23,16 м — В | 8,0 м — **З** (значение класса по умолчанию; осадка не найдена) | B — [6] (MARAD, C4-S-1a) |
| KCS | 32,2 м — В | 10,8 м — В | [7, табл. I] |

На модель движения и калибровку A3.2 это не влияет: B и d в ней не участвуют.
Периоды и затухания качки целей — значения класса по умолчанию (T_φ = 12 с,
T_z = T_θ = 7 с, ζ 0,1/0,25/0,25) — **заглушки**. Если понадобятся: KCS — GM = 0,6 м [7, табл. I],
C_B = 0,651 [8, табл. 1], C_WP — нет данных.

## ShipParameters_FastBoat — быстроходный катер (запрос 15, тестовое судно)

Создаёт `Tools/VR Bridge/Setup Fast Boat`; значения — единственный источник `ShipPresets.FillFastBoat` (Core).
Прототипа нет: открытый катер с центральной рулевой стойкой, L ≈ 18 м. Почти все значения — **допущения** (З),
выбранные так, чтобы катер был заметно быстрее и поворотливее судна сцены. Катер — для отладки сценариев
и демонстрации, **не для оценки** (ThesisNotes, раздел 15).

| Поле | Значение | Тип | Источник / обоснование |
|---|---|---|---|
| `length` | 18 м | **З** | Типичный патрульный / рабочий катер |
| `serviceSpeedKnots` | 35 уз | **З** | Fn = V/√(gL) = 18,0/√(9,81·18) ≈ 1,36 — режим глиссирования (Fn > ≈ 1,0–1,2 [10]) |
| `accelerationTimeConstant` / `decelerationTimeConstant` | 10 / 15 с | **З** | 95 % хода за ≈ 3τ = 30 с; гашение хода быстрее, чем у водоизмещающего судна |
| `asternFactor` | 0,4 | **З** | Задний ход катера слабый |
| `nomotoKPrime` / `nomotoTPrime` | 1,0 / 1,2 | **З** | D(35°) = 2L/(K'·δ) ≈ 59 м ≈ 3,3 L; T = T'·L/U ≈ 1,2 с на полном ходу |
| `rudderRate` | 10 °/с | **З** | Гидравлический привод: борт на борт (70°) ≈ 7 с; требование СОЛАС II-1/29 (28 с) к катеру не относится |
| `beam` / `draft` | 4,8 / 1,1 м | **З** | L/B ≈ 3,75 — типично для глиссирующих корпусов [10] |
| `metacentricHeight` | 1,2 м | **З** | Широкий мелкосидящий корпус — большая начальная остойчивость |
| `blockCoefficient` / `waterplaneCoefficient` | 0,42 / 0,72 | **З** | Острые обводы с V-образным днищем |
| `rollPeriod` | ≈ 4,1 с | В | T_φ = 2·C·B/√GM, C = 0,373 + 0,023·B/d − 0,043·L/100 = 0,466 (IS Code 2008, ч. A, 2.3.4) |
| `heavePeriod` = `pitchPeriod` | ≈ 2,3 с | В | T_z = 2π·√(2·C_B·d/(C_WP·g)) (Journée & Massie, 2001) |
| `rollDamping` / `heaveDamping`, `pitchDamping` | 0,15 / 0,30 | **З** | Скулы и брызгоотбойники — затухание больше, чем у судна |

**Ограничение.** Модель манёвренности (Номото 1-го порядка) и качки (звенья 2-го порядка) построены для
водоизмещающих судов. Подъём корпуса на глиссировании, ходовой дифферент, крен на циркуляции, «дельфинирование»
и удары днища о волну [10, 11] не моделируются: на 35 уз катер движется как «маленькое быстрое судно».
## Литература

1. Fossen T.I. Marine Systems Simulator (MSS), `CRAFT/SHIP/models/mariner.m` (авт. T. Lauvdal, 1994; ред. 2001–2024). https://github.com/cybergalactic/MSS
2. Chislett M.S., Strøm-Tejsen J. Planar Motion Mechanism Tests and Full-Scale Steering and Manoeuvring Predictions for a Mariner Class Vessel. Technical Report Hy-5 (Hy-6). Hydro- and Aerodynamics Laboratory, Lyngby, 1965.
3. Smitt L.W., Chislett M.S. Large Amplitude PMM Tests and Manoeuvring Predictions for a Mariner Class Vessel. Proc. 10th Symposium on Naval Hydrodynamics, Cambridge (Mass.), 1974. ONR, 1976, pp. 131–157. https://trid.trb.org/View/66740
4. Morse R.V., Price D. Maneuvering Characteristics of the Mariner Type Ship (USS Compass Island) in Calm Seas. Sperry Gyroscope Co., 1961. DTIC AD0271369.
5. IMO Res. MSC.137(76). Standards for Ship Manoeuvrability, 2002; MSC/Circ.1053, 2002.
6. MARAD Vessel Status Card: SUNFLOWER MARINER, тип C4-S-1a. https://vesselhistory.marad.dot.gov/ShipHistory/Detail/6318
7. Nath A., Peng H., Qiu W. Numerical Analysis and Validation of Turning Circle and Zig-Zag Maneuvers of KRISO Container Ship in Calm Sea Using Body Force Method. Proc. CSME/CFDSC/CSR 2025, Montréal, paper 409. https://espace2.etsmtl.ca/32469/
8. Yun H., Choi W., Min G. et al. CFD analysis of container ship manoeuvring performance under different trim conditions. Ocean Engineering, 2025, 341, 122489. doi:10.1016/j.oceaneng.2025.122489
9. SIMMAN 2014. KCS free model tests in deep water — model test specification. https://simman2014.dk/wp-content/uploads/2015/07/KCS-free-deep.pdf
10. Faltinsen O.M. Hydrodynamics of High-Speed Marine Vehicles. Cambridge University Press, 2005.
11. Savitsky D. Hydrodynamic Design of Planing Hulls. Marine Technology, 1964, 1(1), pp. 71–95.
