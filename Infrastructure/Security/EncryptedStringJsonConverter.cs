using System;
using Newtonsoft.Json;

namespace ToNRoundCounter.Infrastructure.Security
{
    /// <summary>
    /// JSON converter that automatically encrypts strings when serializing and decrypts when deserializing.
    /// </summary>
    public class EncryptedStringJsonConverter : JsonConverter<string>
    {
        private readonly ISecureSettingsEncryption _encryption;
        private readonly bool _allowPlainTextFallback;

        public EncryptedStringJsonConverter(ISecureSettingsEncryption encryption, bool allowPlainTextFallback = true)
        {
            _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
            _allowPlainTextFallback = allowPlainTextFallback;
        }

        public override void WriteJson(JsonWriter writer, string? value, JsonSerializer serializer)
        {
            // Preserve null vs empty distinction
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            if (string.IsNullOrEmpty(value))
            {
                writer.WriteValue(string.Empty);
                return;
            }

            try
            {
                var encrypted = _encryption.Encrypt(value);
                writer.WriteValue(encrypted);
            }
            catch (InvalidOperationException)
            {
                // Expected encryption failure (e.g., DPAPI not available)
                if (_allowPlainTextFallback)
                {
                    // Write plaintext when fallback is enabled
                    writer.WriteValue(value);
                }
                else
                {
                    throw;
                }
            }
            // Let other unexpected exceptions bubble up
        }

        public override string? ReadJson(JsonReader reader, Type objectType, string? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            // Preserve null vs empty distinction
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            var value = reader.Value?.ToString();
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            // Try to decrypt
            try
            {
                return _encryption.Decrypt(value);
            }
            catch (InvalidOperationException)
            {
                // Expected decryption failure (e.g., plain text, different machine/user)
                if (_allowPlainTextFallback)
                {
                    // Assume plain text (for migration from unencrypted settings)
                    return value;
                }
                else
                {
                    throw;
                }
            }
            // Let other unexpected exceptions bubble up
        }
    }
}
