namespace Cuvara.UIToolkit.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>One file the Screen Creator will write: a name relative to the output folder, and its text.</summary>
    public sealed class ScreenCreatorFile
    {
        public ScreenCreatorFile(string fileName, string content)
        {
            this.FileName = fileName;
            this.Content  = content;
        }

        public string FileName { get; }

        public string Content { get; }
    }

    /// <summary>What the wizard was asked to generate.</summary>
    public sealed class ScreenCreatorRequest
    {
        public ScreenType   Type;
        public bool         HasModel = true;
        public ScreenSource Source   = ScreenSource.NewUxml;
        public string       Name;
        public string       Namespace;

        /// <summary>Project path of the existing UXML. Required for <see cref="ScreenSource.ExistingUxml"/>.</summary>
        public string ExistingUxmlPath;

        /// <summary>The key the view loads its UXML from. Empty means <see cref="ScreenCreatorTemplates.DefaultAssetKey"/>.</summary>
        public string AssetKey;
    }

    /// <summary>
    /// Decides which files the Screen Creator writes, and their content, before anything touches
    /// the disk.
    /// </summary>
    /// <remarks>
    /// <para>Unity-free like <see cref="ScreenCreatorTemplates"/>, so the decision "an existing
    /// UXML never gets a new .uxml or .uss" is a tested fact rather than a branch inside an
    /// Editor window.</para>
    /// </remarks>
    public static class ScreenCreatorPlan
    {
        /// <summary>The hidden ledger the UI Toolkit Connector for Figma keeps in every import folder.</summary>
        public const string ConnectorManifestFileName = ".figma-manifest.json";

        /// <summary>Builds the file list for a request. Nothing is written.</summary>
        /// <exception cref="ArgumentException">The name is empty, or an existing-UXML request names no <c>.uxml</c>.</exception>
        public static IReadOnlyList<ScreenCreatorFile> Build(ScreenCreatorRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("A screen needs a name.", nameof(request));

            var name     = request.Name.Trim();
            var ns       = string.IsNullOrWhiteSpace(request.Namespace) ? name : request.Namespace.Trim();
            var hasModel = request.HasModel || request.Type == ScreenType.Item;
            var files    = new List<ScreenCreatorFile>();

            if (request.Source == ScreenSource.ExistingUxml)
            {
                var uxmlPath = request.ExistingUxmlPath?.Trim();
                if (string.IsNullOrEmpty(uxmlPath) || !uxmlPath.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Choose an existing .uxml file.", nameof(request));

                var key = string.IsNullOrWhiteSpace(request.AssetKey)
                    ? ScreenCreatorTemplates.DefaultAssetKey(uxmlPath)
                    : request.AssetKey.Trim();

                files.Add(new ScreenCreatorFile(
                    name + "Screen.cs",
                    ScreenCreatorTemplates.Substitute(
                        ScreenCreatorTemplates.SelectScriptTemplate(request.Type, hasModel, ScreenSource.ExistingUxml),
                        ns, name, key, uxmlPath)));
            }
            else if (request.Source == ScreenSource.NewUxml)
            {
                files.Add(new ScreenCreatorFile(
                    name + "Screen.cs",
                    ScreenCreatorTemplates.Substitute(ScreenCreatorTemplates.SelectScriptTemplate(request.Type, hasModel), ns, name)));

                files.Add(new ScreenCreatorFile(
                    name + ".uxml",
                    ScreenCreatorTemplates.Substitute(ScreenCreatorTemplates.SelectUxmlTemplate(request.Type), ns, name)));

                if (request.Type != ScreenType.Item)
                {
                    files.Add(new ScreenCreatorFile(
                        name + ".uss",
                        ScreenCreatorTemplates.Substitute(ScreenCreatorTemplates.SelectUssTemplate(), ns, name)));
                }
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(request), request.Source, "Unknown screen source.");
            }

            files.Add(new ScreenCreatorFile(
                name + "PresenterTests.cs",
                ScreenCreatorTemplates.Substitute(ScreenCreatorTemplates.SelectTestTemplate(), ns, name)));

            return files;
        }

        /// <summary>
        /// True when <paramref name="directory"/> or any parent up to <paramref name="stopAt"/> holds
        /// a connector manifest, i.e. it is inside a Figma import folder. The connector owns that
        /// folder; generated code belongs in a game-owned folder instead.
        /// </summary>
        public static bool IsInsideConnectorImportFolder(string directory, string stopAt)
        {
            if (string.IsNullOrEmpty(directory)) return false;

            var current = new DirectoryInfo(Path.GetFullPath(directory));
            var stop    = string.IsNullOrEmpty(stopAt) ? null : Path.GetFullPath(stopAt).TrimEnd('\\', '/');

            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, ConnectorManifestFileName))) return true;
                if (stop != null && string.Equals(current.FullName.TrimEnd('\\', '/'), stop, StringComparison.OrdinalIgnoreCase)) return false;
                current = current.Parent;
            }

            return false;
        }
    }
}
