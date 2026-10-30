namespace Cuvara.UIToolkit.Codegen.Cli
{
    using System;
    using System.Globalization;
    using System.IO;
    using Cuvara.UIToolkit.FigmaCore;

    /// <summary>
    /// <c>validate &lt;importFolder&gt; [--prefix p] [--max-inline N] [--max-absolute N]
    /// [--no-token-colors] [--json]</c>: runs <see cref="FigmaImportValidator"/> (the same
    /// Unity-free core the Editor uses) on one com.unity.ui.figma import folder.
    /// Exit 0 = no errors, 1 = errors, 2 = usage or IO problem.
    /// </summary>
    internal static class FigmaValidateCommand
    {
        public const string Verb = "validate";

        private const string Usage =
            "usage: UxmlCodegenCli validate <importFolder> [--prefix <uss-class-prefix>] [--max-inline <N>] [--max-absolute <N>] [--no-token-colors] [--json]";

        public static int Run(string[] args)
        {
            string folder = null;
            var json = false;
            var options = new FigmaValidationOptions();

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "--json":
                        json = true;
                        break;
                    case "--no-token-colors":
                        options.RequireTokenForColors = false;
                        break;
                    case "--prefix":
                        if (!TryValue(args, ref i, out options.ExpectedClassPrefix)) return UsageError("--prefix needs a value");
                        options.ExpectedClassPrefix = options.ExpectedClassPrefix.TrimEnd('-');
                        break;
                    case "--max-inline":
                        if (!TryCount(args, ref i, out options.MaxInlineStyledElements)) return UsageError("--max-inline needs a non-negative integer");
                        break;
                    case "--max-absolute":
                        if (!TryCount(args, ref i, out options.MaxAbsoluteElements)) return UsageError("--max-absolute needs a non-negative integer");
                        break;
                    case "-h":
                    case "--help":
                        Console.WriteLine(Usage);
                        return 0;
                    default:
                        if (arg.StartsWith("--", StringComparison.Ordinal)) return UsageError($"unknown option '{arg}'");
                        if (folder != null) return UsageError($"only one import folder is accepted (got '{folder}' and '{arg}')");
                        folder = arg;
                        break;
                }
            }

            if (folder == null) return UsageError("missing <importFolder>");
            if (!Directory.Exists(folder))
            {
                Console.Error.WriteLine($"error: import folder '{folder}' does not exist");
                return 2;
            }

            var diagnostics = FigmaImportValidator.ValidateFolder(folder, options);
            var errors = FigmaDiagnostics.Count(diagnostics, FigmaDiagnosticSeverity.Error);

            if (json)
            {
                Console.WriteLine(FigmaDiagnostics.ToJson(folder, diagnostics));
            }
            else
            {
                foreach (var diagnostic in diagnostics) Console.WriteLine(diagnostic.ToString());
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "figma validate: {0}: {1} error(s), {2} warning(s), {3} info",
                    folder,
                    errors,
                    FigmaDiagnostics.Count(diagnostics, FigmaDiagnosticSeverity.Warning),
                    FigmaDiagnostics.Count(diagnostics, FigmaDiagnosticSeverity.Info)));
            }

            return errors > 0 ? 1 : 0;
        }

        private static bool TryValue(string[] args, ref int i, out string value)
        {
            value = null;
            if (i + 1 >= args.Length) return false;
            value = args[++i];
            return true;
        }

        private static bool TryCount(string[] args, ref int i, out int value)
        {
            value = -1;
            return TryValue(args, ref i, out var text)
                && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        private static int UsageError(string message)
        {
            Console.Error.WriteLine("error: " + message);
            Console.Error.WriteLine(Usage);
            return 2;
        }
    }
}
