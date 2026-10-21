using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Island;

// Погода для пузыря (шаг 54). Open-Meteo (open-meteo.com): бесплатно и без ключа.
// Город: из настроек («Погода», поле «Город»), а если пусто — примерно по IP (ipwho.is, тоже без ключа).
// Обновляется не чаще раза в 20 минут. Если сеть недоступна, пузырь показывает последнее, что успели узнать.
internal sealed class WeatherNow
{
    public string City = "";
    public double Temp;
    public int Code;
    public bool Day = true;
    public List<(DateTime day, int code, double max, double min)> Days = new();
    public DateTime At;
}

internal static class Weather
{
    private static readonly HttpClient Web = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static WeatherNow? _last;
    private static string _lastFor = "";
    private static Task<WeatherNow?>? _busy;

    public static WeatherNow? Last => _last;

    // вернуть свежую (не старше 20 минут) или запросить; city = "" — по IP
    public static Task<WeatherNow?> Get(string city)
    {
        if (_last != null && _lastFor == city && (DateTime.Now - _last.At).TotalMinutes < 20) return Task.FromResult<WeatherNow?>(_last);
        if (_busy != null && !_busy.IsCompleted) return _busy;
        return _busy = Fetch(city);
    }

    private static async Task<WeatherNow?> Fetch(string city)
    {
        try
        {
            double lat, lon;
            string name;
            if (city.Trim().Length > 0)
            {
                string geo = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=" + Loc.T("ru", "en") + "&name=" + Uri.EscapeDataString(city.Trim());
                using var g = JsonDocument.Parse(await Web.GetStringAsync(geo));
                if (!g.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0) return _last;
                var r = res[0];
                lat = r.GetProperty("latitude").GetDouble();
                lon = r.GetProperty("longitude").GetDouble();
                name = r.GetProperty("name").GetString() ?? city;
            }
            else
            {
                using var ip = JsonDocument.Parse(await Web.GetStringAsync("https://ipwho.is/?fields=city,latitude,longitude,success"));
                var r = ip.RootElement;
                if (r.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) return _last;
                lat = r.GetProperty("latitude").GetDouble();
                lon = r.GetProperty("longitude").GetDouble();
                name = r.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";
            }
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToString(CultureInfo.InvariantCulture) +
                         "&longitude=" + lon.ToString(CultureInfo.InvariantCulture) +
                         "&current=temperature_2m,weather_code,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4";
            using var doc = JsonDocument.Parse(await Web.GetStringAsync(url));
            var cur = doc.RootElement.GetProperty("current");
            var w = new WeatherNow
            {
                City = name, Temp = cur.GetProperty("temperature_2m").GetDouble(), Code = cur.GetProperty("weather_code").GetInt32(),
                Day = cur.GetProperty("is_day").GetInt32() == 1, At = DateTime.Now
            };
            var d = doc.RootElement.GetProperty("daily");
            var times = d.GetProperty("time");
            for (int i = 0; i < times.GetArrayLength(); i++)
                w.Days.Add((DateTime.Parse(times[i].GetString()!, CultureInfo.InvariantCulture), d.GetProperty("weather_code")[i].GetInt32(),
                            d.GetProperty("temperature_2m_max")[i].GetDouble(), d.GetProperty("temperature_2m_min")[i].GetDouble()));
            _last = w;
            _lastFor = city;
            return w;
        }
        catch (Exception ex) { App.LogOnce("Weather", ex); return _last; }
    }

    public static string Deg(double t) => (Math.Round(t) > 0 ? "+" : "") + Math.Round(t).ToString(CultureInfo.InvariantCulture) + "°";

    // коды погоды WMO -> значок и слово
    public static string Icon(int code, bool day) => code switch
    {
        0 => day ? "sun" : "moon",
        1 or 2 => day ? "cloud-sun" : "cloud",
        3 => "cloud",
        45 or 48 => "cloud-fog",
        >= 51 and <= 67 or >= 80 and <= 82 => "cloud-rain",
        >= 71 and <= 77 or 85 or 86 => "cloud-snow",
        >= 95 => "cloud-lightning",
        _ => "cloud"
    };

    public static string Text(int code) => Loc.T(code switch
    {
        0 => "ясно",
        1 => "почти ясно",
        2 => "переменная облачность",
        3 => "пасмурно",
        45 or 48 => "туман",
        >= 51 and <= 57 => "морось",
        >= 61 and <= 67 => "дождь",
        >= 71 and <= 77 => "снег",
        >= 80 and <= 82 => "ливень",
        85 or 86 => "снегопад",
        >= 95 => "гроза",
        _ => "облачно"
    });
}
