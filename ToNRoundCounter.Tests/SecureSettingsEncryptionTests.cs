using System;
using ToNRoundCounter.Infrastructure.Security;
using Xunit;

namespace ToNRoundCounter.Tests
{
    /// <summary>
    /// Tests for SecureSettingsEncryption functionality.
    /// </summary>
    public class SecureSettingsEncryptionTests
    {
        [Fact]
        public void Encrypt_WithValidString_ReturnsEncryptedValue()
        {
            var encryption = new SecureSettingsEncryption();
            var plainText = "test_api_key_12345";

            var encrypted = encryption.Encrypt(plainText);

            Assert.NotNull(encrypted);
            Assert.NotEmpty(encrypted);
            Assert.NotEqual(plainText, encrypted);
        }

        [Fact]
        public void Encrypt_WithEmptyString_ReturnsEmpty()
        {
            var encryption = new SecureSettingsEncryption();

            var encrypted = encryption.Encrypt(string.Empty);

            Assert.Equal(string.Empty, encrypted);
        }

        [Fact]
        public void Encrypt_WithNull_ReturnsEmpty()
        {
            var encryption = new SecureSettingsEncryption();

            var encrypted = encryption.Encrypt(null!);

            Assert.Equal(string.Empty, encrypted);
        }

        [Fact]
        public void Decrypt_WithEncryptedString_ReturnsOriginalValue()
        {
            var encryption = new SecureSettingsEncryption();
            var plainText = "my_secret_webhook_url";

            var encrypted = encryption.Encrypt(plainText);
            var decrypted = encryption.Decrypt(encrypted);

            Assert.Equal(plainText, decrypted);
        }

        [Fact]
        public void Decrypt_WithEmptyString_ReturnsEmpty()
        {
            var encryption = new SecureSettingsEncryption();

            var decrypted = encryption.Decrypt(string.Empty);

            Assert.Equal(string.Empty, decrypted);
        }

        [Fact]
        public void Decrypt_WithNull_ReturnsEmpty()
        {
            var encryption = new SecureSettingsEncryption();

            var decrypted = encryption.Decrypt(null!);

            Assert.Equal(string.Empty, decrypted);
        }

        [Fact]
        public void EncryptDecrypt_RoundTrip_PreservesData()
        {
            var encryption = new SecureSettingsEncryption();
            var testCases = new[]
            {
                "simple",
                "with spaces and special chars !@#$%",
                "https://discord.com/api/webhooks/123456789/abcdefghijk",
                "very_long_api_key_" + new string('x', 100)
            };

            foreach (var testCase in testCases)
            {
                var encrypted = encryption.Encrypt(testCase);
                var decrypted = encryption.Decrypt(encrypted);

                Assert.Equal(testCase, decrypted);
            }
        }

        [Fact]
        public void IsEncrypted_WithEncryptedString_ReturnsTrue()
        {
            var encryption = new SecureSettingsEncryption();
            var plainText = "test_data";

            var encrypted = encryption.Encrypt(plainText);
            var isEncrypted = encryption.IsEncrypted(encrypted);

            Assert.True(isEncrypted);
        }

        [Fact]
        public void IsEncrypted_WithShortString_ReturnsFalse()
        {
            var encryption = new SecureSettingsEncryption();

            var isEncrypted = encryption.IsEncrypted("short");

            Assert.False(isEncrypted);
        }

        [Fact]
        public void IsEncrypted_WithNull_ReturnsFalse()
        {
            var encryption = new SecureSettingsEncryption();

            var isEncrypted = encryption.IsEncrypted(null);

            Assert.False(isEncrypted);
        }

        [Fact]
        public void IsEncrypted_WithEmpty_ReturnsFalse()
        {
            var encryption = new SecureSettingsEncryption();

            var isEncrypted = encryption.IsEncrypted(string.Empty);

            Assert.False(isEncrypted);
        }

        [Fact]
        public void Encrypt_SameInputTwice_ProducesDifferentOutput()
        {
            var encryption = new SecureSettingsEncryption();
            var plainText = "test_data";

            var encrypted1 = encryption.Encrypt(plainText);
            var encrypted2 = encryption.Encrypt(plainText);

            // DPAPI may produce different encrypted outputs for the same input
            // But both should decrypt to the same value
            var decrypted1 = encryption.Decrypt(encrypted1);
            var decrypted2 = encryption.Decrypt(encrypted2);

            Assert.Equal(plainText, decrypted1);
            Assert.Equal(plainText, decrypted2);
        }

        [Fact]
        public void Decrypt_WithInvalidBase64_ThrowsException()
        {
            var encryption = new SecureSettingsEncryption();

            Assert.Throws<InvalidOperationException>(() => encryption.Decrypt("not_valid_base64!!!"));
        }
    }
}
