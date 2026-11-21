using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WguLti.Core;
using WguLti.Core.Models;
using WguLti.Core.Stores;
using WguLti.Core.Tokens;
using WguLti.Infrastructure.Security;

namespace WguLti.Infrastructure.Keys;

public sealed class ToolKeyResolver : IToolKeyResolver
{
    private const int MaxBytes = 256 * 1024;
    private readonly IPlatformStore _store;
    private readonly IHttpClientFactory _http;
    private readonly LtiOptions _options;

    public ToolKeyResolver(IPlatformStore store, IHttpClientFactory http, IOptions<LtiOptions> options)
    {
        _store = store;
        _http = http;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SecurityKey>> ResolveAsync(ToolAssertionContext tool, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(tool.PublicJwk))
            return Parse(tool.PublicJwk);

        if (string.IsNullOrWhiteSpace(tool.JwksUri) || !Uri.TryCreate(tool.JwksUri, UriKind.Absolute, out var uri))
            return Array.Empty<SecurityKey>();
        if (!SsrfGuard.IsAllowed(uri, _options.AllowLoopbackToolUrls))
            return Array.Empty<SecurityKey>();

        var cached = await _store.GetFreshJwksCacheAsync(tool.RegistrationId, DateTimeOffset.UtcNow, ct);
        if (!string.IsNullOrWhiteSpace(cached))
            return Parse(cached);

        var client = _http.CreateClient("tool-jwks");
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return Array.Empty<SecurityKey>();
        var json = await ReadCappedAsync(response.Content, ct);
        var keys = Parse(json);
        if (keys.Count > 0)
            await _store.UpsertJwksCacheAsync(tool.RegistrationId, json, DateTimeOffset.UtcNow.AddHours(1), ct);
        return keys;
    }

    public static IReadOnlyList<SecurityKey> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<SecurityKey>();
        if (doc.RootElement.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array)
        {
            foreach (var key in keys.EnumerateArray())
                list.Add(new JsonWebKey(key.GetRawText()));
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            list.Add(new JsonWebKey(json));
        }
        return list;
    }

    private static async Task<string> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
                throw new InvalidOperationException("Tool JWKS document is too large.");
            buffer.Write(chunk, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
