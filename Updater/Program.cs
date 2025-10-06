using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace Updater
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: Updater <zipPath> <targetExe>");
                return 1;
            }

            string zipPath = args[0];
            string targetExe = args[1];
            string targetDir = Path.GetDirectoryName(targetExe) ?? ".";

            // wait for the target file to be unlocked
            for (int i = 0; i < 30 && IsFileLocked(targetExe); i++)
            {
                Thread.Sleep(1000);
            }

            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        var destinationPath = Path.Combine(targetDir, entry.FullName);
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destinationPath);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

                            if (IsAudioEntry(entry) && File.Exists(destinationPath) && HasFileChanged(entry, destinationPath))
                            {
                                Console.WriteLine($"Skipping modified audio file: {entry.FullName}");
                                continue;
                            }

                            entry.ExtractToFile(destinationPath, true);
                        }
                    }
                }
                File.Delete(zipPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Update failed: " + ex.Message);
                return 1;
            }

            Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true });
            return 0;
        }

        private static bool IsFileLocked(string path)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return false;
                }
            }
            catch
            {
                return true;
            }
        }

        private static bool IsAudioEntry(ZipArchiveEntry entry)
        {
            var normalizedPath = entry.FullName.Replace('\\', '/');
            return normalizedPath.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasFileChanged(ZipArchiveEntry entry, string destinationPath)
        {
            try
            {
                using var entryStream = entry.Open();
                using var destinationStream = File.OpenRead(destinationPath);

                if (entry.Length != destinationStream.Length)
                {
                    return true;
                }

                const int bufferSize = 81920;
                var entryBuffer = new byte[bufferSize];
                var destinationBuffer = new byte[bufferSize];

                int entryRead;
                while ((entryRead = entryStream.Read(entryBuffer, 0, bufferSize)) > 0)
                {
                    var destinationRead = destinationStream.Read(destinationBuffer, 0, bufferSize);
                    if (entryRead != destinationRead)
                    {
                        return true;
                    }

                    for (int i = 0; i < entryRead; i++)
                    {
                        if (entryBuffer[i] != destinationBuffer[i])
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
