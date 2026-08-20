using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Miao.Core.Services
{
    public sealed class FanqieDecryptService
    {
        private readonly HttpClient _http;
        private readonly FanqieClientConfig _config;

        private string? _dynamicKey;
        private DateTime _keyExpireTime;

        private const string ApiBase =
            "https://api5-normal-sinfonlineb.fqnovel.com";

        public FanqieDecryptService(
            FanqieClientConfig config,
            HttpClient? httpClient = null)
        {
            _config =
                config ?? throw new ArgumentNullException(nameof(config));

            _http =
                httpClient ?? new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(30)
                };

            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "okhttp/4.9.3");
        }

        public async Task<string> GetChapterContentAsync(
            string itemId,
            string? installId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return "";

            var response =
                await GetBatchFullAsync(
                    itemId,
                    installId,
                    cancellationToken);

            var encryptedContent =
                FindEncryptedContent(
                    response,
                    itemId);

            if (string.IsNullOrWhiteSpace(encryptedContent))
                return "";

            return await DecryptContentAsync(
                encryptedContent,
                installId,
                cancellationToken);
        }

        private async Task<JsonElement> GetBatchFullAsync(
            string itemId,
            string? installId,
            CancellationToken cancellationToken)
        {
            var url =
                $"{ApiBase}/reading/reader/batch_full/v" +
                $"?item_ids={Uri.EscapeDataString(itemId)}" +
                $"&req_type=1" +
                $"&aid={Uri.EscapeDataString(_config.Aid)}" +
                $"&update_version_code={Uri.EscapeDataString(_config.VersionCode)}";

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    url);

            AddHeaders(
                request,
                installId);

            using var response =
                await _http.SendAsync(
                    request,
                    cancellationToken);

            response.EnsureSuccessStatusCode();

            var json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            using var document =
                JsonDocument.Parse(json);

            return document.RootElement.Clone();
        }

        private async Task<string> GetDynamicKeyAsync(
            string? installId,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_dynamicKey) &&
                DateTime.UtcNow < _keyExpireTime)
            {
                return _dynamicKey;
            }

            if (string.IsNullOrWhiteSpace(_config.RegKey))
            {
                throw new InvalidOperationException(
                    "Fanqie REG_KEY chưa được cấu hình.");
            }

            var deviceId =
                GetDeviceId();

            var content =
                GenerateRegisterContent(
                    deviceId,
                    _config.RegKey);

            var payload =
                JsonSerializer.Serialize(
                    new
                    {
                        content,
                        keyver = 1
                    });

            var url =
                $"{ApiBase}/reading/crypt/registerkey" +
                $"?aid={Uri.EscapeDataString(_config.Aid)}";

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    url);

            AddHeaders(
                request,
                installId);

            request.Content =
                new StringContent(
                    payload,
                    Encoding.UTF8,
                    "application/json");

            using var response =
                await _http.SendAsync(
                    request,
                    cancellationToken);

            response.EnsureSuccessStatusCode();

            var json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            using var document =
                JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty(
                    "data",
                    out var data))
            {
                throw new InvalidOperationException(
                    "Fanqie registerkey không trả về data.");
            }

            if (!data.TryGetProperty(
                    "key",
                    out var keyElement))
            {
                throw new InvalidOperationException(
                    "Fanqie registerkey không trả về data.key.");
            }

            var keyBase64 =
                keyElement.GetString();

            if (string.IsNullOrWhiteSpace(keyBase64))
            {
                throw new InvalidOperationException(
                    "Fanqie registerkey trả về data.key rỗng.");
            }

            byte[] encryptedKey;

            try
            {
                encryptedKey =
                    Convert.FromBase64String(
                        keyBase64);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    "Fanqie registerkey trả về data.key không phải Base64.",
                    ex);
            }

            var decryptedKey =
                AesCbcDecrypt(
                    encryptedKey,
                    HexToBytes(_config.RegKey));

            _dynamicKey =
                Convert.ToHexString(
                    decryptedKey)
                .ToLowerInvariant();

            _keyExpireTime =
                DateTime.UtcNow.AddHours(1);

            return _dynamicKey;
        }

        private async Task<string> DecryptContentAsync(
            string encryptedContent,
            string? installId,
            CancellationToken cancellationToken)
        {
            var key =
                await GetDynamicKeyAsync(
                    installId,
                    cancellationToken);

            byte[] encrypted;

            try
            {
                encrypted =
                    Convert.FromBase64String(
                        encryptedContent);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    "Fanqie chapter content không phải Base64.",
                    ex);
            }

            var decrypted =
                AesCbcDecrypt(
                    encrypted,
                    HexToBytes(key));

            var decompressed =
                Gunzip(decrypted);

            return Encoding.UTF8.GetString(
                decompressed);
        }

        private static string? FindEncryptedContent(
            JsonElement element,
            string itemId)
        {
            if (element.ValueKind ==
                JsonValueKind.Object)
            {
                if (element.TryGetProperty(
                        "content",
                        out var content))
                {
                    if (content.ValueKind ==
                        JsonValueKind.String)
                    {
                        var value =
                            content.GetString();

                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }

                    if (content.ValueKind ==
                            JsonValueKind.Object &&
                        content.TryGetProperty(
                            "value",
                            out var valueElement) &&
                        valueElement.ValueKind ==
                            JsonValueKind.String)
                    {
                        var value =
                            valueElement.GetString();

                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }
                }

                if (element.TryGetProperty(
                        itemId,
                        out var chapter))
                {
                    var result =
                        FindEncryptedContent(
                            chapter,
                            itemId);

                    if (!string.IsNullOrWhiteSpace(result))
                        return result;
                }

                foreach (var property
                         in element.EnumerateObject())
                {
                    var result =
                        FindEncryptedContent(
                            property.Value,
                            itemId);

                    if (!string.IsNullOrWhiteSpace(result))
                        return result;
                }
            }
            else if (element.ValueKind ==
                     JsonValueKind.Array)
            {
                foreach (var item
                         in element.EnumerateArray())
                {
                    var result =
                        FindEncryptedContent(
                            item,
                            itemId);

                    if (!string.IsNullOrWhiteSpace(result))
                        return result;
                }
            }

            return null;
        }

        private static byte[] AesCbcDecrypt(
            byte[] encrypted,
            byte[] key)
        {
            if (encrypted.Length < 32)
            {
                throw new InvalidOperationException(
                    "Fanqie encrypted data quá ngắn.");
            }

            if (key.Length != 16 &&
                key.Length != 24 &&
                key.Length != 32)
            {
                throw new InvalidOperationException(
                    $"Fanqie AES key không hợp lệ: {key.Length} bytes.");
            }

            var iv =
                new byte[16];

            Buffer.BlockCopy(
                encrypted,
                0,
                iv,
                0,
                16);

            var ciphertext =
                new byte[encrypted.Length - 16];

            Buffer.BlockCopy(
                encrypted,
                16,
                ciphertext,
                0,
                ciphertext.Length);

            using var aes =
                Aes.Create();

            aes.Key =
                key;

            aes.IV =
                iv;

            aes.Mode =
                CipherMode.CBC;

            aes.Padding =
                PaddingMode.PKCS7;

            using var decryptor =
                aes.CreateDecryptor();

            return decryptor.TransformFinalBlock(
                ciphertext,
                0,
                ciphertext.Length);
        }

        private static byte[] Gunzip(
            byte[] data)
        {
            using var input =
                new MemoryStream(data);

            using var gzip =
                new GZipStream(
                    input,
                    CompressionMode.Decompress);

            using var output =
                new MemoryStream();

            gzip.CopyTo(output);

            return output.ToArray();
        }

        private static byte[] HexToBytes(
            string hex)
        {
            if (string.IsNullOrWhiteSpace(hex) ||
                hex.Length % 2 != 0)
            {
                throw new ArgumentException(
                    "Invalid hex string.");
            }

            var result =
                new byte[hex.Length / 2];

            for (var i = 0;
                 i < result.Length;
                 i++)
            {
                result[i] =
                    Convert.ToByte(
                        hex.Substring(
                            i * 2,
                            2),
                        16);
            }

            return result;
        }

        private static string GenerateRegisterContent(
            ulong deviceId,
            string regKey)
        {
            var deviceBytes =
                ToUInt64LittleEndian(
                    deviceId);

            var zeroBytes =
                ToUInt64LittleEndian(0);

            var combined =
                new byte[16];

            Buffer.BlockCopy(
                deviceBytes,
                0,
                combined,
                0,
                8);

            Buffer.BlockCopy(
                zeroBytes,
                0,
                combined,
                8,
                8);

            var iv =
                new byte[16];

            RandomNumberGenerator.Fill(iv);

            var encrypted =
                AesCbcEncrypt(
                    combined,
                    HexToBytes(regKey),
                    iv);

            var result =
                new byte[16 + encrypted.Length];

            Buffer.BlockCopy(
                iv,
                0,
                result,
                0,
                16);

            Buffer.BlockCopy(
                encrypted,
                0,
                result,
                16,
                encrypted.Length);

            return Convert.ToBase64String(
                result);
        }

        private static byte[] AesCbcEncrypt(
            byte[] data,
            byte[] key,
            byte[] iv)
        {
            if (key.Length != 16 &&
                key.Length != 24 &&
                key.Length != 32)
            {
                throw new InvalidOperationException(
                    $"Fanqie AES key không hợp lệ: {key.Length} bytes.");
            }

            using var aes =
                Aes.Create();

            aes.Key =
                key;

            aes.IV =
                iv;

            aes.Mode =
                CipherMode.CBC;

            aes.Padding =
                PaddingMode.PKCS7;

            using var encryptor =
                aes.CreateEncryptor();

            return encryptor.TransformFinalBlock(
                data,
                0,
                data.Length);
        }

        private static byte[] ToUInt64LittleEndian(
            ulong value)
        {
            var bytes =
                new byte[8];

            for (var i = 0;
                 i < 8;
                 i++)
            {
                bytes[i] =
                    (byte)(value >> (i * 8));
            }

            return bytes;
        }

        private ulong GetDeviceId()
        {
            if (!string.IsNullOrWhiteSpace(
                    _config.ServerDeviceId) &&
                ulong.TryParse(
                    _config.ServerDeviceId,
                    out var numericDeviceId))
            {
                return numericDeviceId;
            }

            var value =
                Environment.MachineName;

            var hash =
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        value));

            return BitConverter.ToUInt64(
                hash,
                0);
        }

        private void AddHeaders(
            HttpRequestMessage request,
            string? installId)
        {
            request.Headers.TryAddWithoutValidation(
                "User-Agent",
                "okhttp/4.9.3");

            var effectiveInstallId =
                !string.IsNullOrWhiteSpace(installId)
                    ? installId
                    : _config.InstallId;

            if (!string.IsNullOrWhiteSpace(
                    effectiveInstallId))
            {
                request.Headers.TryAddWithoutValidation(
                    "Cookie",
                    $"install_id={effectiveInstallId}");
            }
        }
    }
}