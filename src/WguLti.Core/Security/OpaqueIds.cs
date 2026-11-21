using System.Security.Cryptography;

namespace WguLti.Core.Security;

public static class OpaqueIds
{
    public static string NewClientId()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes);
        var digits = value % 9000000000000000UL + 1000000000000000UL;
        return digits.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string NewDeploymentId() => "d" + Guid.NewGuid().ToString("N");

    public static string NewHex(int bytes = 32) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    public static byte[] Sha256(string value) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
}
