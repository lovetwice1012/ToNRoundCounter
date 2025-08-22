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
    }
}
