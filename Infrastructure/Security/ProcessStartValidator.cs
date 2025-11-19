using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ToNRoundCounter.Infrastructure.Security
{
    /// <summary>
    /// Provides security validation for Process.Start operations.
    /// </summary>
    public static class ProcessStartValidator
    {
        private static readonly string[] AllowedExecutableExtensions = { ".exe", ".bat", ".cmd", ".com" };

        /// <summary>
        /// Validates that an executable path is safe to launch.
        /// </summary>
        /// <param name="filePath">The path to the executable.</param>
        /// <param name="errorMessage">Output parameter containing the error message if validation fails.</param>
        /// <returns>True if the path is safe, false otherwise.</returns>
        public static bool IsExecutablePathSafe(string filePath, out string? errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                errorMessage = "File path cannot be null or empty.";
                return false;
            }

            try
            {
                // Get full path to resolve any relative paths
                var fullPath = Path.GetFullPath(filePath);

                // Check if file exists
                if (!File.Exists(fullPath))
                {
                    errorMessage = $"File does not exist: {fullPath}";
                    return false;
                }

                // Check for valid executable extension
                var extension = Path.GetExtension(fullPath).ToLowerInvariant();
                if (!AllowedExecutableExtensions.Contains(extension))
                {
                    errorMessage = $"File extension '{extension}' is not allowed. Allowed extensions: {string.Join(", ", AllowedExecutableExtensions)}";
                    return false;
                }

                // Warn if trying to launch from Windows system directories (but don't block)
                var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System).ToLowerInvariant();
                var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
                var lowerPath = fullPath.ToLowerInvariant();

                if (lowerPath.StartsWith(systemDir) || lowerPath.StartsWith(windowsDir))
                {
                    Debug.WriteLine($"WARNING: Launching executable from system directory: {fullPath}");
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Path validation failed: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Validates that a URL is safe to open.
        /// </summary>
        /// <param name="url">The URL to validate.</param>
        /// <param name="errorMessage">Output parameter containing the error message if validation fails.</param>
        /// <returns>True if the URL is safe, false otherwise.</returns>
        public static bool IsUrlSafe(string url, out string? errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(url))
            {
                errorMessage = "URL cannot be null or empty.";
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                errorMessage = "Invalid URL format.";
                return false;
            }

            // Only allow HTTP(S) schemes for URLs
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                errorMessage = $"URL scheme '{uri.Scheme}' is not allowed. Only HTTP and HTTPS are permitted.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates that a file/folder path is safe to open in Explorer.
        /// </summary>
        /// <param name="path">The path to validate.</param>
        /// <param name="errorMessage">Output parameter containing the error message if validation fails.</param>
        /// <returns>True if the path is safe, false otherwise.</returns>
        public static bool IsFileSystemPathSafe(string path, out string? errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                errorMessage = "Path cannot be null or empty.";
                return false;
            }

            try
            {
                // Get full path to resolve any relative paths
                var fullPath = Path.GetFullPath(path);

                // Check for directory traversal attacks
                if (path.Contains("..") && !fullPath.StartsWith(AppDomain.CurrentDomain.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = "Path contains directory traversal sequences outside application directory.";
                    return false;
                }

                // Path must exist as either file or directory
                if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                {
                    // Allow parent directory if file doesn't exist
                    var directory = Path.GetDirectoryName(fullPath);
                    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                    {
                        errorMessage = $"Path does not exist: {fullPath}";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Path validation failed: {ex.Message}";
                return false;
            }
        }
    }
}
