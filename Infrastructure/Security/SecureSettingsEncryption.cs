using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ToNRoundCounter.Infrastructure.Security
{
    /// <summary>
    /// Provides encryption and decryption for sensitive settings using DPAPI (Data Protection API).
    /// This uses Windows DPAPI to encrypt data using machine and user-specific keys.
    /// </summary>
    public class SecureSettingsEncryption : ISecureSettingsEncryption
    {
        private readonly byte[] _entropy;

        public SecureSettingsEncryption()
        {
            // Generate entropy from machine-specific information
            // This provides security without storing secrets in source code
            _entropy = GenerateEntropyFromMachineInfo();
        }

        private static byte[] GenerateEntropyFromMachineInfo()
        {
            // Combine multiple machine-specific values for entropy
            var machineId = Environment.MachineName;
            var userId = Environment.UserName;
            var appName = "ToNRoundCounter";
            var version = "v2"; // Change this if entropy generation changes

            var entropySource = $"{appName}:{version}:{machineId}:{userId}";
            return Encoding.UTF8.GetBytes(entropySource);
        }

        /// <summary>
        /// Encrypts a plain text string using DPAPI.
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
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encryptedBytes = ProtectedData.Protect(
                    plainBytes,
                    _entropy,
                    DataProtectionScope.CurrentUser
                );
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException("Failed to encrypt data. Ensure you are running on Windows with DPAPI support.", ex);
            }
        }

        /// <summary>
        /// Decrypts an encrypted string using DPAPI.
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
                byte[] decryptedBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    _entropy,
                    DataProtectionScope.CurrentUser
                );
                return Encoding.UTF8.GetString(decryptedBytes);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("Failed to decrypt data. The encrypted text is not valid Base64.", ex);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException("Failed to decrypt data. The data may have been encrypted on a different machine or user account.", ex);
            }
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
