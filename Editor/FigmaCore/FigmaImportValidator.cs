namespace Cuvara.UIToolkit.FigmaCore
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Xml;

    /// <summary>
    /// Checks one connector import folder (<c>Assets/Figma/&lt;file&gt;/</c>, written by
    /// <c>com.unity.ui.figma</c>) against this package's rules: manifest integrity, class
    /// prefix, token coverage, inline style and absolute positioning budgets, and asset
    /// references. Rule codes are stable; see <c>Documentation~/FIGMA-VALIDATION.md</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>This file must stay Unity-free</b>, like <c>Editor/Codegen/Core</c>: the
    /// <c>Tools~/UxmlCodegenCli</c> <c>validate</c> verb compiles it under plain .NET, so CI
    /// and agents can check committed output without Unity or the connector.</para>
    ///
    /// <para>It only reads files. It never writes into the import folder: the connector owns
    /// it, and its write-back would treat any edit as a user change.</para>
    /// </remarks>
    public static class FigmaImportValidator
    {
        public const string ManifestMissing = "FIG001";
        public const string ManifestUnreadable = "FIG002";
        public const string ScreenUxmlMissing = "FIG003";
        public const string GeneratedFileMissing = "FIG004";
        public const string UxmlMalformed = "FIG005";
        public const string UssMalformed = "FIG006";
        public const string FolderMissing = "FIG007";
        public const string ClassPrefixMismatch = "FIG010";
        public const string LiteralColor = "FIG020";
        public const string LiteralColorSummary = "FIG021";
        public const string UndefinedToken = "FIG022";
        public const string InlineStyleVariable = "FIG023";
        public const string InlineStyleBudget = "FIG030";
        public const string LayoutSummary = "FIG031";
        public const string AbsoluteBudget = "FIG032";
        public const string UnresolvedUrl = "FIG040";
        public const string ProjectRootNotFound = "FIG041";
        public const string StyleSrcMissing = "FIG050";
        public const string TemplateSrcMissing = "FIG051";

        /// <summary>The connector's token sheet: <c>:root</c> custom properties.</summary>
        public const string TokensFileName = "tokens.uss";

        private const string ProjectScheme = "project://database/";

        /// <summary>Validates <paramref name="importFolderPath"/>. Never throws for content
        /// problems; every problem is a diagnostic. The list is sorted deterministically
        /// (<see cref="FigmaDiagnostics.Compare"/>).</summary>
        public static IReadOnlyList<FigmaDiagnostic> ValidateFolder(string importFolderPath, FigmaValidationOptions options)
        {
            var run = new Run(importFolderPath, options ?? new FigmaValidationOptions());
            run.Execute();
            run.Diagnostics.Sort(FigmaDiagnostics.Compare);
            return run.Diagnostics;
        }

        private sealed class Run
        {
            public readonly List<FigmaDiagnostic> Diagnostics = new List<FigmaDiagnostic>();

            private readonly string _folder;
            private readonly FigmaValidationOptions _options;
            private readonly HashSet<string> _definedTokens = new HashSet<string>(StringComparer.Ordinal);
            private string _projectRoot;
            private int _literalColorCount;
            private int _uncheckedProjectReferences;

            public Run(string folder, FigmaValidationOptions options)
            {
                _folder = string.IsNullOrEmpty(folder) ? string.Empty : Path.GetFullPath(folder);
                _options = options;
            }

            public void Execute()
            {
                if (_folder.Length == 0 || !Directory.Exists(_folder))
                {
                    Add(FolderMissing, FigmaDiagnosticSeverity.Error, string.Empty, 0, $"import folder '{_folder}' does not exist");
                    return;
                }

                _projectRoot = FindProjectRoot(_folder);

                var manifest = ReadManifest();
                if (manifest != null) CheckManifest(manifest);

                var ussFiles = EnumerateFiles("*.uss");
                var uxmlFiles = EnumerateFiles("*.uxml");

                // Pass 1: parse sheets and collect every custom property the folder defines, so
                // var() checks do not depend on file order.
                var sheets = new List<KeyValuePair<string, UssSheet>>();
                foreach (var path in ussFiles)
                {
                    var sheet = FigmaUss.Parse(ReadText(path));
                    sheets.Add(new KeyValuePair<string, UssSheet>(path, sheet));
                    foreach (var rule in sheet.Rules)
                    {
                        foreach (var declaration in rule.Declarations)
                        {
                            if (declaration.Property.StartsWith("--", StringComparison.Ordinal)) _definedTokens.Add(declaration.Property);
                        }
                    }
                }

                if (manifest != null)
                {
                    foreach (var name in manifest.TokenNames) _definedTokens.Add(name);
                }

                foreach (var pair in sheets) CheckSheet(pair.Key, pair.Value);
                foreach (var path in uxmlFiles) CheckUxml(path);

                if (_options.RequireTokenForColors)
                {
                    Add(LiteralColorSummary, FigmaDiagnosticSeverity.Info, string.Empty, 0,
                        $"{_literalColorCount.ToString(CultureInfo.InvariantCulture)} literal colour(s) not bound to a token");
                }

                if (_uncheckedProjectReferences > 0)
                {
                    Add(ProjectRootNotFound, FigmaDiagnosticSeverity.Warning, string.Empty, 0,
                        $"no 'Assets' folder above the import folder, so {_uncheckedProjectReferences.ToString(CultureInfo.InvariantCulture)} project:// or /Assets reference(s) were not checked");
                }
            }

            private FigmaManifestInfo ReadManifest()
            {
                var path = Path.Combine(_folder, FigmaManifestReader.ManifestFileName);
                if (!File.Exists(path))
                {
                    Add(ManifestMissing, FigmaDiagnosticSeverity.Error, string.Empty, 0,
                        $"{FigmaManifestReader.ManifestFileName} not found: this is not a connector import folder, or it was not committed");
                    return null;
                }

                try
                {
                    return FigmaManifestReader.Read(File.ReadAllText(path));
                }
                catch (Exception exception) when (exception is FormatException || exception is IOException || exception is UnauthorizedAccessException)
                {
                    Add(ManifestUnreadable, FigmaDiagnosticSeverity.Error, FigmaManifestReader.ManifestFileName, 0, exception.Message);
                    return null;
                }
            }

            private void CheckManifest(FigmaManifestInfo manifest)
            {
                foreach (var screen in manifest.Screens)
                {
                    var uxmlName = screen.AssetName + ".uxml";
                    var uxmlPath = InsideFolder(uxmlName);
                    if (screen.AssetName.Length > 0 && (uxmlPath == null || !File.Exists(uxmlPath)))
                    {
                        Add(ScreenUxmlMissing, FigmaDiagnosticSeverity.Error, FigmaManifestReader.ManifestFileName, 0,
                            $"screen '{screen.AssetName}' (node {screen.RootNodeId}) has no {uxmlName}");
                    }

                    foreach (var generated in screen.Generated)
                    {
                        if (string.Equals(generated, uxmlName, StringComparison.Ordinal)) continue; // reported above
                        var generatedPath = InsideFolder(generated);
                        if (generatedPath == null)
                        {
                            // The connector only writes inside its folder; a path that leaves it is
                            // not something an import produced, whether or not it exists.
                            Add(GeneratedFileMissing, FigmaDiagnosticSeverity.Warning, FigmaManifestReader.ManifestFileName, 0,
                                $"screen '{screen.AssetName}' lists generated file '{generated}', which is outside the import folder");
                        }
                        else if (!File.Exists(generatedPath))
                        {
                            Add(GeneratedFileMissing, FigmaDiagnosticSeverity.Warning, FigmaManifestReader.ManifestFileName, 0,
                                $"screen '{screen.AssetName}' lists generated file '{generated}', which does not exist");
                        }
                    }
                }
            }

            private void CheckSheet(string path, UssSheet sheet)
            {
                var file = Relative(path);
                if (sheet.Problem != null)
                {
                    Add(UssMalformed, FigmaDiagnosticSeverity.Warning, file, sheet.ProblemLine, "USS is not well formed: " + sheet.Problem);
                }

                foreach (var import in sheet.Imports)
                {
                    CheckUrl(file, Path.GetDirectoryName(path), import.Line, import.Target);
                }

                var tokenSheet = IsTokenSheet(path, sheet);
                var prefix = string.IsNullOrEmpty(_options.ExpectedClassPrefix) ? null : _options.ExpectedClassPrefix + "-";
                var reportedClasses = new HashSet<string>(StringComparer.Ordinal);

                foreach (var rule in sheet.Rules)
                {
                    if (!tokenSheet && prefix != null)
                    {
                        foreach (var className in FigmaUss.SelectorClasses(rule.Selector))
                        {
                            if (className.StartsWith("unity-", StringComparison.Ordinal)) continue; // built-in control classes
                            if (className.StartsWith(prefix, StringComparison.Ordinal)) continue;
                            if (!reportedClasses.Add(className)) continue;
                            Add(ClassPrefixMismatch, FigmaDiagnosticSeverity.Warning, file, rule.Line,
                                $"class '.{className}' does not start with the expected prefix '{prefix}' (Project Settings > UI Toolkit > Connector for Figma > USS class prefix)");
                        }
                    }

                    foreach (var declaration in rule.Declarations)
                    {
                        // Token sheets define the tokens: a literal there IS the token value.
                        var checkColors = !tokenSheet && !declaration.Property.StartsWith("--", StringComparison.Ordinal);
                        CheckDeclaration(file, Path.GetDirectoryName(path), declaration, checkColors, null);
                    }
                }
            }

            private void CheckUxml(string path)
            {
                var file = Relative(path);
                var directory = Path.GetDirectoryName(path);
                var inlineStyled = 0;
                var absolute = 0;

                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                };

                try
                {
                    using (var stream = File.OpenRead(path))
                    using (var reader = XmlReader.Create(stream, settings))
                    {
                        var lineInfo = reader as IXmlLineInfo;
                        while (reader.Read())
                        {
                            if (reader.NodeType != XmlNodeType.Element) continue;
                            var line = lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;
                            var tag = reader.LocalName;

                            if (tag == "Style") CheckSource(file, directory, line, reader.GetAttribute("src"), StyleSrcMissing, "<Style src>");
                            else if (tag == "Template") CheckSource(file, directory, line, reader.GetAttribute("src"), TemplateSrcMissing, "<Template src>");

                            if (!reader.MoveToFirstAttribute()) continue;
                            do
                            {
                                var name = reader.LocalName;
                                var value = reader.Value ?? string.Empty;
                                if (name == "style")
                                {
                                    if (value.Trim().Length == 0) continue;
                                    inlineStyled++;
                                    var isAbsolute = false;
                                    foreach (var declaration in FigmaUss.ParseInline(value, line))
                                    {
                                        if (FigmaUss.IsAbsolutePosition(declaration)) isAbsolute = true;
                                        if (FigmaUss.UsesVariable(declaration.Value))
                                        {
                                            // Unity 6000.3: VisualTreeAsset.CloneTree/Instantiate throws a
                                            // NullReferenceException in StyleVariableResolver for var() in an
                                            // inline style, attached or not. USS variables are class-rule only.
                                            Add(InlineStyleVariable, FigmaDiagnosticSeverity.Error, file, line,
                                                $"inline style '{declaration.Property}: {declaration.Value}' on <{tag}> uses var(); UI Toolkit cannot clone this UXML (NullReferenceException in CloneTree). Bind the layer through a Figma component so the value lands in a class rule, or unbind the variable");
                                        }

                                        CheckDeclaration(file, directory, declaration,
                                            !declaration.Property.StartsWith("--", StringComparison.Ordinal), tag);
                                    }

                                    if (isAbsolute) absolute++;
                                }
                                else if (name != "src" && value.StartsWith(ProjectScheme, StringComparison.Ordinal))
                                {
                                    // Bare image URIs land in attributes such as icon-image.
                                    CheckUrl(file, directory, line, value);
                                }
                            }
                            while (reader.MoveToNextAttribute());

                            reader.MoveToElement();
                        }
                    }
                }
                catch (XmlException exception)
                {
                    Add(UxmlMalformed, FigmaDiagnosticSeverity.Error, file, exception.LineNumber, "UXML is not well formed: " + exception.Message);
                    return;
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    Add(UxmlMalformed, FigmaDiagnosticSeverity.Error, file, 0, "UXML cannot be read: " + exception.Message);
                    return;
                }

                Add(LayoutSummary, FigmaDiagnosticSeverity.Info, file, 0,
                    $"{inlineStyled.ToString(CultureInfo.InvariantCulture)} inline-styled element(s), {absolute.ToString(CultureInfo.InvariantCulture)} absolute");

                if (_options.MaxInlineStyledElements >= 0 && inlineStyled > _options.MaxInlineStyledElements)
                {
                    Add(InlineStyleBudget, FigmaDiagnosticSeverity.Warning, file, 0,
                        $"{inlineStyled.ToString(CultureInfo.InvariantCulture)} inline-styled elements, over the limit of {_options.MaxInlineStyledElements.ToString(CultureInfo.InvariantCulture)}: make repeated layers Figma components so they share a class");
                }

                if (_options.MaxAbsoluteElements >= 0 && absolute > _options.MaxAbsoluteElements)
                {
                    Add(AbsoluteBudget, FigmaDiagnosticSeverity.Warning, file, 0,
                        $"{absolute.ToString(CultureInfo.InvariantCulture)} absolute-positioned elements, over the limit of {_options.MaxAbsoluteElements.ToString(CultureInfo.InvariantCulture)}: use auto layout in Figma so the screen scales");
                }
            }

            private void CheckDeclaration(string file, string directory, UssDeclaration declaration, bool checkColors, string elementTag)
            {
                var where = elementTag == null ? string.Empty : $" (inline style on <{elementTag}>)";

                if (checkColors && _options.RequireTokenForColors)
                {
                    foreach (var color in FigmaUss.FindLiteralColors(declaration.Property, declaration.Value))
                    {
                        _literalColorCount++;
                        Add(LiteralColor, FigmaDiagnosticSeverity.Warning, file, declaration.Line,
                            $"'{declaration.Property}: {color}' is a literal colour{where}; bind it to a Figma variable so it becomes var(--token)");
                    }
                }

                foreach (var reference in FigmaUss.FindVarReferences(declaration.Value))
                {
                    var name = reference.Key;
                    if (reference.Value) continue; // has a fallback
                    if (name.StartsWith("--unity-", StringComparison.Ordinal) || name.StartsWith("--cuvara-", StringComparison.Ordinal)) continue;
                    if (_definedTokens.Contains(name)) continue;
                    Add(UndefinedToken, FigmaDiagnosticSeverity.Error, file, declaration.Line,
                        $"var({name}) in '{declaration.Property}'{where} is not defined by tokens.uss or any mode sheet in the folder");
                }

                foreach (var url in FigmaUss.FindUrls(declaration.Value))
                {
                    CheckUrl(file, directory, declaration.Line, url);
                }
            }

            private void CheckSource(string file, string directory, int line, string source, string code, string what)
            {
                if (string.IsNullOrEmpty(source))
                {
                    Add(code, FigmaDiagnosticSeverity.Error, file, line, what + " is empty");
                    return;
                }

                var resolved = Resolve(directory, source);
                if (resolved == null) return;
                if (!File.Exists(resolved))
                {
                    Add(code, FigmaDiagnosticSeverity.Error, file, line, $"{what} '{source}' does not exist");
                }
            }

            private void CheckUrl(string file, string directory, int line, string target)
            {
                var resolved = Resolve(directory, target);
                if (resolved == null) return;
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    Add(UnresolvedUrl, FigmaDiagnosticSeverity.Error, file, line, $"url '{target}' does not resolve to a file");
                }
            }

            // Full path for an asset reference, or null when it cannot or need not be checked.
            private string Resolve(string directory, string target)
            {
                if (string.IsNullOrEmpty(target)) return null;
                var path = target;
                var cut = path.IndexOfAny(new[] { '?', '#' });
                if (cut >= 0) path = path.Substring(0, cut);
                if (path.Length == 0) return null;

                var projectRelative = false;
                if (path.StartsWith(ProjectScheme, StringComparison.Ordinal))
                {
                    path = path.Substring(ProjectScheme.Length);
                    projectRelative = true;
                }
                else if (path.StartsWith("/", StringComparison.Ordinal))
                {
                    path = path.Substring(1);
                    projectRelative = true;
                }
                else if (path.IndexOf("://", StringComparison.Ordinal) >= 0 || path.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    return null; // another scheme: nothing on disk to check
                }

                path = Uri.UnescapeDataString(path);

                if (!projectRelative) return Path.GetFullPath(Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar)));

                // Packages/ resolve through the package manager (and its cache), not the disk layout.
                if (path.StartsWith("Packages/", StringComparison.Ordinal)) return null;

                if (_projectRoot == null)
                {
                    _uncheckedProjectReferences++;
                    return null;
                }

                return Path.GetFullPath(Path.Combine(_projectRoot, path.Replace('/', Path.DirectorySeparatorChar)));
            }

            // Full path of a manifest entry relative to the import folder, or null when the
            // entry is rooted or climbs out of the folder. Both slash styles are accepted.
            private string InsideFolder(string relative)
            {
                var normalized = relative.Replace('\\', '/');
                if (normalized.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(normalized)) return null;

                var full = Path.GetFullPath(Path.Combine(_folder, normalized.Replace('/', Path.DirectorySeparatorChar)));
                var root = _folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
            }

            private bool IsTokenSheet(string path, UssSheet sheet)
            {
                if (string.Equals(Path.GetFileName(path), TokensFileName, StringComparison.OrdinalIgnoreCase)) return true;

                // Mode sheets (<mode>.uss) hold only :root rules.
                if (sheet.Rules.Count == 0) return false;
                foreach (var rule in sheet.Rules)
                {
                    if (!string.Equals(rule.Selector, ":root", StringComparison.Ordinal)) return false;
                }

                return true;
            }

            private List<string> EnumerateFiles(string pattern)
            {
                var files = new List<string>();
                var pending = new Stack<string>();
                pending.Push(_folder);
                while (pending.Count > 0)
                {
                    var directory = pending.Pop();
                    foreach (var child in Directory.GetDirectories(directory))
                    {
                        if (Path.GetFileName(child).StartsWith(".", StringComparison.Ordinal)) continue;
                        pending.Push(child);
                    }

                    files.AddRange(Directory.GetFiles(directory, pattern));
                }

                // Directory.GetFiles("*.uss") also matches "*.ussx" on some platforms (8.3 rule).
                var extension = pattern.Substring(1);
                files.RemoveAll(f => !f.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
                files.Sort(StringComparer.Ordinal);
                return files;
            }

            private static string FindProjectRoot(string folder)
            {
                var directory = new DirectoryInfo(folder);
                while (directory != null)
                {
                    if (string.Equals(directory.Name, "Assets", StringComparison.Ordinal) && directory.Parent != null)
                    {
                        return directory.Parent.FullName;
                    }

                    directory = directory.Parent;
                }

                return null;
            }

            private string ReadText(string path)
            {
                try
                {
                    return File.ReadAllText(path);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    Add(UssMalformed, FigmaDiagnosticSeverity.Warning, Relative(path), 0, "USS cannot be read: " + exception.Message);
                    return string.Empty;
                }
            }

            private string Relative(string path)
            {
                var full = Path.GetFullPath(path);
                var root = _folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var relative = full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
                return relative.Replace('\\', '/');
            }

            private void Add(string code, FigmaDiagnosticSeverity severity, string file, int line, string message)
            {
                Diagnostics.Add(new FigmaDiagnostic { Code = code, Severity = severity, File = file, Line = line, Message = message });
            }
        }
    }
}
