namespace Cuvara.UIToolkit.Editor.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using Cuvara.UIToolkit.FigmaCore;
    using NUnit.Framework;

    /// <summary>
    /// The Figma connector stays optional. Runs in the ungated editor test assembly, so it runs
    /// whether or not <c>com.unity.ui.figma</c> is installed: no ungated assembly may reference the
    /// gated integration or the connector, the gated assemblies keep their
    /// <c>versionDefines</c> + <c>defineConstraints</c>, nothing compiles against the connector's
    /// internal editor assembly, and <c>Editor/FigmaCore</c> stays Unity-free.
    /// </summary>
    /// <remarks>
    /// Reads the asmdef JSON from the package folder with the package's own Unity-free
    /// <see cref="FigmaJson"/> reader. Folders ending in <c>~</c> are not Unity-visible and are
    /// skipped, exactly as the Editor skips them.
    /// </remarks>
    public class FigmaIsolationTests
    {
        private const string FigmaDefine = "CUVARA_UITOOLKIT_FIGMA";
        private const string ConnectorPackage = "com.unity.ui.figma";
        private const string GatedAssembly = "Cuvara.UIToolkit.Editor.Figma";
        private const string ConnectorEditorAssembly = "Unity.UIToolkit.Figma.Editor";

        private static readonly string[] GatedAsmdefs =
        {
            "Editor/Figma/Cuvara.UIToolkit.Editor.Figma.asmdef",
            "Tests/Editor/Figma/Cuvara.UIToolkit.Editor.Figma.Tests.asmdef",
        };

        private sealed class Asmdef
        {
            public string RelativePath;
            public string Guid;
            public string Name;
            public List<string> References;
            public List<string> DefineConstraints;
            public List<object> VersionDefines;
            public List<string> IncludePlatforms;

            public bool IsGated => this.DefineConstraints.Contains(FigmaDefine);

            public bool IsEditorOnly => this.IncludePlatforms.Count == 1 && this.IncludePlatforms[0] == "Editor";

            public override string ToString() => this.RelativePath;
        }

        private static string _packageRoot;
        private static List<Asmdef> _asmdefs;

        private static string PackageRoot
        {
            get
            {
                if (_packageRoot != null) return _packageRoot;
#if UNITY_EDITOR
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(FigmaImportValidator).Assembly);
                if (info != null && Directory.Exists(info.resolvedPath)) return _packageRoot = info.resolvedPath;
#endif
                // Outside a Unity project (the plain .NET harness): this file is Tests/Editor/<name>.cs.
                return _packageRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile()), "..", ".."));
            }
        }

        private static List<Asmdef> Asmdefs => _asmdefs ?? (_asmdefs = LoadAsmdefs(PackageRoot));

        private static string ThisFile([CallerFilePath] string path = "") => path;

        private static List<Asmdef> LoadAsmdefs(string root)
        {
            var result = new List<Asmdef>();
            foreach (var path in UnityVisibleFiles(root, "*.asmdef"))
            {
                var json = FigmaJson.Parse(File.ReadAllText(path));
                var metaPath = path + ".meta";
                var guid = File.Exists(metaPath)
                    ? File.ReadAllLines(metaPath).Select(l => l.Trim()).Where(l => l.StartsWith("guid:", StringComparison.Ordinal)).Select(l => l.Substring(5).Trim()).FirstOrDefault()
                    : null;

                result.Add(new Asmdef
                {
                    RelativePath = path.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/'),
                    Guid = guid,
                    Name = FigmaJson.MemberString(json, "name"),
                    References = Strings(json, "references"),
                    DefineConstraints = Strings(json, "defineConstraints"),
                    VersionDefines = FigmaJson.MemberArray(json, "versionDefines"),
                    IncludePlatforms = Strings(json, "includePlatforms"),
                });
            }

            result.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));
            return result;
        }

        private static List<string> Strings(object json, string member)
        {
            return FigmaJson.MemberArray(json, member).OfType<string>().ToList();
        }

        private static IEnumerable<string> UnityVisibleFiles(string root, string pattern)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                foreach (var child in Directory.GetDirectories(directory))
                {
                    var name = Path.GetFileName(child);
                    if (name.EndsWith("~", StringComparison.Ordinal) || name.StartsWith(".", StringComparison.Ordinal)) continue;
                    pending.Push(child);
                }

                foreach (var file in Directory.GetFiles(directory, pattern)) yield return file;
            }
        }

        // A reference names an assembly either by name or as "GUID:<asmdef meta guid>".
        private static bool References(Asmdef asmdef, Asmdef target)
        {
            return asmdef.References.Any(r => r == target.Name || (target.Guid != null && r == "GUID:" + target.Guid));
        }

        private static Asmdef Find(string relativePath)
        {
            var asmdef = Asmdefs.SingleOrDefault(a => a.RelativePath == relativePath);
            Assert.That(asmdef, Is.Not.Null, relativePath + " must exist");
            return asmdef;
        }

        [Test]
        public void PackageRoot_HoldsTheAsmdefs()
        {
            Assert.That(File.Exists(Path.Combine(PackageRoot, "package.json")), Is.True, PackageRoot);
            Assert.That(Asmdefs.Count, Is.GreaterThanOrEqualTo(6), string.Join(", ", Asmdefs));
            Assert.That(Asmdefs.Select(a => a.Name), Does.Contain("Cuvara.UIToolkit"));
            Assert.That(Asmdefs.Select(a => a.Name), Does.Contain(GatedAssembly));
        }

        [Test]
        public void AsmdefFileName_EqualsItsName_WithThePackagePrefix()
        {
            foreach (var asmdef in Asmdefs)
            {
                Assert.That(Path.GetFileNameWithoutExtension(asmdef.RelativePath), Is.EqualTo(asmdef.Name), asmdef.RelativePath);
                Assert.That(asmdef.Name, Does.StartWith("Cuvara.UIToolkit"), asmdef.RelativePath);
            }
        }

        [TestCase("Editor/Figma/Cuvara.UIToolkit.Editor.Figma.asmdef")]
        [TestCase("Tests/Editor/Figma/Cuvara.UIToolkit.Editor.Figma.Tests.asmdef")]
        public void GatedAssembly_KeepsVersionDefineAndDefineConstraint(string relativePath)
        {
            var asmdef = Find(relativePath);

            Assert.That(asmdef.DefineConstraints, Does.Contain(FigmaDefine));
            var versionDefine = asmdef.VersionDefines.SingleOrDefault(v => FigmaJson.MemberString(v, "name") == ConnectorPackage);
            Assert.That(versionDefine, Is.Not.Null, relativePath + " must define " + FigmaDefine + " from " + ConnectorPackage);
            Assert.That(FigmaJson.MemberString(versionDefine, "define"), Is.EqualTo(FigmaDefine));
            Assert.That(FigmaJson.MemberString(versionDefine, "expression"), Is.Not.Null.And.Not.Empty);
            Assert.That(asmdef.IsEditorOnly, Is.True, relativePath + " must stay Editor-only");
        }

        [Test]
        public void OnlyTheKnownAssemblies_AreGated()
        {
            var gated = Asmdefs.Where(a => a.IsGated).Select(a => a.RelativePath).ToArray();

            Assert.That(gated, Is.EqualTo(GatedAsmdefs));
        }

        [Test]
        public void NoUngatedAssembly_ReferencesTheIntegrationOrTheConnector()
        {
            var integration = Find(GatedAsmdefs[0]);
            foreach (var asmdef in Asmdefs.Where(a => !a.IsGated))
            {
                Assert.That(References(asmdef, integration), Is.False,
                    asmdef + " is not gated on " + FigmaDefine + " but references " + GatedAssembly + "; it would fail to compile without the connector");
                Assert.That(asmdef.References.Any(r => r.IndexOf("Figma", StringComparison.OrdinalIgnoreCase) >= 0), Is.False,
                    asmdef + " is not gated but names a Figma assembly: " + string.Join(", ", asmdef.References));
                Assert.That(asmdef.VersionDefines.Any(v => FigmaJson.MemberString(v, "name") == ConnectorPackage), Is.False,
                    asmdef + " reacts to the connector but is not gated by it");
            }
        }

        [Test]
        public void NoAssembly_CompilesAgainstTheConnectorsInternalEditorAssembly()
        {
            foreach (var asmdef in Asmdefs)
            {
                Assert.That(asmdef.References, Does.Not.Contain(ConnectorEditorAssembly), asmdef.RelativePath);
            }
        }

        [Test]
        public void RuntimeAssemblies_HaveNoFigmaKnowledge()
        {
            foreach (var asmdef in Asmdefs.Where(a => !a.IsEditorOnly && a.RelativePath.StartsWith("Runtime/", StringComparison.Ordinal)))
            {
                Assert.That(asmdef.References.Any(r => r.IndexOf("Figma", StringComparison.OrdinalIgnoreCase) >= 0 || r.IndexOf(".Editor", StringComparison.Ordinal) >= 0), Is.False,
                    asmdef + ": " + string.Join(", ", asmdef.References));
            }
        }

        [Test]
        public void FigmaCore_StaysUnityFree()
        {
            foreach (var file in Directory.GetFiles(Path.Combine(PackageRoot, "Editor", "FigmaCore"), "*.cs"))
            {
                var text = File.ReadAllText(file);
                Assert.That(text, Does.Not.Contain("using UnityEngine"), file);
                Assert.That(text, Does.Not.Contain("using UnityEditor"), file);
                Assert.That(text, Does.Not.Contain("UnityEngine."), file);
                Assert.That(text, Does.Not.Contain("UnityEditor."), file);
            }
        }

        [Test]
        public void LoadedPackageAssemblies_DoNotLinkTheConnector_ExceptTheGatedOnes()
        {
            var gatedNames = new HashSet<string>(Asmdefs.Where(a => a.IsGated).Select(a => a.Name));
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = assembly.GetName().Name;
                if (!name.StartsWith("Cuvara.UIToolkit", StringComparison.Ordinal) || gatedNames.Contains(name)) continue;

                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    Assert.That(reference.Name, Is.Not.EqualTo(GatedAssembly), name);
                    Assert.That(reference.Name, Is.Not.EqualTo(ConnectorEditorAssembly), name);
                }
            }
        }
    }
}
