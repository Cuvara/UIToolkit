using System.IO;
using Cuvara.UIToolkit.Editor.SlicedArt;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Cuvara.UIToolkit.Editor.Tests
{
    /// <summary>
    /// The sidecar format is a contract with the tools that write it. The penpot-uitoolkit-bridge converter
    /// validates the same file: its tests/fixtures/contract/contract.slice.json is byte-identical to
    /// Fixtures/contract.slice.json here.
    /// </summary>
    public class SliceSidecarContractTests
    {
        const string FixtureInPackage = "Tests/Editor/SlicedArt/Fixtures/contract.slice.json";

        // The package may be embedded or installed from a file: path, so the virtual Packages/<name>
        // path is not a directory; resolve the physical package root before using System.IO.
        static string FixturePath()
        {
            var info = PackageInfo.FindForAssembly(typeof(SliceSidecar).Assembly);
            Assert.IsNotNull(info, "SliceSidecar is not inside a package");
            return Path.Combine(info.resolvedPath, FixtureInPackage);
        }

        [Test]
        public void SharedContractFixtureLoads()
        {
            var folder = new SlicedArtTempFolder("CuvaraUIToolkitTests_SliceContract");
            try
            {
                var text = File.ReadAllText(FixturePath());
                var path = folder.Write("contract.slice.json", text);
                Assert.IsTrue(SliceSidecar.TryLoad(path, out var sidecar, out var error), error);
                CollectionAssert.AreEqual(new[] { 4f, 6f, 8f, 10f }, sidecar.border);
                Assert.AreEqual(new Vector4(4f, 10f, 8f, 6f), sidecar.ToSpriteBorder());
                Assert.AreEqual(100f, sidecar.pixelsPerUnit);
                Assert.AreEqual(2048, sidecar.maxSize);
                Assert.AreEqual("ui", sidecar.group);
            }
            finally
            {
                folder.Delete();
            }
        }
    }
}
