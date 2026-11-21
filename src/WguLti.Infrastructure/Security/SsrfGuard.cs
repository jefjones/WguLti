using System.Net;
using System.Net.Sockets;

namespace WguLti.Infrastructure.Security;

public static class SsrfGuard
{
    public static bool IsAllowed(Uri uri, bool allowLoopback)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;
        if (uri.IsLoopback)
            return allowLoopback;

        try
        {
            var addresses = Dns.GetHostAddresses(uri.Host);
            return addresses.Length > 0 && addresses.All(IsPublic);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 0 || b[0] == 10 || b[0] == 127) return false;
            if (b[0] == 100 && b[1] is >= 64 and <= 127) return false;
            if (b[0] == 169 && b[1] == 254) return false;
            if (b[0] == 172 && b[1] is >= 16 and <= 31) return false;
            if (b[0] == 192 && b[1] == 168) return false;
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return false;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;
            return true;
        }

        return false;
    }
}
