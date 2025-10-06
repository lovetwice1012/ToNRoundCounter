using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ToNRoundCounter.Infrastructure
{
    public static class LanguageAssemblyResolver
    {
        private static readonly object Sync = new object();
        private static bool _initialized;

        public static void Initialize()
        {
            lock (Sync)
            {
                if (_initialized)
                {
                    return;
                }

                var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var languageDirectory = Path.Combine(baseDirectory, "lang");
                Directory.CreateDirectory(languageDirectory);

                MoveSatelliteAssemblies(baseDirectory, languageDirectory);

                AppDomain.CurrentDomain.AssemblyResolve += HandleAssemblyResolve;
                _initialized = true;
            }
        }

        private static void MoveSatelliteAssemblies(string baseDirectory, string languageDirectory)
        {
            IEnumerable<string> candidateDirectories;

            try
            {
                candidateDirectories = Directory.EnumerateDirectories(baseDirectory)
                    .Where(dir => !string.Equals(Path.GetFileName(dir), "lang", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            foreach (var directory in candidateDirectories)
            {
                bool containsResource;

                try
                {
                    containsResource = Directory.EnumerateFiles(directory, "*.resources.dll", SearchOption.TopDirectoryOnly).Any();
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                if (!containsResource)
                {
                    continue;
                }

                var targetDirectory = Path.Combine(languageDirectory, Path.GetFileName(directory));
                Directory.CreateDirectory(targetDirectory);

                MoveResourceFiles(directory, targetDirectory);
                TryDeleteDirectory(directory);
            }
        }

        private static void MoveResourceFiles(string sourceDirectory, string targetDirectory)
        {
            string[] resourceFiles;

            try
            {
                resourceFiles = Directory.GetFiles(sourceDirectory, "*.resources.dll", SearchOption.TopDirectoryOnly);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            foreach (var sourceFile in resourceFiles)
            {
                var destinationFile = Path.Combine(targetDirectory, Path.GetFileName(sourceFile));

                if (string.Equals(sourceFile, destinationFile, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (File.Exists(destinationFile))
                    {
                        if (!FilesAreIdentical(sourceFile, destinationFile))
                        {
                            File.Delete(destinationFile);
                        }
                        else
                        {
                            File.Delete(sourceFile);
                            continue;
                        }
                    }

                    File.Move(sourceFile, destinationFile);
                }
                catch (IOException)
                {
                    // Ignore failures and continue processing remaining files.
                }
                catch (UnauthorizedAccessException)
                {
                    // Ignore failures and continue processing remaining files.
                }
            }
        }

        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (IOException)
            {
                // Ignore when the directory cannot be removed.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore when the directory cannot be removed.
            }
        }

        private static bool FilesAreIdentical(string firstFile, string secondFile)
        {
            try
            {
                var firstInfo = new FileInfo(firstFile);
                var secondInfo = new FileInfo(secondFile);

                if (firstInfo.Length != secondInfo.Length)
                {
                    return false;
                }

                if (firstInfo.LastWriteTimeUtc != secondInfo.LastWriteTimeUtc)
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            return true;
        }

        private static Assembly? HandleAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            var requestedAssembly = new AssemblyName(args.Name);

            if (!requestedAssembly.Name?.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) ?? true)
            {
                return null;
            }

            var cultureName = requestedAssembly.CultureInfo?.Name;

            if (string.IsNullOrEmpty(cultureName))
            {
                return null;
            }

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var languageDirectory = Path.Combine(baseDirectory, "lang");
            var candidates = BuildCandidateCultures(cultureName);

            foreach (var candidate in candidates)
            {
                var resourcePath = Path.Combine(languageDirectory, candidate, requestedAssembly.Name + ".dll");

                if (!File.Exists(resourcePath))
                {
                    continue;
                }

                try
                {
                    return Assembly.LoadFrom(resourcePath);
                }
                catch (IOException)
                {
                    // Ignore and continue to next candidate.
                }
                catch (UnauthorizedAccessException)
                {
                    // Ignore and continue to next candidate.
                }
            }

            return null;
        }

        private static IEnumerable<string> BuildCandidateCultures(string cultureName)
        {
            var candidates = new LinkedList<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void TryAdd(string candidate)
            {
                if (string.IsNullOrEmpty(candidate) || !seen.Add(candidate))
                {
                    return;
                }

                candidates.AddLast(candidate);
            }

            TryAdd(cultureName);

            var normalized = LanguageManager.NormalizeCulture(cultureName);
            TryAdd(normalized);

            try
            {
                var culture = new CultureInfo(cultureName);

                while (!string.IsNullOrEmpty(culture.Name))
                {
                    TryAdd(culture.Name);
                    culture = culture.Parent;
                }
            }
            catch (CultureNotFoundException)
            {
                // Ignore invalid culture values.
            }

            return candidates;
        }
    }
}
