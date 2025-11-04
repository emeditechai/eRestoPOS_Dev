using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RestaurantManagementSystem.Services
{
    /// <summary>
    /// Service for encrypting and decrypting URL parameters using AES-256 encryption
    /// Industry standard approach for protecting sensitive URL data
    /// </summary>
    public class UrlEncryptionService
    {
        private readonly byte[] _key;
        private readonly byte[] _iv;
        private readonly ILogger<UrlEncryptionService> _logger;

        public UrlEncryptionService(IConfiguration configuration, ILogger<UrlEncryptionService> logger)
        {
            _logger = logger;
            
            // Get encryption key and IV from configuration
            var keyString = configuration["Encryption:Key"] 
                ?? throw new InvalidOperationException("Encryption key not found in configuration");
            var ivString = configuration["Encryption:IV"] 
                ?? throw new InvalidOperationException("Encryption IV not found in configuration");

            _key = Convert.FromBase64String(keyString);
            _iv = Convert.FromBase64String(ivString);

            // Validate key and IV lengths for AES-256
            if (_key.Length != 32)
                throw new InvalidOperationException("Encryption key must be 32 bytes (256 bits) for AES-256");
            if (_iv.Length != 16)
                throw new InvalidOperationException("Encryption IV must be 16 bytes (128 bits)");
        }

        /// <summary>
        /// Encrypts a dictionary of parameters and returns a URL-safe encrypted token
        /// </summary>
        public string EncryptParameters(Dictionary<string, string> parameters)
        {
            try
            {
                // Serialize parameters to JSON
                var json = JsonSerializer.Serialize(parameters);
                var plainBytes = Encoding.UTF8.GetBytes(json);

                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = _iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var encryptor = aes.CreateEncryptor();
                var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                // Convert to URL-safe Base64 string
                return Convert.ToBase64String(encryptedBytes)
                    .Replace('+', '-')
                    .Replace('/', '_')
                    .Replace("=", "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error encrypting parameters");
                throw new InvalidOperationException("Failed to encrypt parameters", ex);
            }
        }

        /// <summary>
        /// Decrypts a URL-safe encrypted token and returns the parameters dictionary
        /// </summary>
        public Dictionary<string, string> DecryptParameters(string encryptedToken)
        {
            try
            {
                // Convert from URL-safe Base64 back to standard Base64
                var base64 = encryptedToken
                    .Replace('-', '+')
                    .Replace('_', '/');

                // Add padding if needed
                switch (base64.Length % 4)
                {
                    case 2: base64 += "=="; break;
                    case 3: base64 += "="; break;
                }

                var encryptedBytes = Convert.FromBase64String(base64);

                using var aes = Aes.Create();
                aes.Key = _key;
                aes.IV = _iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
                var json = Encoding.UTF8.GetString(decryptedBytes);

                // Deserialize JSON back to dictionary
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error decrypting parameters: {Token}", encryptedToken);
                throw new InvalidOperationException("Failed to decrypt parameters. The token may be invalid or corrupted.", ex);
            }
        }

        /// <summary>
        /// Generates a random encryption key (32 bytes for AES-256)
        /// Use this method once to generate keys for configuration
        /// </summary>
        public static string GenerateKey()
        {
            using var rng = RandomNumberGenerator.Create();
            var key = new byte[32]; // 256 bits
            rng.GetBytes(key);
            return Convert.ToBase64String(key);
        }

        /// <summary>
        /// Generates a random IV (16 bytes for AES)
        /// Use this method once to generate IV for configuration
        /// </summary>
        public static string GenerateIV()
        {
            using var rng = RandomNumberGenerator.Create();
            var iv = new byte[16]; // 128 bits
            rng.GetBytes(iv);
            return Convert.ToBase64String(iv);
        }
    }

    /// <summary>
    /// Extension methods for easy URL encryption in views
    /// </summary>
    public static class UrlEncryptionExtensions
    {
        /// <summary>
        /// Creates an encrypted payment URL
        /// </summary>
        public static string GetEncryptedPaymentUrl(this IUrlHelper urlHelper, 
            UrlEncryptionService encryptionService, 
            int orderId, 
            decimal? discount = null)
        {
            var parameters = new Dictionary<string, string>
            {
                ["orderId"] = orderId.ToString()
            };

            if (discount.HasValue)
            {
                parameters["discount"] = discount.Value.ToString("F2");
            }

            var encryptedToken = encryptionService.EncryptParameters(parameters);
            return urlHelper.Action("ProcessPayment", "Payment", new { token = encryptedToken }) ?? "";
        }
    }
}
