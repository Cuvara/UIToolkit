#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Cuvara.UIToolkit.Prototype.Tests
{
    /// <summary>The version rule of the <c>flow.json</c> contract (Documentation~/PROTOTYPE-FLOW.md).</summary>
    public class PrototypeFlowContractTests
    {
        const string Fixtures = "Packages/com.cuvara.uitoolkit/Tests/Runtime/Prototype/Fixtures/";

        [Test]
        public void TheRunnerReadsVersionOne()
        {
            Assert.AreEqual(1, PrototypeFlowFile.SupportedVersion);
        }

        [TestCase("menu")]
        [TestCase("detail")]
        [TestCase("dialog")]
        [TestCase("scroll")]
        [TestCase("timer")]
        public void EveryFixtureIsAVersionOneFile(string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Fixtures + name + ".flow.json");
            Assert.IsNotNull(asset, name);
            var file = PrototypeFlowFile.Parse(asset.text);
            Assert.AreEqual(PrototypeFlowFile.SupportedVersion, file.version);
            Assert.IsFalse(string.IsNullOrEmpty(file.screen));
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(10)]
        public void AnUnknownMajorVersionIsRejected(int version)
        {
            var json = "{\"version\": " + version + ", \"screen\": \"Menu\", \"interactions\": []}";
            var e = Assert.Throws<FormatException>(() => PrototypeFlowFile.Parse(json));
            StringAssert.Contains("version " + version, e.Message);
        }

        [Test]
        public void AFileWithoutAVersionIsRejected()
        {
            Assert.Throws<FormatException>(() => PrototypeFlowFile.Parse("{\"screen\": \"Menu\"}"));
        }

        [Test]
        public void FieldsTheRunnerDoesNotReadAreIgnored()
        {
            // additive changes keep the major: the converter already writes control fields for the bridge's gates
            // (size, component, label, variants, crossfade) that the runner never reads
            var json = "{\"version\": 1, \"screen\": \"Menu\", \"futureField\": {\"a\": 1}," +
                       " \"controls\": [{\"name\": \"Go\", \"type\": \"Button\", \"enabled\": true, \"size\": [10, 20]," +
                       " \"crossfade\": {\"frame\": \"Go__frame\"}}]," +
                       " \"interactions\": [{\"id\": \"Menu.Go.click#0\", \"element\": \"Go\", \"trigger\": \"click\", \"action\": \"navigate-to\"," +
                       " \"target\": \"Detail\", \"manual\": null, \"animation\": null}]}";
            var file = PrototypeFlowFile.Parse(json);
            Assert.AreEqual("Go", file.controls[0].name);
            Assert.AreEqual("navigate-to", file.interactions[0].action);
            var animation = file.interactions[0].animation;
            Assert.IsTrue(animation == null || !animation.IsSet, "a null animation plays nothing");
            Assert.AreEqual("reverse", file.motion.back, "a file without motion keeps the defaults");
            Assert.IsTrue(file.motion.enabled);
        }
    }
}
#endif
