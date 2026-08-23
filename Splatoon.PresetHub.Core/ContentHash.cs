using System.Security.Cryptography;
using System.Text;

namespace Splatoon.PresetHub.Core;

public static class ContentHash
{
    public static string Sha256(string content) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
