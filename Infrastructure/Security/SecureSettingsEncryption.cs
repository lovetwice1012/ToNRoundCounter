using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace ToNRoundCounter.Infrastructure.Security
{
    /// <summary>
    /// Provides cross-platform encryption and decryption for sensitive settings.
    /// Uses ASP.NET Core Data Protection API which:
    /// - On Windows: Uses DPAPI with user-specific keys
    /// - On Linux/macOS: Uses file-based key storage with appropriate permissions
    /// This provides consistent security across all platforms.
    /// </summary>
    public class SecureSettingsEncryption : ISecureSettingsEncryption
    {
        private readonly IDataProtector _protector;
        private readonly byte[] _legacyEntropy; // For migrating old DPAPI-encrypted data
        private readonly bool _isWindows;

        public SecureSettingsEncryption()
        {
            // Determine platform
            _isWindows = OperatingSystem.IsWindows();

            // Set up key storage directory in user's AppData
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var keyDirectory = Path.Combine(appDataPath, "ToNRoundCounter", "DataProtectionKeys");
            Directory.CreateDirectory(keyDirectory);

            // Create Data Protection provider with application-specific name
            var provider = DataProtectionProvider.Create(
                new DirectoryInfo(keyDirectory),
                configuration =>
                {
                    configuration.SetApplicationName("ToNRoundCounter");
                }
            );

            // Create protector with purpose string (acts like a salt)
            // Version suffix allows key rotation if needed
            _protector = provider.CreateProtector("ToNRoundCounter.Settings.v1");

            // Keep legacy entropy for backward compatibility on Windows
            _legacyEntropy = GenerateEntropyFromMachineInfo();
        }

        private static byte[] GenerateEntropyFromMachineInfo()
        {
            // Combine multiple machine-specific values for entropy
            var machineId = Environment.MachineName;
            var userId = Environment.UserName;
            var appName = "ToNRoundCounter";
            var version = "v2";

            var entropySource = $"{appName}:{version}:{machineId}:{userId}";
            return Encoding.UTF8.GetBytes(entropySource);
        }

        /// <summary>
        /// Encrypts a plain text string using Data Protection API.
        /// </summary>
        /// <param name="plainText">The text to encrypt.</param>
        /// <returns>Base64 encoded encrypted string, or empty string if input is null/empty.</returns>
        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return string.Empty;
            }

            try
            {
                // Use Data Protection API (cross-platform)
                var encryptedBytes = _protector.Protect(Encoding.UTF8.GetBytes(plainText));
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException("Failed to encrypt data. Ensure the application has proper file system permissions.", ex);
            }
        }

        /// <summary>
        /// Decrypts an encrypted string using Data Protection API.
        /// Supports automatic migration from legacy Windows DPAPI encryption.
        /// </summary>
        /// <param name="encryptedText">Base64 encoded encrypted string.</param>
        /// <returns>Decrypted plain text, or empty string if input is null/empty.</returns>
        public string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
            {
                return string.Empty;
            }

            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(encryptedText);

                // Try new Data Protection API first
                try
                {
                    var decryptedBytes = _protector.Unprotect(encryptedBytes);
                    return Encoding.UTF8.GetString(decryptedBytes);
                }
                catch (CryptographicException) when (_isWindows)
                {
                    // On Windows, attempt to decrypt using legacy DPAPI for backward compatibility
                    // This allows migration from old encryption method
                    try
                    {
                        var decryptedBytes = ProtectedData.Unprotect(
                            encryptedBytes,
                            _legacyEntropy,
                            DataProtectionScope.CurrentUser
                        );
                        var plainText = Encoding.UTF8.GetString(decryptedBytes);

                        // Successfully decrypted legacy data - could re-encrypt with new method here
                        // For now, just return the decrypted value
                        // Note: Next save will use new encryption method automatically
                        return plainText;
                    }
                    catch
                    {
                        // Both methods failed, rethrow original exception
                        throw;
                    }
                }
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("Failed to decrypt data. The encrypted text is not valid Base64.", ex);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException(
                    "Failed to decrypt data. The data may have been encrypted on a different machine or user account, " +
                    "or the encryption keys may have been lost.", ex);
            }

            // Unreachable, but required for compilation
            return string.Empty;
        }

        /// <summary>
        /// Checks if a string appears to be encrypted (Base64 format).
        /// </summary>
        public bool IsEncrypted(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            try
            {
                // Check if it's valid Base64
                Convert.FromBase64String(value);
                // Additional heuristic: encrypted strings are typically longer than plain text
                return value.Length > 20;
            }
            catch (FormatException)
            {
                // Not valid Base64, so not encrypted
                return false;
            }
        }
    }
}
