using System;
using System.Collections.Generic;
using System.IO;

namespace ToNRoundCounter.Application
{
    public sealed class RoundLogExportOptions
    {
        private RoundLogExportOptions(string dataDirectory, string outputPath)
        {
            DataDirectory = dataDirectory;
            OutputPath = outputPath;
        }

        public string DataDirectory { get; }
        public string OutputPath { get; }

        public static RoundLogExportOptions FromPaths(string dataDirectory, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                throw new ArgumentException("Data directory is required.", nameof(dataDirectory));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Output path is required.", nameof(outputPath));
            }

            var resolvedDataDirectory = Path.GetFullPath(dataDirectory);
            var resolvedOutputPath = Path.GetFullPath(outputPath);
            return new RoundLogExportOptions(resolvedDataDirectory, resolvedOutputPath);
        }

        public static bool TryCreate(IReadOnlyList<string> args, out RoundLogExportOptions? options, out string? error)
        {
            options = null;
            error = null;

            if (args == null || args.Count == 0)
            {
                return false;
            }

            bool exportRequested = false;
            string? output = null;
            string? dataDirectory = null;

            for (int i = 0; i < args.Count; i++)
            {
                var current = args[i];
                if (string.Equals(current, "--export-round-logs", StringComparison.OrdinalIgnoreCase))
                {
                    exportRequested = true;
                }
                else if (string.Equals(current, "--output", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Count)
                    {
                        error = "Missing value for --output option.";
                        return true;
                    }

                    output = args[++i];
                }
                else if (string.Equals(current, "--data-dir", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Count)
                    {
                        error = "Missing value for --data-dir option.";
                        return true;
                    }

                    dataDirectory = args[++i];
                }
            }

            if (!exportRequested)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                string fileName = $"roundLogs-export-{DateTime.Now:yyyyMMdd_HHmmss}.json";
                output = Path.Combine(Directory.GetCurrentDirectory(), fileName);
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                dataDirectory = Path.Combine(baseDirectory, "data");
            }

            try
            {
                var resolvedOutput = Path.GetFullPath(output);
                var resolvedData = Path.GetFullPath(dataDirectory);

                options = new RoundLogExportOptions(resolvedData, resolvedOutput);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to resolve export options: {ex.Message}";
                return true;
            }
        }
    }
}

