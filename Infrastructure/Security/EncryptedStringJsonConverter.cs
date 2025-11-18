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
            if (string.IsNullOrEmpty(value))
            {
                writer.WriteNull();
                return;
            }

            try
            {
                var encrypted = _encryption.Encrypt(value);
                writer.WriteValue(encrypted);
            }
            catch (Exception)
            {
                // If encryption fails, write empty or throw based on configuration
                if (_allowPlainTextFallback)
                {
                    writer.WriteValue(string.Empty);
                }
                else
                {
                    throw;
                }
            }
        }

        public override string? ReadJson(JsonReader reader, Type objectType, string? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return string.Empty;
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
            catch
            {
                // If decryption fails and fallback is allowed, assume it's plain text (for migration)
                if (_allowPlainTextFallback)
                {
                    return value;
                }
                else
                {
                    return string.Empty;
                }
            }
        }
    }
}
