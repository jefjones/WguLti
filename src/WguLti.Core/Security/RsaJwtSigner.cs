using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace WguLti.Core.Security;

public sealed class SigningMaterial
{
    public required string Kid { get; init; }
    public required RSA Rsa { get; init; }
}

public static class RsaJwtSigner
{
    public static string Sign(SigningMaterial material, string payloadJson)
    {
        var headerJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["alg"] = SecurityAlgorithms.RsaSha256,
            ["typ"] = "JWT",
            ["kid"] = material.Kid,
        });
        var header = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(headerJson));
        var payload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = Encoding.ASCII.GetBytes(header + "." + payload);
        var signature = material.Rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return header + "." + payload + "." + Base64UrlEncoder.Encode(signature);
    }

    public static (string N, string E) PublicComponents(RSA rsa)
    {
        var parameters = rsa.ExportParameters(false);
        return (Base64UrlEncoder.Encode(parameters.Modulus), Base64UrlEncoder.Encode(parameters.Exponent));
    }

    public static string KidFor(RSA rsa)
    {
        var modulus = rsa.ExportParameters(false).Modulus ?? throw new InvalidOperationException("RSA modulus is missing.");
        var hash = SHA256.HashData(modulus);
        return "wgulti-" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}

public interface IPlatformKeySource
{
    SigningMaterial GetActive();
    IReadOnlyList<PublishedKey> GetPublicKeys();
}

public sealed record PublishedKey(string Kid, string N, string E);
