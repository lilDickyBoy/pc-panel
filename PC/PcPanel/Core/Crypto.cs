using System;
using System.Security.Cryptography;
using System.Text;

namespace PcPanel.Core
{
    /// <summary>
    /// AES-256-GCM. Key = SHA256(pairing token). Wire format: base64(nonce[12] | ciphertext | tag[16]).
    /// The Android app uses exactly the same scheme.
    /// </summary>
    public sealed class Crypto
    {
        readonly byte[] _key;

        public Crypto(string token)
        {
            _key = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        }

        public string Encrypt(string plain)
        {
            var pt = Encoding.UTF8.GetBytes(plain);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var outBuf = new byte[12 + pt.Length + 16];
            Buffer.BlockCopy(nonce, 0, outBuf, 0, 12);
            using (var gcm = new AesGcm(_key, 16))
            {
                gcm.Encrypt(nonce, pt, outBuf.AsSpan(12, pt.Length), outBuf.AsSpan(12 + pt.Length, 16));
            }
            return Convert.ToBase64String(outBuf);
        }

        public string Decrypt(string b64)
        {
            var data = Convert.FromBase64String(b64);
            if (data.Length < 28) throw new CryptographicException("short");
            int ctLen = data.Length - 28;
            var pt = new byte[ctLen];
            using (var gcm = new AesGcm(_key, 16))
            {
                gcm.Decrypt(data.AsSpan(0, 12), data.AsSpan(12, ctLen), data.AsSpan(12 + ctLen, 16), pt);
            }
            return Encoding.UTF8.GetString(pt);
        }
    }
}
