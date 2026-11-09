#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.UIElements;

namespace Cuvara.UIToolkit.Prototype.Tests
{
    /// <summary>
    /// The runtime theme of the tests: a fixture that imports Unity's default runtime theme, the same content as the
    /// <c>UnityDefaultRuntimeTheme.tss</c> a project creates. The bridge's tests found that file in the client project; this
    /// package's CI project has none, so the fixture keeps the tests independent of what the host project contains.
    /// </summary>
    internal static class TestTheme
    {
        public const string Path = "Packages/com.cuvara.uitoolkit/Tests/Runtime/Prototype/Fixtures/PrototypeTestTheme.tss";

        public static ThemeStyleSheet Load()
        {
            return AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Path);
        }
    }
}
#endif
