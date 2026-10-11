// ============================================================
// EnvironmentTests.cs — EditMode-тесты освещения «погода × время суток» (B4)
// Window → General → Test Runner → EditMode → Run All.
// ============================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;

public class EnvironmentTests
{
    // Пресеты погоды — те же значения, что в WeatherController (дневной вид до B4)
    private static WeatherLook Preset(string name)
    {
        switch (name)
        {
            case "calm":
                return new WeatherLook
                {
                    SeaState = 0.08f, WindSpeed = 0.5f, FogDensity = 0.0003f, SunIntensity = 1.2f,
                    SunColor = new Rgb(1f, 0.96f, 0.84f), SunElevationDeg = 50f,
                    AmbientSky = new Rgb(0.53f, 0.71f, 0.85f), AmbientGround = new Rgb(0.25f, 0.22f, 0.18f), FogColor = new Rgb(0.7f, 0.8f, 0.9f)
                };
            case "storm":
                return new WeatherLook
                {
                    SeaState = 0.85f, WindSpeed = 2.2f, Rain = 1f, FogDensity = 0.0015f, SunIntensity = 0.12f,
                    SunColor = new Rgb(0.45f, 0.45f, 0.5f), SunElevationDeg = 25f,
                    AmbientSky = new Rgb(0.08f, 0.09f, 0.11f), AmbientGround = new Rgb(0.05f, 0.05f, 0.05f), FogColor = new Rgb(0.25f, 0.27f, 0.3f)
                };
            case "night":
                return new WeatherLook
                {
                    SeaState = 0.15f, WindSpeed = 0.6f, FogDensity = 0.00015f, SunIntensity = 1.0f,
                    SunColor = new Rgb(1f, 0.95f, 0.85f), SunElevationDeg = 45f,
                    AmbientSky = new Rgb(0.45f, 0.58f, 0.7f), AmbientGround = new Rgb(0.2f, 0.2f, 0.18f), FogColor = new Rgb(0.6f, 0.68f, 0.76f)
                };
            default:
                return new WeatherLook
                {
                    SeaState = 0.3f, WindSpeed = 1f, FogDensity = 0.0004f, SunIntensity = 1.0f,
                    SunColor = new Rgb(1f, 0.95f, 0.85f), SunElevationDeg = 45f,
                    AmbientSky = new Rgb(0.45f, 0.58f, 0.7f), AmbientGround = new Rgb(0.2f, 0.2f, 0.18f), FogColor = new Rgb(0.6f, 0.68f, 0.76f)
                };
        }
    }

    private static readonly string[] AllWeather = { "calm", "moderate", "storm", "night" };

    private static void AssertRgb(Rgb expected, Rgb actual, string what)
    {
        Assert.AreEqual(expected.R, actual.R, 0f, what + " R");
        Assert.AreEqual(expected.G, actual.G, 0f, what + " G");
        Assert.AreEqual(expected.B, actual.B, 0f, what + " B");
    }

    [TestCase("calm")]
    [TestCase("moderate")]
    [TestCase("storm")]
    [TestCase("night")]
    public void Day_EqualsWeatherPreset_BitForBit(string name)
    {
        WeatherLook w = Preset(name);
        EnvironmentLighting L = EnvironmentModel.Combine(w, EnvironmentModel.DayPhase, new NightProfile());
        Assert.AreEqual(w.SunIntensity, L.SunIntensity, 0f);
        AssertRgb(w.SunColor, L.SunColor, "цвет солнца");
        Assert.AreEqual(w.SunElevationDeg, L.SunElevationDeg, 0f);
        AssertRgb(w.AmbientSky, L.AmbientSky, "рассеянный сверху");
        AssertRgb(w.AmbientGround, L.AmbientGround, "рассеянный снизу");
        AssertRgb((w.AmbientSky + w.AmbientGround) * 0.5f, L.AmbientEquator, "рассеянный у горизонта");
        AssertRgb(w.FogColor, L.FogColor, "цвет тумана");
        Assert.AreEqual(w.FogDensity, L.FogDensity, 0f);
        // Небо — формулы SkyWeather до B4
        float legacyDark = 1f - Math.Max(0.03f, Math.Min(1f, (float)Math.Pow(w.SunIntensity, 0.6)));
        Assert.AreEqual(legacyDark, L.SkyDarkening, 1e-6f);
        float x = Math.Max(0f, Math.Min(1f, (w.SeaState - 0.45f) / 0.35f));
        Assert.AreEqual(x * x * (3f - 2f * x), L.SkyOvercast, 1e-6f);
        // Ночного ничего нет
        Assert.AreEqual(0f, L.MoonIntensity, 0f);
        Assert.AreEqual(0f, L.SkyNight, 0f);
        Assert.AreEqual(0f, L.StarBrightness, 0f);
        Assert.AreEqual(0f, L.TwilightGlow, 0f);
        Assert.AreEqual(0f, L.WaterSpecGain, 0f, "блик воды днём как до B4");
        Assert.AreEqual(0f, L.WaterSpecWidening, 0f);
        Assert.AreEqual(0f, L.Darkness, 0f);
    }

    [Test]
    public void Phase_WeightsSumToOne_DayToNightThroughTwilight()
    {
        for (int i = -10; i <= 210; i++)
        {
            float s = i / 100f;
            EnvironmentModel.Weights(s, out float d, out float t, out float n);
            Assert.AreEqual(1f, d + t + n, 1e-6f, $"s = {s}");
            Assert.IsTrue(d >= 0f && t >= 0f && n >= 0f, $"s = {s}");
        }
        EnvironmentModel.Weights(1f, out float d1, out float t1, out float n1);
        Assert.AreEqual(1f, t1, 0f, "s = 1 — сумерки");
        Assert.AreEqual(EnvironmentModel.DayPhase, EnvironmentModel.PhaseOf(TimeOfDay.Day));
        Assert.AreEqual(EnvironmentModel.TwilightPhase, EnvironmentModel.PhaseOf(TimeOfDay.Twilight));
        Assert.AreEqual(EnvironmentModel.NightPhase, EnvironmentModel.PhaseOf(TimeOfDay.Night));
    }

    [Test]
    public void Night_IndependentOfWeather_ExceptVisibilityAndClouds()
    {
        var p = new NightProfile();
        EnvironmentLighting calm = EnvironmentModel.Combine(Preset("calm"), 2f, p);
        EnvironmentLighting moderate = EnvironmentModel.Combine(Preset("moderate"), 2f, p);
        EnvironmentLighting night = EnvironmentModel.Combine(Preset("night"), 2f, p);
        foreach (EnvironmentLighting L in new[] { calm, moderate, night })
        {
            Assert.AreEqual(0f, L.SunIntensity, 0f, "ночью солнца нет — нет и дневного блика на воде");
            Assert.AreEqual(p.MoonIntensity, L.MoonIntensity, 1e-6f);
            AssertRgb(calm.AmbientSky, L.AmbientSky, "рассеянный ночью");
            AssertRgb(calm.FogColor, L.FogColor, "цвет тумана ночью");
            Assert.AreEqual(1f, L.Darkness, 0f);
            Assert.AreEqual(1f, L.SkyNight, 0f);
        }
        // Видимость — по погоде (C4 подменит видимостью сценария)
        Assert.AreEqual(0.0003f, calm.FogDensity, 0f);
        Assert.AreEqual(0.0004f, moderate.FogDensity, 0f);
        Assert.AreEqual(0.00015f, night.FogDensity, 0f, "B3-01: 6,2 мили");

        // Шторм ночью: тучи закрывают луну и звёзды, темнее
        EnvironmentLighting storm = EnvironmentModel.Combine(Preset("storm"), 2f, p);
        Assert.Less(storm.MoonIntensity, 0.2f * p.MoonIntensity);
        Assert.AreEqual(0f, storm.StarBrightness, 1e-6f);
        Assert.AreEqual(0f, storm.MoonDiscBrightness, 1e-6f);
        Assert.Less(storm.AmbientSky.Luma, calm.AmbientSky.Luma);
        Assert.AreEqual(0.0015f, storm.FogDensity, 0f);
    }

    [Test]
    public void Twilight_LowWarmSun_GlowAndHalfNightSky()
    {
        var p = new NightProfile();
        EnvironmentLighting L = EnvironmentModel.Combine(Preset("moderate"), 1f, p);
        Assert.AreEqual(p.TwilightSunElevationDeg, L.SunElevationDeg, 1e-5f);
        AssertRgb(p.TwilightSunColor, L.SunColor, "солнце в сумерки");
        Assert.AreEqual(p.TwilightSunIntensity, L.SunIntensity, 1e-6f);
        Assert.AreEqual(1f, L.TwilightGlow, 1e-6f);
        Assert.AreEqual(p.TwilightSkyNight, L.SkyNight, 1e-6f);
        Assert.AreEqual(p.TwilightDarkness, L.Darkness, 1e-6f);
        Assert.Greater(L.StarBrightness, 0f, "первые звёзды");
        Assert.Less(L.StarBrightness, 0.5f);
    }

    [TestCase("calm")]
    [TestCase("moderate")]
    [TestCase("storm")]
    [TestCase("night")]
    public void DayToNight_Continuous_NoBrightening_MainLightSwitchInDark(string name)
    {
        WeatherLook w = Preset(name);
        var p = new NightProfile();
        const int N = 2000;
        EnvironmentLighting prev = EnvironmentModel.Combine(w, 0f, p);
        bool switched = false;
        for (int i = 1; i <= N; i++)
        {
            float s = 2f * i / N;
            EnvironmentLighting L = EnvironmentModel.Combine(w, s, p);
            float ds = 2f / N;
            // Непрерывность: изменение за шаг ограничено (нет скачков)
            Assert.LessOrEqual(Math.Abs(L.SunIntensity - prev.SunIntensity), 1.3f * ds + 1e-6f, $"солнце, s = {s}");
            Assert.LessOrEqual(Math.Abs(L.SunElevationDeg - prev.SunElevationDeg), 60f * ds + 1e-4f, $"высота солнца, s = {s}");
            Assert.LessOrEqual(Rgb.MaxDiff(L.AmbientSky, prev.AmbientSky), 1f * ds + 1e-6f, $"рассеянный, s = {s}");
            Assert.LessOrEqual(Math.Abs(L.SkyNight - prev.SkyNight), 1f * ds + 1e-6f, $"небо, s = {s}");
            // Без вспышек: солнце, рассеянный свет и туман по ходу к ночи не ярче
            Assert.LessOrEqual(L.SunIntensity, prev.SunIntensity + 1e-6f, $"солнце ярче, s = {s}");
            Assert.LessOrEqual(L.AmbientSky.Luma, prev.AmbientSky.Luma + 1e-6f, $"рассеянный сверху ярче, s = {s}");
            Assert.LessOrEqual(L.AmbientEquator.Luma, prev.AmbientEquator.Luma + 1e-6f, $"у горизонта ярче, s = {s}");
            Assert.LessOrEqual(L.AmbientGround.Luma, prev.AmbientGround.Luma + 1e-6f, $"снизу ярче, s = {s}");
            Assert.LessOrEqual(L.FogColor.Luma, prev.FogColor.Luma + 1e-6f, $"туман ярче, s = {s}");
            Assert.GreaterOrEqual(L.Darkness, prev.Darkness - 1e-6f);
            Assert.LessOrEqual(L.MoonIntensity, p.MoonIntensity + 1e-6f);
            // Главный источник меняется с солнца на луну только когда оба тусклые
            if (!switched && L.MoonIntensity > L.SunIntensity)
            {
                switched = true;
                Assert.Less(L.SunIntensity, 0.1f, $"солнце при смене главного источника, s = {s}");
            }
            prev = L;
        }
        Assert.AreEqual(0f, prev.SunIntensity, 0f);
        Assert.IsTrue(name == "storm" ? prev.MoonIntensity > 0f : switched, "ночью главный — луна");
    }

    [Test]
    public void Lights_RequiredAtTwilightAndNight_OrRestrictedVisibility()
    {
        var p = new NightProfile();
        WeatherLook w = Preset("night");
        Assert.AreEqual(0f, EnvironmentModel.Combine(w, 0f, p).Darkness, 0f);
        Assert.AreEqual(0.6f, EnvironmentModel.Combine(w, 1f, p).Darkness, 1e-6f);
        Assert.AreEqual(1f, EnvironmentModel.Combine(w, 2f, p).Darkness, 0f);
        double visNight = 1.7308 / 0.00015;   // ≈ 11,5 км
        double visStorm = 1.7308 / 0.0015;    // ≈ 1,15 км = 0,62 мили
        Assert.IsFalse(EnvironmentModel.LightsRequired(0f, visNight), "день, ясно — огней нет");
        Assert.IsTrue(EnvironmentModel.LightsRequired(0f, visStorm), "шторм днём — по видимости (правило 20 в)");
        Assert.IsTrue(EnvironmentModel.LightsRequired(0.6f, visNight), "сумерки");
        Assert.IsTrue(EnvironmentModel.LightsRequired(1f, visNight), "ночь");
        // Огни загораются на переходе день → сумерки при Darkness = 0,5: s = 0,5/0,6
        Assert.IsFalse(EnvironmentModel.LightsRequired(EnvironmentModel.Combine(w, 0.8f, p).Darkness, visNight));
        Assert.IsTrue(EnvironmentModel.LightsRequired(EnvironmentModel.Combine(w, 0.85f, p).Darkness, visNight));
    }

    [Test]
    public void Exposure_ScalesNightOnly()
    {
        WeatherLook w = Preset("calm");
        var p1 = new NightProfile();
        var p2 = new NightProfile { Exposure = 2f };
        EnvironmentLighting day1 = EnvironmentModel.Combine(w, 0f, p1), day2 = EnvironmentModel.Combine(w, 0f, p2);
        Assert.AreEqual(day1.SunIntensity, day2.SunIntensity, 0f);
        AssertRgb(day1.AmbientSky, day2.AmbientSky, "день");
        EnvironmentLighting n1 = EnvironmentModel.Combine(w, 2f, p1), n2 = EnvironmentModel.Combine(w, 2f, p2);
        Assert.AreEqual(2f * n1.MoonIntensity, n2.MoonIntensity, 1e-6f, "луна × E");
        float k = (float)Math.Pow(2.0, 1.0 / 2.2);
        Assert.AreEqual(k * n1.AmbientSky.G, n2.AmbientSky.G, 1e-5f, "цвета (sRGB) × E^(1/2,2) — линейная яркость × E");
        Assert.AreEqual(n1.FogDensity, n2.FogDensity, 0f, "видимость от экспозиции не зависит");
    }

    [Test]
    public void Night_MoonPathOnWater_NoSunGlint()
    {
        var p = new NightProfile();
        EnvironmentLighting L = EnvironmentModel.Combine(Preset("night"), 2f, p);
        Assert.AreEqual(0f, L.SunIntensity, 0f);
        Assert.AreEqual(p.WaterSpecGain, L.WaterSpecGain, 0f);
        Assert.AreEqual(p.WaterSpecWidening, L.WaterSpecWidening, 0f);
        Assert.Greater(L.MoonElevationDeg, 0f, "луна над горизонтом");
        Assert.Greater(L.MoonDiscBrightness, 0f);
    }

    [Test]
    public void TimeOfDayChange_RecordedAsEvent()
    {
        var d = new ControlActionDetector();
        ControlState S(int tod) => new ControlState
        {
            Telegraph = (int)TelegraphOrder.FullAhead, TelegraphSf = ScenarioText.SpeedFactor(TelegraphOrder.FullAhead),
            WeatherPreset = (int)WeatherPresetId.Night, TimeOfDay = tod
        };
        d.Reset(0, S((int)TimeOfDay.Day));
        Assert.AreEqual(0, d.Update(0.02, S((int)TimeOfDay.Day)).Count);
        IReadOnlyList<ExerciseEvent> ev = d.Update(0.04, S((int)TimeOfDay.Night));
        Assert.AreEqual(1, ev.Count);
        Assert.AreEqual(ExerciseEventType.TimeOfDayChanged, ev[0].Type);
        Assert.AreEqual((int)TimeOfDay.Night, ev[0].Index);
        Assert.AreEqual((double)TimeOfDay.Day, ev[0].Value);
        Assert.AreEqual("ночь", ev[0].Subject);
        Assert.AreEqual("Время суток: ночь", ev[0].Describe());
        Assert.AreEqual(0, d.Update(0.06, S((int)TimeOfDay.Night)).Count, "одно событие на смену");
    }

    [Test]
    public void ScenarioText_ConditionsSeparateTimeAndWeather()
    {
        Assert.AreEqual("Ночь · слабое волнение, видимость ≈ 11,5 км (6,2 мили)", ScenarioText.Conditions(WeatherPresetId.Night, TimeOfDay.Night));
        Assert.AreEqual("Сумерки · шторм, видимость ≈ 1,2 км", ScenarioText.Conditions(WeatherPresetId.Storm, TimeOfDay.Twilight));
        Assert.AreEqual("День · умеренное волнение, видимость ≈ 4,3 км", ScenarioText.Conditions(WeatherPresetId.Moderate, TimeOfDay.Day));
    }
}
