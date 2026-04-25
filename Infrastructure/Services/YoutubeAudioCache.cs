using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Events;
using ToNRoundCounter.Application;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace ToNRoundCounter.Infrastructure.Services
{
    /// <summary>
    /// Resolves YouTube URLs to locally cached audio files using YoutubeExplode.
    /// Downloads happen in the background; consumers poll <see cref="TryGetCachedPath"/>
    /// or subscribe to <see cref="CacheUpdated"/> to learn when a URL becomes playable.
    /// </summary>
    public sealed class YoutubeAudioCache
    {
        private static readonly char[] InvalidIdChars = Path.GetInvalidFileNameChars();
        private readonly IEventLogger _logger;
        private readonly string _cacheDirectory;
        private readonly YoutubeClient _client = new YoutubeClient();
        // url -> local file path (when complete)
        private readonly ConcurrentDictionary<string, string> _completed = new(StringComparer.OrdinalIgnoreCase);
        // url -> in-flight download task
        private readonly ConcurrentDictionary<string, Task> _inFlight = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised on the thread pool when a previously requested URL becomes locally playable.</summary>
        public event EventHandler<string>? CacheUpdated;

        public YoutubeAudioCache(IEventLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToNRoundCounter", "yt-cache");
            try
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
            catch (Exception ex)
            {
                _logger.LogEvent("YoutubeAudioCache", $"Failed to create cache directory: {ex.Message}", LogEventLevel.Warning);
            }

            // Pre-populate completed map by scanning existing cache files (videoId.ext format).
            // We can not reverse a videoId back to the original URL, so we register by canonical
            // https://www.youtube.com/watch?v={id} so subsequent identical URLs are served immediately.
            try
            {
                foreach (var file in Directory.EnumerateFiles(_cacheDirectory))
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(fileName)) continue;
                    string canonical = $"https://www.youtube.com/watch?v={fileName}";
                    _completed[canonical] = file;
                }
            }
            catch { /* ignore enumeration errors */ }
        }

        public static bool IsYoutubeUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string v = value.Trim();
            bool isHttp = v.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                       || v.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            if (!isHttp) return false;
            return v.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
                || v.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Normalizes URL variants (music.youtube.com, m.youtube.com, etc.) to a form
        /// YoutubeExplode can reliably parse. Strips query parameters except <c>v</c>, <c>list</c>, <c>t</c>.
        /// </summary>
        private static string NormalizeUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                string host = uri.Host.ToLowerInvariant();
                // youtu.be short links are accepted as-is.
                if (host.EndsWith("youtu.be", StringComparison.Ordinal))
                {
                    return url;
                }
                // Map music./m./gaming. -> www.
                if (host == "music.youtube.com" || host == "m.youtube.com" || host == "gaming.youtube.com" || host == "youtube.com")
                {
                    var builder = new UriBuilder(uri) { Host = "www.youtube.com", Port = -1 };
                    return builder.Uri.ToString();
                }
                return url;
            }
            catch
            {
                return url;
            }
        }

        /// <summary>
        /// Returns true and the local file path when the URL is already cached. Otherwise returns false and
        /// schedules a background download that, on completion, raises <see cref="CacheUpdated"/>.
        /// </summary>
        public bool TryGetCachedPath(string url, out string localPath)
        {
            localPath = string.Empty;
            if (!IsYoutubeUrl(url)) return false;

            if (_completed.TryGetValue(url, out var cached) && File.Exists(cached))
            {
                localPath = cached;
                return true;
            }

            // Fallback: try to derive video ID from the URL and look for a matching file in the cache.
            string? videoId = TryExtractVideoId(url);
            if (!string.IsNullOrEmpty(videoId))
            {
                try
                {
                    var match = Directory.EnumerateFiles(_cacheDirectory, videoId + ".*").FirstOrDefault();
                    if (match != null)
                    {
                        _completed[url] = match;
                        localPath = match;
                        return true;
                    }
                }
                catch { /* ignore */ }
            }

            EnsureDownload(url);
            return false;
        }

        private static string? TryExtractVideoId(string url)
        {
            try
            {
                var uri = new Uri(url);
                string host = uri.Host.ToLowerInvariant();
                if (host.EndsWith("youtu.be", StringComparison.Ordinal))
                {
                    return uri.AbsolutePath.Trim('/');
                }
                var query = uri.Query.TrimStart('?');
                foreach (var kv in query.Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    if (string.Equals(kv.Substring(0, eq), "v", StringComparison.Ordinal))
                    {
                        return Uri.UnescapeDataString(kv.Substring(eq + 1));
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private void EnsureDownload(string url)
        {
            if (_inFlight.ContainsKey(url)) return;
            var task = Task.Run(() => DownloadAsync(url, CancellationToken.None));
            _inFlight[url] = task;
        }

        private async Task DownloadAsync(string url, CancellationToken ct)
        {
            try
            {
                string requestUrl = NormalizeUrl(url);
                if (!string.Equals(requestUrl, url, StringComparison.Ordinal))
                {
                    _logger.LogEvent("YoutubeAudioCache", $"Normalized URL {url} -> {requestUrl}", LogEventLevel.Information);
                }
                _logger.LogEvent("YoutubeAudioCache", $"Resolving stream for {requestUrl}", LogEventLevel.Information);
                var streamManifest = await _client.Videos.Streams.GetManifestAsync(requestUrl, ct).ConfigureAwait(false);
                var audioStream = streamManifest.GetAudioOnlyStreams()
                    .OrderByDescending(s => s.Bitrate.BitsPerSecond)
                    .FirstOrDefault();
                if (audioStream == null)
                {
                    _logger.LogEvent("YoutubeAudioCache", $"No audio-only stream found for {requestUrl}", LogEventLevel.Warning);
                    return;
                }

                var video = await _client.Videos.GetAsync(requestUrl, ct).ConfigureAwait(false);
                string videoId = SanitizeFileName(video.Id.Value);
                string ext = string.IsNullOrWhiteSpace(audioStream.Container.Name) ? "m4a" : audioStream.Container.Name;
                string targetPath = Path.Combine(_cacheDirectory, $"{videoId}.{ext}");

                if (!File.Exists(targetPath))
                {
                    string tmpPath = targetPath + ".part";
                    await _client.Videos.Streams.DownloadAsync(audioStream, tmpPath, null, ct).ConfigureAwait(false);
                    if (File.Exists(targetPath))
                    {
                        try { File.Delete(tmpPath); } catch { /* ignore */ }
                    }
                    else
                    {
                        File.Move(tmpPath, targetPath);
                    }
                }

                _completed[url] = targetPath;
                _logger.LogEvent("YoutubeAudioCache", $"Cached {url} -> {targetPath}", LogEventLevel.Information);
                try { CacheUpdated?.Invoke(this, url); } catch { /* ignore handler exceptions */ }
            }
            catch (Exception ex)
            {
                _logger.LogEvent("YoutubeAudioCache", $"Download failed for {url}: {ex.Message}", LogEventLevel.Error);
            }
            finally
            {
                _inFlight.TryRemove(url, out _);
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "video";
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(InvalidIdChars, chars[i]) >= 0) chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
