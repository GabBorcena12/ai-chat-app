using System.Security.Cryptography;
using System.Text;

namespace AIChatApp.API.Services.Authentication
{
    /// <summary>
    /// Creates and verifies Google Authenticator-compatible TOTP secrets and codes.
    /// This service performs local cryptographic operations only and does not call a Google API.
    /// </summary>
    public class GoogleAuthenticatorService
    {
        private const int SecretSize = 20;
        private const int Digits = 6;
        private const int TimeStepSeconds = 30;
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        // generate shared secret key for a user,
        // which will be stored in the database
        // and used for both generating the QR code and validating OTP codes
        public string GenerateSecret()
        {
            Span<byte> secretBytes = stackalloc byte[SecretSize];
            RandomNumberGenerator.Fill(secretBytes);
            return Base32Encode(secretBytes);
        }

        // generate the otpauth URI that encodes the secret and account information
        public string BuildQrCodeUri(string issuer, string accountName, string secret)
        {
            var escapedIssuer = Uri.EscapeDataString(issuer);
            var escapedAccountName = Uri.EscapeDataString(accountName);
            return $"otpauth://totp/{escapedIssuer}:{escapedAccountName}?secret={secret}&issuer={escapedIssuer}&digits={Digits}&period={TimeStepSeconds}";
        }

        // TOTP - Time based one time password
        // server validate the otp code provided by the user
        // by computing the expected code based on the shared secret and current time, allowing for some time drift
        public bool ValidateCode(string? secret, string? code, int allowedDriftWindows = 1)
        {
            if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            var normalizedCode = NormalizeCode(code);
            if (normalizedCode.Length != Digits || !normalizedCode.All(char.IsDigit))
            {
                return false;
            }

            var secretBytes = Base32Decode(secret);
            var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var timestep = unixTime / TimeStepSeconds;

            foreach (var offset in Enumerable.Range(-allowedDriftWindows, allowedDriftWindows * 2 + 1))
            {
                var expectedCode = ComputeTotp(secretBytes, timestep + offset);
                if (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expectedCode),
                    Encoding.UTF8.GetBytes(normalizedCode)))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeCode(string code) =>
            new(code.Where(char.IsDigit).ToArray());

        private static string ComputeTotp(byte[] secret, long timestepNumber)
        {
            Span<byte> timestepBytes = stackalloc byte[8];
            for (var i = 7; i >= 0; i--)
            {
                timestepBytes[i] = (byte)(timestepNumber & 0xFF);
                timestepNumber >>= 8;
            }

            using var hmac = new HMACSHA1(secret);
            var hash = hmac.ComputeHash(timestepBytes.ToArray());
            var offset = hash[^1] & 0x0F;
            var binaryCode =
                ((hash[offset] & 0x7F) << 24) |
                (hash[offset + 1] << 16) |
                (hash[offset + 2] << 8) |
                hash[offset + 3];

            var otp = binaryCode % (int)Math.Pow(10, Digits);
            return otp.ToString(new string('0', Digits));
        }

        private static string Base32Encode(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
            {
                return string.Empty;
            }

            var output = new StringBuilder((int)Math.Ceiling(data.Length / 5d) * 8);
            var buffer = (int)data[0];
            var next = 1;
            var bitsLeft = 8;

            while (bitsLeft > 0 || next < data.Length)
            {
                if (bitsLeft < 5)
                {
                    if (next < data.Length)
                    {
                        buffer <<= 8;
                        buffer |= data[next++] & 0xFF;
                        bitsLeft += 8;
                    }
                    else
                    {
                        var pad = 5 - bitsLeft;
                        buffer <<= pad;
                        bitsLeft += pad;
                    }
                }

                var index = (buffer >> (bitsLeft - 5)) & 0x1F;
                bitsLeft -= 5;
                output.Append(Base32Alphabet[index]);
            }

            return output.ToString();
        }

        private static byte[] Base32Decode(string input)
        {
            var normalized = input.Trim().TrimEnd('=').ToUpperInvariant();
            if (normalized.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var output = new List<byte>(normalized.Length * 5 / 8);
            var bitBuffer = 0;
            var bitsInBuffer = 0;

            foreach (var c in normalized)
            {
                var charIndex = Base32Alphabet.IndexOf(c);
                if (charIndex < 0)
                {
                    throw new FormatException("Secret contains invalid Base32 characters.");
                }

                bitBuffer = (bitBuffer << 5) | charIndex;
                bitsInBuffer += 5;

                if (bitsInBuffer >= 8)
                {
                    bitsInBuffer -= 8;
                    output.Add((byte)((bitBuffer >> bitsInBuffer) & 0xFF));
                }
            }

            return output.ToArray();
        }
    }
}
