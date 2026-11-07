namespace ServiceLib.Models;

[Serializable]
public class ProfileExItem
{
    [PrimaryKey]
    public string IndexId { get; set; }

    public int Delay { get; set; }
    public decimal Speed { get; set; }
    public int Sort { get; set; }
    public string? Message { get; set; }
    public string? GeoIP { get; set; }
    public string? GeoCountry { get; set; }
    public string? GeoRegion { get; set; }
    public string? GeoCity { get; set; }
    public string? GeoIsp { get; set; }
}
