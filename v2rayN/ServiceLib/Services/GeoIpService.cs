using System.Net;
using ServiceLib.Helper;
using ServiceLib.Models;

namespace ServiceLib.Services;

public static class GeoIpService
{
    public static async Task<IpApiGeoResponse?> QueryAsync(string? address)
    {
        if (address.IsNullOrEmpty())
        {
            return null;
        }

        var ip = await ResolveIPv4Async(address);
        if (ip.IsNullOrEmpty())
        {
            return new IpApiGeoResponse
            {
                Status = "fail",
                Message = "resolve failed"
            };
        }

        var geo = await QuerySingleAsync("https", ip);
        if (geo == null)
        {
            return new IpApiGeoResponse
            {
                Query = ip,
                Status = "fail",
                Message = "request failed"
            };
        }

        if (string.Equals(geo.Status, "success", StringComparison.OrdinalIgnoreCase))
        {
            geo.Query ??= ip;
            return geo;
        }

        if ((geo.Message ?? string.Empty).Contains("SSL unavailable", StringComparison.OrdinalIgnoreCase))
        {
            var fallback = await QuerySingleAsync("http", ip);
            if (fallback != null)
            {
                fallback.Query ??= ip;
                return fallback;
            }
        }

        geo.Query ??= ip;
        return geo;
    }

    private static async Task<string?> ResolveIPv4Async(string address)
    {
        if (IPAddress.TryParse(address, out var parsed))
        {
            return parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                ? parsed.ToString()
                : null;
        }

        try
        {
            var all = await Dns.GetHostAddressesAsync(address);
            return all.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<IpApiGeoResponse?> QuerySingleAsync(string scheme, string ip)
    {
        var url = $"{scheme}://ip-api.com/json/{Uri.EscapeDataString(ip)}";
        var result = await HttpClientHelper.Instance.TryGetAsync(url);
        if (result.IsNullOrEmpty())
        {
            return null;
        }

        return JsonUtils.Deserialize<IpApiGeoResponse>(result);
    }
}
