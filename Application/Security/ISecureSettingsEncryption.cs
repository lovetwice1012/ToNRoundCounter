namespace ToNRoundCounter.Infrastructure.Security
{
    /// <summary>
    /// Interface for encrypting and decrypting sensitive settings.
    /// </summary>
    public interface ISecureSettingsEncryption
    {
        /// <summary>
        /// Encrypts a plain text string.
        /// </summary>
        /// <param name="plainText">The text to encrypt. Null or empty values return empty string.</param>
        /// <returns>Encrypted string with version prefix (e.g., "ENC_V1:..."), or empty string if input is null/empty.</returns>
        string Encrypt(string? plainText);

        /// <summary>
        /// Decrypts an encrypted string.
        /// </summary>
        /// <param name="encryptedText">The encrypted text (with or without version prefix). Null or empty values return empty string.</param>
        /// <returns>Decrypted plain text, or empty string if input is null/empty.</returns>
        string Decrypt(string? encryptedText);

        /// <summary>
        /// Checks if a string appears to be encrypted.
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <returns>True if the value appears to be encrypted.</returns>
        bool IsEncrypted(string? value);
    }
}
