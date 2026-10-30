using System.Runtime.CompilerServices;

// CuvaraTheme.OverrideDefaultStyleSheet is internal: tests use it to exercise the inline
// fallback without stripping the asset, and hosts have no reason to.
[assembly: InternalsVisibleTo("Cuvara.UIToolkit.Tests")]
[assembly: InternalsVisibleTo("Cuvara.UIToolkit.Flow.Tests")]
