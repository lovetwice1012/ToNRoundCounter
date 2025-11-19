using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Updater
{
    internal static class Program
    {
        static async Task<int> Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: Updater <zipPath> <targetExe>");
                return 1;
            }

            string zipPath = args[0];
            string targetExe = args[1];
            string targetDir = Path.GetDirectoryName(targetExe) ?? ".";

            // wait for the target file to be unlocked (async with cancellation support)
            for (int i = 0; i < 30 && IsFileLocked(targetExe); i++)
            {
                await Task.Delay(1000);
            }

            var backupDirectory = Path.Combine(targetDir, $".update-backup-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
            var backedUpEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        // Security: Validate archive entry path to prevent directory traversal attacks
                        if (string.IsNullOrWhiteSpace(entry.FullName))
                        {
                            continue; // Skip invalid entries
                        }

                        // Normalize the entry path and prevent directory traversal
                        var entryPath = entry.FullName.Replace('\\', '/').TrimStart('/');
                        if (entryPath.Contains("..") || Path.IsPathRooted(entryPath))
                        {
                            Console.WriteLine($"Skipping potentially malicious entry: {entry.FullName}");
                            continue;
                        }

                        var destinationPath = Path.Combine(targetDir, entryPath);
                        var fullDestinationPath = Path.GetFullPath(destinationPath);
                        var fullTargetDir = Path.GetFullPath(targetDir);

                        // Ensure the destination is within the target directory
                        if (!fullDestinationPath.StartsWith(fullTargetDir, StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"Blocked directory traversal attempt: {entry.FullName}");
                            continue;
                        }

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(fullDestinationPath);
                        }
                        else
                        {
                            var destinationDirectory = Path.GetDirectoryName(fullDestinationPath);
                            if (!string.IsNullOrEmpty(destinationDirectory))
                            {
                                Directory.CreateDirectory(destinationDirectory);
                            }

                            if (IsAudioEntry(entry) && File.Exists(fullDestinationPath) && HasFileChanged(entry, fullDestinationPath))
                            {
                                Console.WriteLine($"Skipping modified audio file: {entry.FullName}");
                                continue;
                            }

                            BackupExistingFile(fullDestinationPath, entryPath, backupDirectory, backedUpEntries);
                            entry.ExtractToFile(fullDestinationPath, true);
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

            // Security: Validate executable path before starting
            if (!IsExecutablePathSafe(targetExe))
            {
                Console.WriteLine("Target executable path is not safe.");
                return 1;
            }

            try
            {
                Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true });
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start target executable: {ex.Message}");
                return 1;
            }
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

        private static bool IsExecutablePathSafe(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                // Get full path to resolve any relative paths or symlinks
                var fullPath = Path.GetFullPath(path);

                // Check if file exists
                if (!File.Exists(fullPath))
                {
                    return false;
                }

                // Ensure it's an executable file
                var extension = Path.GetExtension(fullPath).ToLowerInvariant();
                if (extension != ".exe" && extension != ".com")
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
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
