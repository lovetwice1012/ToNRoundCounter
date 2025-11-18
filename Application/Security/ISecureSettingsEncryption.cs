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
        /// <param name="plainText">The text to encrypt.</param>
        /// <returns>Encrypted string.</returns>
        string Encrypt(string plainText);

        /// <summary>
        /// Decrypts an encrypted string.
        /// </summary>
        /// <param name="encryptedText">The encrypted text.</param>
        /// <returns>Decrypted plain text.</returns>
        string Decrypt(string encryptedText);

        /// <summary>
        /// Checks if a string appears to be encrypted.
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <returns>True if the value appears to be encrypted.</returns>
        bool IsEncrypted(string? value);
    }
}
