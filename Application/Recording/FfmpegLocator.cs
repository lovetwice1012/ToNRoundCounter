#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ToNRoundCounter.Application.Recording
{
    /// <summary>
    /// Locates or auto-downloads a local copy of ffmpeg.exe used by
    /// <see cref="AudioMuxingMediaWriter"/> to mux Media Foundation's MP4 video output with the
    /// PCM WAV audio captured by WasapiAudioCapture.
    ///
    /// The binary is cached under %LOCALAPPDATA%\ToNRoundCounter\ffmpeg\ so redistributing
    /// the application itself does not include any third-party executable. The download URL
    /// points at the official BtbN LGPL build which can be bundled/downloaded freely.
    /// </summary>
    internal static class FfmpegLocator
    {
        private const string DownloadUrl =
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl-shared.zip";

        private static readonly object Sync = new object();
        private static string? _cachedPath;
        private static Task? _backgroundDownload;

        public static string CacheDirectory
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(appData, "ToNRoundCounter", "ffmpeg");
            }
        }

        /// <summary>
        /// Returns true if ffmpeg.exe is already available locally (either in PATH or in the
        /// local cache directory). Used by the UI to decide whether a user-visible download
        /// notification is necessary when auto recording is enabled.
        /// </summary>
        public static bool IsAvailable()
        {
            if (_cachedPath != null && File.Exists(_cachedPath))
            {
                return true;
            }

            string? existing = FindInPath() ?? FindInDirectory(CacheDirectory);
            if (existing != null)
            {
                _cachedPath = existing;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Kicks off an asynchronous download of ffmpeg if it is not yet cached. Subsequent
        /// calls return the same task. Intended to be fired when auto recording is enabled in
        /// settings so the first actual recording does not stall on a cold cache download.
        /// </summary>
        public static Task EnsureAvailableInBackground()
        {
            if (IsAvailable())
            {
                return Task.CompletedTask;
            }

            lock (Sync)
            {
                if (_backgroundDownload != null && !_backgroundDownload.IsCompleted)
                {
                    return _backgroundDownload;
                }

                _backgroundDownload = Task.Run(() =>
                {
                    try
                    {
                        Locate();
                    }
                    catch
                    {
                        // Background download errors are swallowed - the next recording attempt
                        // will surface them through the synchronous Locate() call.
                    }
                });
                return _backgroundDownload;
            }
        }

        /// <summary>
        /// Returns a fully qualified path to an ffmpeg.exe on disk. If the cache is empty
        /// this will block while the LGPL zip is downloaded and extracted (typically 30-80 MB).
        /// Throws <see cref="InvalidOperationException"/> if the download or extraction fails.
        /// </summary>
        public static string Locate(CancellationToken cancellationToken = default)
        {
            if (_cachedPath != null && File.Exists(_cachedPath))
            {
                return _cachedPath;
            }

            lock (Sync)
            {
                if (_cachedPath != null && File.Exists(_cachedPath))
                {
                    return _cachedPath;
                }

                // Look first in PATH / app folder so users who already have ffmpeg
                // available do not pay the download cost.
                string? existing = FindInPath();
                if (existing != null)
                {
                    _cachedPath = existing;
                    return existing;
                }

                Directory.CreateDirectory(CacheDirectory);

                // Probe the cache: any ffmpeg.exe under the cache folder wins.
                string? cached = FindInDirectory(CacheDirectory);
                if (cached != null)
                {
                    _cachedPath = cached;
                    return cached;
                }

                // Need to download.
                string zipPath = Path.Combine(CacheDirectory, "ffmpeg-download.zip");
                try
                {
                    DownloadZip(zipPath, cancellationToken);
                    ExtractZip(zipPath, CacheDirectory);
                }
                finally
                {
                    try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                }

                cached = FindInDirectory(CacheDirectory);
                if (cached == null)
                {
                    throw new InvalidOperationException(
                        "ffmpeg.exe was not found in the downloaded archive. Audio muxing cannot proceed.");
                }

                _cachedPath = cached;
                return cached;
            }
        }

        private static string? FindInPath()
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv))
            {
                return null;
            }

            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                {
                    continue;
                }

                try
                {
                    string candidate = Path.Combine(dir, "ffmpeg.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // Malformed PATH entries are ignored.
                }
            }

            return null;
        }

        private static string? FindInDirectory(string rootDir)
        {
            if (!Directory.Exists(rootDir))
            {
                return null;
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(rootDir, "ffmpeg.exe", SearchOption.AllDirectories))
                {
                    return file;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static void DownloadZip(string destination, CancellationToken cancellationToken)
        {
            // Use a long-lived HttpClient with a generous timeout: the LGPL build is ~40 MB and
            // GitHub release downloads can take a while on slow connections.
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ToNRoundCounter/1.0 (+ffmpeg-auto-dl)");

            using var response = client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            using var input = response.Content.ReadAsStreamAsync(cancellationToken).GetAwaiter().GetResult();
            using var output = File.Create(destination);
            input.CopyToAsync(output, 81920, cancellationToken).GetAwaiter().GetResult();
        }

        private static void ExtractZip(string zipPath, string destinationRoot)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                // Only pull ffmpeg.exe (and adjacent DLLs for shared LGPL build) - we do not need
                // ffprobe / ffplay / doc.
                string name = Path.GetFileName(entry.FullName);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                bool wanted = name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase)
                              || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
                if (!wanted)
                {
                    continue;
                }

                string dest = Path.Combine(destinationRoot, name);
                Directory.CreateDirectory(destinationRoot);
                entry.ExtractToFile(dest, overwrite: true);
            }
        }
    }
}
