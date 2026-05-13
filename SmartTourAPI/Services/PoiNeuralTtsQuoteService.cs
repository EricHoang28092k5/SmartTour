using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace SmartTourAPI.Services;

/// <summary>
/// Ước giá Neural TTS: 15 USD / 1 triệu ký tự, quy đổi VND; cộng độ dài script tiếng Việt + bản dịch zh,ja,en,ko (Google translate).
/// Cấu hình: PoiCreation:UsdPerMillionNeuralChars (15), PoiCreation:UsdToVnd (25400), PoiCreation:MinimumChargeVnd.
/// </summary>
public class PoiNeuralTtsQuoteService
{
    private static readonly HttpClient Http = new();
    private readonly IConfiguration _config;

    public PoiNeuralTtsQuoteService(IConfiguration config)
    {
        _config = config;
    }

    public async Task<(long chargeVnd, int totalChars, IReadOnlyList<(string Lang, int Len)> breakdown)> EstimatePoiCreationAsync(
        string? ttsScript,
        string? description,
        CancellationToken ct = default)
    {
        var vi = string.IsNullOrWhiteSpace(ttsScript) ? (description ?? string.Empty) : ttsScript.Trim();
        var section = _config.GetSection("PoiCreation");
        var usdPerMillion = double.TryParse(section["UsdPerMillionNeuralChars"], out var um) && um > 0 ? um : 15d;
        var usdToVnd = double.TryParse(section["UsdToVnd"], out var rate) && rate > 0 ? rate : 25400d;
        var minimum = long.TryParse(section["MinimumChargeVnd"], out var min) && min > 0 ? min : 1L;

        var targets = new[] { "zh-CN", "ja", "en", "ko" };
        var breakdown = new List<(string Lang, int Len)> { ("vi", vi.Length) };
        var totalChars = vi.Length;

        foreach (var tl in targets)
        {
            ct.ThrowIfCancellationRequested();
            var translated = string.Equals(tl, "vi", StringComparison.OrdinalIgnoreCase)
                ? vi
                : await TranslateViToAsync(vi, tl, ct);
            breakdown.Add((tl, translated.Length));
            totalChars += translated.Length;
        }

        var usdPerChar = usdPerMillion / 1_000_000d;
        var rawVnd = (decimal)totalChars * (decimal)usdPerChar * (decimal)usdToVnd;
        var charge = (long)Math.Ceiling(rawVnd);
        if (charge < minimum) charge = minimum;
        return (charge, totalChars, breakdown);
    }

    private static async Task<string> TranslateViToAsync(string text, string targetLang, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text)) return text;
        try
        {
            var tl = targetLang.Split('-')[0];
            var url =
                $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=vi&tl={Uri.EscapeDataString(tl)}&dt=t&q={Uri.EscapeDataString(text)}";
            var res = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(res);
            return doc.RootElement[0][0][0].GetString() ?? text;
        }
        catch
        {
            return text;
        }
    }
}
