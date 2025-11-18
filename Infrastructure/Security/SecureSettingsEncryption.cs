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
        private const string EncryptedPrefix = "ENC_V1:";
        private readonly byte[] _entropy;

        public SecureSettingsEncryption()
        {
            // Additional entropy for DPAPI encryption.
            // This is not a secret key; DPAPI uses machine and user-specific keys for actual encryption.
            _entropy = Encoding.UTF8.GetBytes("ToNRoundCounter-Entropy-v1");
        }

        /// <summary>
        /// Encrypts a plain text string using DPAPI.
        /// </summary>
        /// <param name="plainText">The text to encrypt. Null or empty values return empty string.</param>
        /// <returns>Prefixed Base64 encoded encrypted string (ENC_V1:...), or empty string if input is null/empty.</returns>
        public string Encrypt(string? plainText)
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
                return EncryptedPrefix + Convert.ToBase64String(encryptedBytes);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException("Failed to encrypt data. Ensure you are running on Windows with DPAPI support.", ex);
            }
        }

        /// <summary>
        /// Decrypts an encrypted string using DPAPI.
        /// </summary>
        /// <param name="encryptedText">Prefixed Base64 encoded encrypted string (ENC_V1:...). Null or empty values return empty string.</param>
        /// <returns>Decrypted plain text, or empty string if input is null/empty.</returns>
        public string Decrypt(string? encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
            {
                return string.Empty;
            }

            try
            {
                // Remove prefix if present
                string base64Data = encryptedText;
                if (encryptedText.StartsWith(EncryptedPrefix))
                {
                    base64Data = encryptedText.Substring(EncryptedPrefix.Length);
                }

                byte[] encryptedBytes = Convert.FromBase64String(base64Data);
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
        /// Checks if a string is encrypted by looking for the encryption prefix.
        /// This is a reliable check (not a heuristic) based on the presence of the version prefix.
        /// </summary>
        public bool IsEncrypted(string? value)
        {
            return !string.IsNullOrEmpty(value) && value.StartsWith(EncryptedPrefix);
        }
    }
}
