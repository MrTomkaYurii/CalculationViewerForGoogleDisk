using System.Text.Json.Serialization;

namespace CalculationViewer.Models;

public sealed class VisitorLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string Ip { get; set; }
    public string? Country { get; set; }
    public string? CountryCode { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? FlagEmoji { get; set; }
    public string? Isp { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Page { get; set; } = "Головна";
    public string? UserAgent { get; set; }
    public string? Device { get; set; }

    /// <summary>
    /// Відображення у форматі: IP (Країна), наприклад: 195.88.139.241 (Україна)
    /// </summary>
    [JsonIgnore]
    public string DisplayTitle => $"{Ip} ({CountryDisplayName})";

    [JsonIgnore]
    public string CountryDisplayName => UaCountries.GetDisplayName(Country, CountryCode);
}

public sealed class ClientInfoDto
{
    public string? UserAgent { get; set; }
    public string? Language { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
}

public sealed class IpWhoIsResponse
{
    public string? Ip { get; set; }
    public bool Success { get; set; }
    public string? Country { get; set; }
    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public IpWhoIsFlag? Flag { get; set; }
    public IpWhoIsConnection? Connection { get; set; }
}

public sealed class IpWhoIsFlag
{
    public string? Emoji { get; set; }
    public string? Img { get; set; }
}

public sealed class IpWhoIsConnection
{
    public string? Isp { get; set; }
    public string? Org { get; set; }
}

public sealed class IpifyResponse
{
    public string? Ip { get; set; }
}

public static class UaCountries
{
    private static readonly Dictionary<string, string> CodeToName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UA"] = "Україна",
        ["UKRAINE"] = "Україна",
        ["PL"] = "Польща",
        ["POLAND"] = "Польща",
        ["DE"] = "Німеччина",
        ["GERMANY"] = "Німеччина",
        ["US"] = "США",
        ["USA"] = "США",
        ["UNITED STATES"] = "США",
        ["GB"] = "Велика Британія",
        ["UNITED KINGDOM"] = "Велика Британія",
        ["CA"] = "Канада",
        ["CANADA"] = "Канада",
        ["FR"] = "Франція",
        ["FRANCE"] = "Франція",
        ["IT"] = "Італія",
        ["ITALY"] = "Італія",
        ["ES"] = "Іспанія",
        ["SPAIN"] = "Іспанія",
        ["CZ"] = "Чехія",
        ["CZECH REPUBLIC"] = "Чехія",
        ["CZECHIA"] = "Чехія",
        ["SK"] = "Словаччина",
        ["SLOVAKIA"] = "Словаччина",
        ["RO"] = "Румунія",
        ["ROMANIA"] = "Румунія",
        ["MD"] = "Молдова",
        ["MOLDOVA"] = "Молдова",
        ["LT"] = "Литва",
        ["LITHUANIA"] = "Литва",
        ["LV"] = "Латвія",
        ["LATVIA"] = "Латвія",
        ["EE"] = "Естонія",
        ["ESTONIA"] = "Естонія",
        ["NL"] = "Нідерланди",
        ["NETHERLANDS"] = "Нідерланди",
        ["CH"] = "Швейцарія",
        ["SWITZERLAND"] = "Швейцарія",
        ["AT"] = "Австрія",
        ["AUSTRIA"] = "Австрія",
        ["SE"] = "Швеція",
        ["SWEDEN"] = "Швеція",
        ["NO"] = "Норвегія",
        ["NORWAY"] = "Норвегія",
        ["FI"] = "Фінляндія",
        ["FINLAND"] = "Фінляндія",
        ["DK"] = "Данія",
        ["DENMARK"] = "Данія",
        ["BE"] = "Бельгія",
        ["BELGIUM"] = "Бельгія",
        ["IE"] = "Ірландія",
        ["IRELAND"] = "Ірландія",
        ["PT"] = "Португалія",
        ["PORTUGAL"] = "Португалія",
        ["GR"] = "Греція",
        ["GREECE"] = "Греція",
        ["TR"] = "Туреччина",
        ["TURKEY"] = "Туреччина",
        ["TÜRKIYE"] = "Туреччина",
        ["IL"] = "Ізраїль",
        ["ISRAEL"] = "Ізраїль",
        ["JP"] = "Японія",
        ["JAPAN"] = "Японія",
        ["KR"] = "Південна Корея",
        ["SOUTH KOREA"] = "Південна Корея",
        ["AU"] = "Австралія",
        ["AUSTRALIA"] = "Австралія",
        ["NZ"] = "Нова Зеландія",
        ["NEW ZEALAND"] = "Нова Зеландія",
        ["BR"] = "Бразилія",
        ["BRAZIL"] = "Бразилія",
        ["KZ"] = "Казахстан",
        ["KAZAKHSTAN"] = "Казахстан",
        ["GE"] = "Грузія",
        ["GEORGIA"] = "Грузія"
    };

    public static string GetDisplayName(string? country, string? countryCode)
    {
        if (!string.IsNullOrWhiteSpace(countryCode) && CodeToName.TryGetValue(countryCode.Trim(), out var uaName))
            return uaName;

        if (!string.IsNullOrWhiteSpace(country))
        {
            var trimmed = country.Trim();
            if (CodeToName.TryGetValue(trimmed, out var uaByName))
                return uaByName;
            return trimmed;
        }

        return !string.IsNullOrWhiteSpace(countryCode) ? countryCode.Trim() : "Невідома країна";
    }
}
