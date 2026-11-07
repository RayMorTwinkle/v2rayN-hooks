namespace ServiceLib.Models;

public class IpApiGeoResponse
{
    public string? Query { get; set; }
    public string? Status { get; set; }
    public string? Country { get; set; }
    public string? RegionName { get; set; }
    public string? City { get; set; }
    public string? Isp { get; set; }
    public string? Message { get; set; }
}
