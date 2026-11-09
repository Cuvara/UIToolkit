using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cuvara.UIToolkit.Motion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Motion.Tests
{
    /// <summary>Easing curves, the preset JSON (validation and lookup) and the inline-style target. No frames.</summary>
    public class MotionDataTests
    {
        #region Easing

        [Test]
        public void Easing_EndpointsAreFixed()
        {
            foreach (var e in new[] { MotionEasing.Linear, MotionEasing.Ease, MotionEasing.EaseIn, MotionEasing.EaseOut, MotionEasing.EaseInOut })
            {
                Assert.AreEqual(0f, e.Evaluate(0f), 1e-6f, e.ToString());
                Assert.AreEqual(1f, e.Evaluate(1f), 1e-6f, e.ToString());
                Assert.AreEqual(0f, e.Evaluate(-0.5f), 1e-6f, "clamped below");
                Assert.AreEqual(1f, e.Evaluate(1.5f), 1e-6f, "clamped above");
            }
        }

        [Test]
        public void Easing_NamedCurvesMatchCss()
        {
            Assert.AreEqual(0.3f, MotionEasing.Linear.Evaluate(0.3f), 1e-6f);
            Assert.AreEqual(0.8024f, MotionEasing.Ease.Evaluate(0.5f), 0.005f);
            Assert.AreEqual(0.3153f, MotionEasing.EaseIn.Evaluate(0.5f), 0.005f);
            Assert.AreEqual(0.6847f, MotionEasing.EaseOut.Evaluate(0.5f), 0.005f);
            Assert.AreEqual(0.5f, MotionEasing.EaseInOut.Evaluate(0.5f), 0.005f);
        }

        [Test]
        public void Easing_NamedCurvesAreMonotonic()
        {
            foreach (var e in new[] { MotionEasing.Ease, MotionEasing.EaseIn, MotionEasing.EaseOut, MotionEasing.EaseInOut })
            {
                var last = 0f;
                for (var i = 1; i <= 100; i++)
                {
                    var v = e.Evaluate(i / 100f);
                    Assert.GreaterOrEqual(v, last - 1e-4f, e + " at " + i);
                    last = v;
                }
            }
        }

        [Test]
        public void Easing_BezierEqualsTheNamedCurveWithTheSamePoints_AndMayOvershoot()
        {
            var same = MotionEasing.CubicBezier(0.25f, 0.1f, 0.25f, 1f);
            Assert.AreEqual(MotionEasing.Ease.Evaluate(0.37f), same.Evaluate(0.37f), 1e-4f);
            Assert.IsFalse(same.IsNamed);
            var overshoot = MotionEasing.CubicBezier(0.3f, -0.6f, 0.7f, 1.6f);
            Assert.Less(overshoot.Evaluate(0.15f), 0f, "anticipation");
            Assert.Greater(overshoot.Evaluate(0.9f), 1f, "overshoot");
        }

        [Test]
        public void Easing_Parse()
        {
            Assert.IsTrue(MotionEasing.TryParse(" EASE-in-Out ", out var e));
            Assert.AreEqual(MotionEasing.EaseInOut, e);
            Assert.IsTrue(MotionEasing.TryParse("cubic-bezier(0.1, 0.7, 1.0, 0.1)", out e));
            Assert.AreEqual(MotionEasing.CubicBezier(0.1f, 0.7f, 1f, 0.1f), e);
            Assert.AreEqual(e, MotionEasing.TryParse(e.ToString(), out var round) ? round : default, "ToString round-trips");
            foreach (var bad in new[] { null, "", "bounce", "ease-out-back", "cubic-bezier(2,0,1,1)", "cubic-bezier(0,0,1)", "cubic-bezier(a,0,1,1)", "cubic-bezier(0,0,1,1" })
                Assert.IsFalse(MotionEasing.TryParse(bad, out _), bad ?? "null");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => MotionEasing.CubicBezier(1.5f, 0f, 0f, 1f));
        }

        [Test]
        public void Easing_NonFiniteControlPointsAreRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => MotionEasing.CubicBezier(float.NaN, 0f, 0.5f, 1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => MotionEasing.CubicBezier(0.2f, float.NaN, 0.5f, 1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => MotionEasing.CubicBezier(0.2f, 0f, 0.5f, float.PositiveInfinity));
            foreach (var bad in new[] { "cubic-bezier(NaN, 0, 1, 1)", "cubic-bezier(0, Infinity, 1, 1)", "cubic-bezier(0, 0, 1, -Infinity)", "cubic-bezier(0.1, 0, 0.2, NaN)" })
                Assert.IsFalse(MotionEasing.TryParse(bad, out _), bad);
        }

        #endregion

        #region Presets

        const string Valid = @"{ ""version"": 1,
  ""durations"": [ { ""name"": ""fast"", ""ms"": 120 }, { ""name"": ""normal"", ""ms"": 200 } ],
  ""presets"": [
    { ""name"": ""button"", ""duration"": ""fast"", ""easing"": ""ease-out"", ""delayMs"": 10,
      ""properties"": [ ""scale"", ""translate"", ""opacity"", ""color"" ], ""crossfade"": true,
      ""states"": [ { ""state"": ""hover"", ""scale"": ""1.04"" },
                    { ""state"": ""pressed"", ""scale"": ""0.96"", ""translate"": ""0 2px"" },
                    { ""state"": ""disabled"", ""opacity"": ""0.6"", ""color"": ""#80808080"" } ] },
    { ""name"": ""panel"", ""duration"": ""250"", ""easing"": ""cubic-bezier(0.2, 0, 0.2, 1)"", ""properties"": [ ""opacity"" ] } ] }";

        static Dictionary<string, MotionPreset> Resolve(string json, out List<string> errors)
        {
            MotionPresetSet.TryResolveJson(json, out var resolved, out errors);
            return resolved;
        }

        // Fixtures/ExamplePresets.json is what the bridge converter writes (`<Screen>.motion.json`) for the example profile
        // (tools/gen_fixture_bindings.py; tests/test_motion.py checks it is current): the helpers must resolve exactly that
        [Test]
        public void GeneratedPresets_TheBridgeOutputResolvesWithoutErrors()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/com.cuvara.uitoolkit/Tests/Runtime/Motion/Fixtures/ExamplePresets.json");
            Assert.IsNotNull(asset, "fixture missing");
            Assert.IsTrue(MotionPresetSet.TryResolveJson(asset.text, out var presets, out var errors), string.Join("; ", errors));
            Assert.IsEmpty(errors);
            var press = presets["press"];
            Assert.AreEqual(120f, press.Timing.DurationMs);
            Assert.AreEqual(MotionEasing.EaseOut, press.Timing.Easing);
            Assert.AreEqual(MotionChannels.Scale | MotionChannels.Tint, press.Channels);
            Assert.IsFalse(press.Crossfade);
            Assert.IsTrue(press.TryGetState("pressed", out var pressed));
            Assert.AreEqual(0.96f, pressed.Get(MotionChannels.Scale).x, 1e-5f);
            Assert.AreEqual(0.8f, pressed.Get(MotionChannels.Tint).x, 0.01f);
            var fade = presets["fade"];
            Assert.AreEqual(MotionChannels.Opacity, fade.Channels);
            Assert.AreEqual(120f, fade.Timing.DurationMs);
            var hoverFade = presets["hover-fade"];
            Assert.IsTrue(hoverFade.Crossfade);
            Assert.AreEqual(200f, hoverFade.Timing.DurationMs);
            Assert.AreEqual(MotionChannels.TextColor, hoverFade.Channels);
        }

        [Test]
        public void Presets_ValidJsonResolves()
        {
            Assert.IsTrue(MotionPresetSet.TryResolveJson(Valid, out var presets, out var errors), string.Join("; ", errors));
            Assert.AreEqual(2, presets.Count);
            var button = presets["button"];
            Assert.AreEqual(120f, button.Timing.DurationMs);
            Assert.AreEqual(10f, button.Timing.DelayMs);
            Assert.AreEqual(MotionEasing.EaseOut, button.Timing.Easing);
            Assert.IsTrue(button.Crossfade);
            Assert.AreEqual(MotionChannels.Scale | MotionChannels.Translate | MotionChannels.Opacity | MotionChannels.TextColor, button.Channels);
            Assert.IsTrue(button.TryGetState("pressed", out var pressed));
            Assert.AreEqual(0.96f, pressed.Get(MotionChannels.Scale).x, 1e-5f);
            Assert.AreEqual(2f, pressed.Get(MotionChannels.Translate).y, 1e-5f);
            Assert.IsTrue(button.TryGetState("disabled", out var disabled));
            Assert.AreEqual(0.6f, disabled.Get(MotionChannels.Opacity).x, 1e-5f);
            Assert.AreEqual(128f / 255f, disabled.Get(MotionChannels.TextColor).w, 0.01f);
            Assert.IsFalse(button.TryGetState("focus", out _));
            Assert.AreEqual(250f, presets["panel"].Timing.DurationMs, "a number as the duration");
            Assert.IsFalse(presets["panel"].Timing.Easing.IsNamed);
        }

        [Test]
        public void Presets_RestStateIsTheIdentityOfTheNonColourProperties()
        {
            var button = Resolve(Valid, out _)["button"];
            Assert.IsTrue(button.TryGetState(MotionPreset.RestState, out var rest));
            Assert.AreEqual(1f, rest.Get(MotionChannels.Scale).x);
            Assert.AreEqual(0f, rest.Get(MotionChannels.Translate).y);
            Assert.AreEqual(1f, rest.Get(MotionChannels.Opacity).x);
            Assert.AreEqual(MotionChannels.Scale | MotionChannels.Translate | MotionChannels.Opacity, rest.Channels, "colours have no known rest value");
        }

        static string One(string fields) =>
            @"{ ""durations"": [ { ""name"": ""fast"", ""ms"": 120 } ], ""presets"": [ { ""name"": ""p"", " + fields + " } ] }";

        [TestCase(@"""duration"": ""fast"", ""properties"": [ ""width"" ]", "width")]
        [TestCase(@"""duration"": ""fast"", ""properties"": [ ""all"" ]", "'all'")]
        [TestCase(@"""duration"": ""fast"", ""properties"": [ ""margin-left"", ""font-size"", ""-unity-slice-left"", ""left"", ""flex-grow"" ]", "margin-left")]
        [TestCase(@"""duration"": ""slow"", ""properties"": [ ""opacity"" ]", "unknown duration")]
        [TestCase(@"""properties"": [ ""opacity"" ]", "duration is missing")]
        [TestCase(@"""duration"": ""fast"", ""easing"": ""bounce"", ""properties"": [ ""opacity"" ]", "easing")]
        [TestCase(@"""duration"": ""fast"", ""properties"": [ ""translate"" ], ""states"": [ { ""state"": ""hover"", ""translate"": ""10%"" } ]", "bad translate")]
        [TestCase(@"""duration"": ""fast"", ""properties"": [ ""opacity"" ], ""states"": [ { ""state"": ""hover"", ""scale"": ""1.1"" } ]", "do not list it")]
        [TestCase(@"""duration"": ""fast"", ""properties"": [ ""opacity"" ], ""states"": [ { ""state"": ""hover"", ""opacity"": ""abc"" } ]", "bad opacity")]
        [TestCase(@"""duration"": ""fast"", ""properties"": [ ""opacity"" ], ""states"": [ { ""state"": ""hover"", ""opacity"": ""NaN"" } ]", "bad opacity")]
        [TestCase(@"""duration"": ""fast"", ""properties"": [ ""scale"" ], ""states"": [ { ""state"": ""hover"", ""scale"": ""Infinity"" } ]", "bad scale")]
        [TestCase(@"""duration"": ""fast"", ""easing"": ""cubic-bezier(NaN, 0, 1, 1)"", ""properties"": [ ""opacity"" ]", "easing")]
        [TestCase(@"""duration"": ""fast"", ""properties"": [ ""color"" ], ""states"": [ { ""state"": ""hover"", ""color"": ""not-a-colour"" } ]", "bad color")]
        [TestCase(@"""duration"": ""fast"", ""properties"": [ ""opacity"" ], ""states"": [ { ""state"": ""rest"", ""opacity"": ""1"" } ]", "reserved")]
        [TestCase(@"""duration"": ""fast"", ""delayMs"": -5, ""properties"": [ ""opacity"" ]", "negative delay")]
        public void Presets_InvalidDataIsRejectedWithAReason(string fields, string expected)
        {
            var presets = Resolve(One(fields), out var errors);
            Assert.AreEqual(0, presets.Count, "an invalid preset is not registered");
            Assert.IsNotEmpty(errors);
            StringAssert.Contains(expected, string.Join(" | ", errors));
        }

        [Test]
        public void Presets_ALayoutPropertyNamesTheProperty_AndGoodPresetsStillLoad()
        {
            var json = @"{ ""durations"": [ { ""name"": ""fast"", ""ms"": 120 } ], ""presets"": [
              { ""name"": ""bad"", ""duration"": ""fast"", ""properties"": [ ""height"" ] },
              { ""name"": ""good"", ""duration"": ""fast"", ""properties"": [ ""opacity"", ""border-top-color"" ] } ] }";
            var presets = Resolve(json, out var errors);
            Assert.IsTrue(presets.ContainsKey("good"));
            Assert.IsFalse(presets.ContainsKey("bad"));
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("'height'", errors[0]);
        }

        [Test]
        public void Presets_DuplicateNameAndBrokenJson()
        {
            var dup = @"{ ""presets"": [ { ""name"": ""p"", ""duration"": ""5"" }, { ""name"": ""p"", ""duration"": ""6"" } ] }";
            var resolved = Resolve(dup, out var errors);
            Assert.AreEqual(1, resolved.Count);
            StringAssert.Contains("defined twice", string.Join(" | ", errors));
            Assert.IsFalse(MotionPresetSet.TryResolveJson("{ not json", out _, out errors));
            StringAssert.Contains("invalid JSON", errors[0]);
        }

        [Test]
        public void Presets_RegistryLookup_AndFallbackWithAWarning()
        {
            Assert.IsTrue(MotionPresets.Load(Valid));
            try
            {
                Assert.IsTrue(MotionPresets.TryGet("button", out var p));
                Assert.AreEqual(120f, MotionPresets.Timing("button").DurationMs);
                LogAssert.Expect(LogType.Warning, new Regex("'nope' is not loaded"));
                Assert.AreEqual(MotionTiming.Normal.DurationMs, MotionPresets.Timing("nope").DurationMs);
            }
            finally
            {
                MotionPresets.Use(new Dictionary<string, MotionPreset>());
            }
        }

        [Test]
        public void Presets_LoadLogsEveryErrorAndKeepsTheValidOnes()
        {
            LogAssert.Expect(LogType.Error, new Regex("'height'"));
            Assert.IsFalse(MotionPresets.Load(@"{ ""durations"": [ { ""name"": ""f"", ""ms"": 1 } ], ""presets"": [
              { ""name"": ""bad"", ""duration"": ""f"", ""properties"": [ ""height"" ] },
              { ""name"": ""good"", ""duration"": ""f"", ""properties"": [ ""opacity"" ] } ] }"));
            try
            {
                Assert.IsTrue(MotionPresets.TryGet("good", out _));
                Assert.IsFalse(MotionPresets.TryGet("bad", out _));
            }
            finally
            {
                MotionPresets.Use(new Dictionary<string, MotionPreset>());
            }
        }

        [Test]
        public void Presets_StateTweenWritesTheEndValuesOfAnElementOutsideAPanel()
        {
            var button = Resolve(Valid, out _)["button"];
            var element = new VisualElement();
            var handle = button.To(element, "hover").Play();
            Assert.AreEqual(MotionResult.Detached, handle.Result, "not in a panel: the end state is applied at once");
            Assert.AreEqual(1.04f, element.style.scale.value.value.x, 1e-5f);
            LogAssert.Expect(LogType.Warning, new Regex("no state 'focus'"));
            var nothing = button.To(element, "focus").Play(default, new FakeDriver());
            Assert.IsTrue(nothing.IsRunning, "an empty clip");
            nothing.Cancel();
        }

        #endregion

        #region VisualElementTarget

        [Test]
        public void Target_WritesAndReadsInlineStylesWithoutAPanel()
        {
            var element = new VisualElement();
            var t = UiMotion.Target(element);
            t.Set(MotionChannels.Opacity, new Vector4(0.25f, 0, 0, 0));
            t.Set(MotionChannels.Translate, new Vector4(10f, -4f, 0, 0));
            t.Set(MotionChannels.Scale, new Vector4(1.5f, 2f, 0, 0));
            t.Set(MotionChannels.Rotate, new Vector4(45f, 0, 0, 0));
            t.Set(MotionChannels.BackgroundColor, new Vector4(1f, 0f, 0f, 1f));
            t.Set(MotionChannels.Tint, new Vector4(0f, 1f, 0f, 1f));
            t.Set(MotionChannels.TextColor, new Vector4(0f, 0f, 1f, 1f));

            Assert.AreEqual(0.25f, element.style.opacity.value, 1e-5f);
            Assert.AreEqual(0.25f, t.Get(MotionChannels.Opacity).x, 1e-5f, "an inline value shows at once, before resolvedStyle catches up");
            Assert.AreEqual(new Vector2(10f, -4f), (Vector2)t.Get(MotionChannels.Translate));
            Assert.AreEqual(new Vector2(1.5f, 2f), (Vector2)t.Get(MotionChannels.Scale));
            Assert.AreEqual(45f, t.Get(MotionChannels.Rotate).x, 1e-3f);
            Assert.AreEqual(Color.red, (Color)t.Get(MotionChannels.BackgroundColor));
            Assert.AreEqual(Color.green, (Color)t.Get(MotionChannels.Tint));
            Assert.AreEqual(Color.blue, (Color)t.Get(MotionChannels.TextColor));
        }

        /// <summary>The inline transition declaration of an element as (property, duration ms) pairs.</summary>
        static List<KeyValuePair<StylePropertyName, float>> Declared(VisualElement element)
        {
            var result = new List<KeyValuePair<StylePropertyName, float>>();
            var props = element.style.transitionProperty;
            var durations = element.style.transitionDuration;
            if (props.keyword != StyleKeyword.Undefined)
                return result;
            for (var i = 0; i < props.value.Count; i++)
                result.Add(new KeyValuePair<StylePropertyName, float>(props.value[i], durations.value[i % durations.value.Count].value));
            return result;
        }

        static float DurationOf(VisualElement element, string property)
        {
            foreach (var kv in Declared(element))
                if (kv.Key.Equals(new StylePropertyName(property)))
                    return kv.Value;
            return -1f;
        }

        static int Index(VisualElement element, string property)
        {
            var props = element.style.transitionProperty.value;
            for (var i = 0; i < props.Count; i++)
                if (props[i].Equals(new StylePropertyName(property)))
                    return i;
            return -1;
        }

        [Test]
        public void Ledger_MergesTheTimingsOfSeveralOwnersAndTargetObjectsOnOneElement()
        {
            var element = new VisualElement();
            var one = UiMotion.Target(element);
            var two = UiMotion.Target(element); // a second target object for the same element
            var a = new object();
            var b = new object();

            one.BeginTransition(a, MotionChannels.Opacity, 200f, MotionEasing.EaseOut);
            two.BeginTransition(b, MotionChannels.Translate | MotionChannels.Scale, 400f, MotionEasing.Linear);
            Assert.AreEqual(200f, DurationOf(element, "opacity"));
            Assert.AreEqual(400f, DurationOf(element, "translate"));
            Assert.AreEqual(400f, DurationOf(element, "scale"));
            Assert.AreEqual(EasingMode.EaseOut, element.style.transitionTimingFunction.value[Index(element, "opacity")].mode);

            // owner a ends its animation: only its channel goes back to "jump", b is untouched
            one.EndTransition(a);
            Assert.AreEqual(0f, DurationOf(element, "opacity"));
            Assert.AreEqual(400f, DurationOf(element, "translate"));

            // owner a releases: its entry is gone, b keeps its entries
            one.ReleaseTransitions(a, true);
            Assert.AreEqual(-1f, DurationOf(element, "opacity"));
            Assert.AreEqual(400f, DurationOf(element, "translate"));
            Assert.AreEqual(1, TransitionLedger.ClaimCount(element));

            two.ReleaseTransitions(b, true);
            Assert.AreEqual(0, TransitionLedger.ClaimCount(element));
            Assert.AreEqual(StyleKeyword.Null, element.style.transitionProperty.keyword, "nothing was declared before: nothing is left");
        }

        [Test]
        public void Ledger_PutsBackTheDeclarationThatWasThereBeforeTheFirstClaim()
        {
            var element = new VisualElement();
            element.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("color") };
            element.style.transitionDuration = new List<TimeValue> { new TimeValue(500f, TimeUnit.Millisecond) };
            var t = UiMotion.Target(element);
            var owner = new object();

            t.SuppressTransitions(owner, MotionChannels.Opacity);
            Assert.AreEqual(0f, DurationOf(element, "opacity"), "writes to the channel jump");

            t.ReleaseTransitions(owner, true);
            Assert.AreEqual(1, Declared(element).Count);
            Assert.AreEqual(500f, DurationOf(element, "color"));
            Assert.AreEqual(-1f, DurationOf(element, "opacity"));
        }

        [Test]
        public void Ledger_TheNewestClaimOwnsAChannel()
        {
            var element = new VisualElement();
            var t = UiMotion.Target(element);
            var a = new object();
            var b = new object();
            t.BeginTransition(a, MotionChannels.Opacity, 300f, MotionEasing.Linear);
            t.BeginTransition(b, MotionChannels.Opacity, 100f, MotionEasing.Linear);
            Assert.AreEqual(100f, DurationOf(element, "opacity"));
            var count = 0;
            foreach (var kv in Declared(element))
                if (kv.Key.Equals(new StylePropertyName("opacity")))
                    count++;
            Assert.AreEqual(1, count, "one entry per property");
            t.ReleaseTransitions(a, true);
            t.ReleaseTransitions(b, true);
            Assert.AreEqual(0, TransitionLedger.ClaimCount(element));
        }

        [Test]
        public void Ledger_AFinishedRunLeavesNoClaimBehind()
        {
            var element = new VisualElement(); // not in a panel: the run ends at once and releases at once
            var handle = UiMotion.Tween(element, new MotionValues().WithOpacity(0f).WithScale(2f), MotionTiming.Ms(300f)).Play();
            Assert.AreEqual(MotionResult.Detached, handle.Result);
            Assert.AreEqual(0, TransitionLedger.ClaimCount(element));
            Assert.AreEqual(StyleKeyword.Null, element.style.transitionProperty.keyword);
        }

        [Test]
        public void ResetStyles_RemovesTheInlineValuesAndReleasesTheClaims()
        {
            var element = new VisualElement();
            var t = UiMotion.Target(element);
            t.Set(MotionChannels.Opacity, new Vector4(0.1f, 0, 0, 0));
            t.Set(MotionChannels.Scale, new Vector4(3f, 3f, 0, 0));
            t.BeginTransition(new object(), MotionChannels.Opacity, 100f, MotionEasing.Linear);
            UiMotion.ResetStyles(element);
            Assert.AreEqual(StyleKeyword.Null, element.style.opacity.keyword);
            Assert.AreEqual(StyleKeyword.Null, element.style.scale.keyword);
            Assert.AreEqual(0, TransitionLedger.ClaimCount(element));
            Assert.AreEqual(StyleKeyword.Null, element.style.transitionProperty.keyword);
        }

        [Test]
        public void Target_IsNotAliveOutsideAPanel_AndTheDetachWatcherIsRemovable()
        {
            var element = new VisualElement();
            var t = UiMotion.Target(element);
            Assert.IsFalse(t.IsAlive);
            System.Action cb = () => { };
            t.WatchDetach(cb);
            t.UnwatchDetach(cb);
            Assert.IsTrue(t.SupportsTransitions);
            Assert.AreNotSame(t.CreateDriver(), t.CreateDriver(), "every run gets its own driver");
        }

        #endregion
    }
}
