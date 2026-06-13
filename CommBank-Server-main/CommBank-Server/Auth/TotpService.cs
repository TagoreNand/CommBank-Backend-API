using System.Security.Cryptography;
using System.Text;

namespace CommBank.Auth;

/// <summary>Time-based one-time passwords (RFC 6238) for multi-factor authentication.</summary>
public interface ITotpService
{
    /// <summary>Generates a new base32-encoded shared secret.</summary>
    string GenerateSecret();

    /// <summary>Builds the otpauth:// URI an authenticator app scans as a QR code.</summary>
    string BuildOtpAuthUri(string secret, string accountLabel, string issuer);

    /// <summary>Verifies a 6-digit code against the secret, allowing one step of clock drift.</summary>
    bool VerifyCode(string secret, string code);
}

/// <summary>
/// Self-contained RFC 6238 TOTP (HMAC-SHA1, 30-second period, 6 digits). No external dependency: secrets
/// are base32 (RFC 4648), codes are compared in constant time, and ±1 window of drift is tolerated.
/// </summary>
public sealed class TotpService : ITotpService
{
    private const int Digits = 6;
    private const int PeriodSeconds = 30;
    private const int DriftWindows = 1;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public string GenerateSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public string BuildOtpAuthUri(string secret, string accountLabel, string issuer)
    {
        string label = Uri.EscapeDataString($"{issuer}:{accountLabel}");
        string escapedIssuer = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={secret}&issuer={escapedIssuer}&algorithm=SHA1&digits={Digits}&period={PeriodSeconds}";
    }

    public bool VerifyCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        code = code.Trim();
        byte[] key;
        try
        {
            key = Base32Decode(secret);
        }
        catch (FormatException)
        {
            return false;
        }

        long timestep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / PeriodSeconds;
        for (long window = -DriftWindows; window <= DriftWindows; window++)
        {
            if (ConstantTimeEquals(Compute(key, timestep + window), code))
            {
                return true;
            }
        }

        return false;
    }

    private static string Compute(byte[] key, long counter)
    {
        var counterBytes = new byte[8];
        for (int i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(counter & 0xff);
            counter >>= 8;
        }

        using var hmac = new HMACSHA1(key);
        byte[] hash = hmac.ComputeHash(counterBytes);

        int offset = hash[^1] & 0x0f;
        int binary = ((hash[offset] & 0x7f) << 24)
                   | ((hash[offset + 1] & 0xff) << 16)
                   | ((hash[offset + 2] & 0xff) << 8)
                   | (hash[offset + 3] & 0xff);

        int otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString().PadLeft(Digits, '0');
    }

    private static bool ConstantTimeEquals(string a, string b) =>
        a.Length == b.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0;
        int bitsLeft = 0;

        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 0x1f]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            sb.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1f]);
        }

        return sb.ToString();
    }

    private static byte[] Base32Decode(string input)
    {
        input = input.TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(input.Length * 5 / 8);
        int buffer = 0;
        int bitsLeft = 0;

        foreach (char c in input)
        {
            int value = Base32Alphabet.IndexOf(c);
            if (value < 0)
            {
                throw new FormatException("Invalid base32 character.");
            }

            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 0xff));
                bitsLeft -= 8;
            }
        }

        return output.ToArray();
    }
}
