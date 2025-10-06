using System;
using System.Collections.Generic;
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

            var backupDirectory = Path.Combine(targetDir, $".update-backup-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
            var backedUpEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                            var destinationDirectory = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrEmpty(destinationDirectory))
                            {
                                Directory.CreateDirectory(destinationDirectory);
                            }

                            if (IsAudioEntry(entry) && File.Exists(destinationPath) && HasFileChanged(entry, destinationPath))
                            {
                                Console.WriteLine($"Skipping modified audio file: {entry.FullName}");
                                continue;
                            }

                            BackupExistingFile(destinationPath, entry.FullName, backupDirectory, backedUpEntries);
                            entry.ExtractToFile(destinationPath, true);
                        }
                    }
                }

                File.Delete(zipPath);

                if (Directory.Exists(backupDirectory))
                {
                    Directory.Delete(backupDirectory, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Update failed: " + ex.Message);
                TryRollback(targetDir, backupDirectory, backedUpEntries);
                return 1;
            }

            Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true });
            return 0;
        }

        private static void BackupExistingFile(string destinationPath, string relativeEntryPath, string backupDirectory, HashSet<string> backedUpEntries)
        {
            if (!File.Exists(destinationPath))
            {
                return;
            }

            if (backedUpEntries.Contains(relativeEntryPath))
            {
                return;
            }

            var backupPath = Path.Combine(backupDirectory, relativeEntryPath);
            var backupDir = Path.GetDirectoryName(backupPath);

            if (!string.IsNullOrEmpty(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }

            Directory.CreateDirectory(backupDirectory);
            File.Copy(destinationPath, backupPath, true);
            backedUpEntries.Add(relativeEntryPath);
        }

        private static void TryRollback(string targetDir, string backupDirectory, HashSet<string> backedUpEntries)
        {
            if (!Directory.Exists(backupDirectory))
            {
                return;
            }

            try
            {
                foreach (var relativePath in backedUpEntries)
                {
                    var backupPath = Path.Combine(backupDirectory, relativePath);
                    var restorePath = Path.Combine(targetDir, relativePath);
                    var restoreDir = Path.GetDirectoryName(restorePath);

                    if (!string.IsNullOrEmpty(restoreDir))
                    {
                        Directory.CreateDirectory(restoreDir);
                    }

                    if (File.Exists(backupPath))
                    {
                        File.Copy(backupPath, restorePath, true);
                    }
                }
            }
            catch (Exception rollbackEx)
            {
                Console.WriteLine("Rollback failed: " + rollbackEx.Message);
            }
            finally
            {
                try
                {
                    Directory.Delete(backupDirectory, true);
                }
                catch
                {
                    // ignore cleanup errors
                }
            }
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
