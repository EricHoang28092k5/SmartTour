using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourAPI.Data;

namespace SmartTourAPI.Services;

public class PoiCreationFulfillmentProcessor
{
    private static readonly HttpClient Http = new();
    private readonly AppDbContext _db;
    private readonly IVoiceService _voiceService;
    private readonly ILogger<PoiCreationFulfillmentProcessor> _logger;

    public PoiCreationFulfillmentProcessor(
        AppDbContext db,
        IVoiceService voiceService,
        ILogger<PoiCreationFulfillmentProcessor> logger)
    {
        _db = db;
        _voiceService = voiceService;
        _logger = logger;
    }

    public async Task FulfillAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var order = await _db.VendorPremiumOrders.FirstOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);
        if (order == null ||
            !string.Equals(order.OrderKind, "poi_create", StringComparison.OrdinalIgnoreCase) ||
            order.PoiId > 0 ||
            !string.Equals(order.Status, "paid", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(order.PoiCreationDraftJson))
        {
            await MarkFulfillFailedAsync(order, "missing_draft_json", cancellationToken);
            return;
        }

        PoiCreationDraftPayload? draft;
        try
        {
            draft = JsonSerializer.Deserialize<PoiCreationDraftPayload>(order.PoiCreationDraftJson);
        }
        catch (Exception ex)
        {
            await MarkFulfillFailedAsync(order, "invalid_draft:" + ex.Message, cancellationToken);
            return;
        }

        if (draft == null || string.IsNullOrWhiteSpace(draft.Name))
        {
            await MarkFulfillFailedAsync(order, "invalid_draft_fields", cancellationToken);
            return;
        }

        int? createdPoiId = null;
        try
        {
            var poi = new Poi
            {
                Name = draft.Name.Trim(),
                Description = draft.Description ?? string.Empty,
                TtsScript = string.IsNullOrWhiteSpace(draft.TtsScript) ? draft.Description : draft.TtsScript,
                Lat = draft.Lat,
                Lng = draft.Lng,
                Radius = draft.Radius > 0 ? draft.Radius : 100,
                ImageUrl = draft.ImageUrl ?? string.Empty,
                Priority = draft.Priority,
                OpenTime = ParseOptionalTime(draft.OpenTime),
                CloseTime = ParseOptionalTime(draft.CloseTime),
                CategoryId = draft.CategoryId,
                VendorId = draft.VendorId,
                CreatedBy = draft.CreatedBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ApprovalStatus = "approved",
                IsActive = true,
                ApprovedAt = DateTime.UtcNow,
                ApprovedByUserId = null,
                ApprovalNote = null
            };

            _db.Pois.Add(poi);
            await _db.SaveChangesAsync(cancellationToken);
            createdPoiId = poi.Id;

            var missingAudio = await CreateTranslationsAndAudioAsync(poi, cancellationToken);
            order.PoiId = poi.Id;
            order.PoiCreationDraftJson = null;
            order.UpdatedAt = DateTime.UtcNow;
            if (missingAudio > 0)
                order.LastError = $"audio_partial:{missingAudio}";
            else
                order.LastError = null;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Poi creation fulfilled for order {OrderId}, poi {PoiId}", orderId, poi.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fulfill poi creation failed for order {OrderId}", orderId);
            if (createdPoiId is int pid)
            {
                try
                {
                    var orphan = await _db.Pois.Include(p => p.PoiTranslations).FirstOrDefaultAsync(p => p.Id == pid, cancellationToken);
                    if (orphan != null)
                    {
                        _db.PoiTranslations.RemoveRange(orphan.PoiTranslations);
                        _db.Pois.Remove(orphan);
                        await _db.SaveChangesAsync(cancellationToken);
                    }
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "Cleanup orphan POI {PoiId} failed", pid);
                }
            }

            await MarkFulfillFailedAsync(order, ex.Message, cancellationToken);
        }
    }

    private async Task MarkFulfillFailedAsync(VendorPremiumOrder order, string error, CancellationToken ct)
    {
        order.Status = "fulfill_failed";
        order.LastError = error.Length > 2000 ? error[..2000] : error;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static TimeSpan? ParseOptionalTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var t) ? t : null;
    }

    private async Task<int> CreateTranslationsAndAudioAsync(Poi poi, CancellationToken cancellationToken)
    {
        var languages = await _db.Languages.OrderBy(l => l.Id).ToListAsync(cancellationToken);

        if (!languages.Any(l => NormalizeTranslateLanguageCode(l.Code) == "en"))
        {
            var enLang = new Language { Name = "English", Code = "en" };
            _db.Languages.Add(enLang);
            await _db.SaveChangesAsync(cancellationToken);
            languages.Add(enLang);
        }

        var baseScript = string.IsNullOrWhiteSpace(poi.TtsScript) ? poi.Description : poi.TtsScript!;
        var sourceLang = ResolveSourceLanguageCode(languages);

        var translatedItems = new List<PoiTranslation>();
        foreach (var lang in languages)
        {
            var item = await BuildPoiTranslationAsync(poi, baseScript, sourceLang, lang, cancellationToken);
            translatedItems.Add(item);
            await Task.Delay(500, cancellationToken);
        }

        var missingAudio = translatedItems.Count(t => string.IsNullOrWhiteSpace(t.AudioUrl));
        _db.PoiTranslations.AddRange(translatedItems);
        await _db.SaveChangesAsync(cancellationToken);
        return missingAudio;
    }

    private async Task<PoiTranslation> BuildPoiTranslationAsync(
        Poi poi,
        string baseScript,
        string sourceLang,
        Language lang,
        CancellationToken cancellationToken)
    {
        var targetLang = NormalizeTranslateLanguageCode(lang.Code);
        var title = await TranslateIfNeededAsync(poi.Name, sourceLang, targetLang, cancellationToken);
        var desc = await TranslateIfNeededAsync(poi.Description, sourceLang, targetLang, cancellationToken);
        var localizedScript = await TranslateIfNeededAsync(baseScript, sourceLang, targetLang, cancellationToken);
        var audioUrl = await _voiceService.GenerateAndUploadAudio(localizedScript, ResolveTtsVoiceLanguageCode(lang.Code));

        return new PoiTranslation
        {
            PoiId = poi.Id,
            LanguageId = lang.Id,
            Title = title,
            Description = desc,
            TtsScript = localizedScript,
            AudioUrl = audioUrl
        };
    }

    private static string ResolveSourceLanguageCode(IReadOnlyList<Language> languages)
    {
        if (languages.Count == 0) return "vi";
        var vi = languages.FirstOrDefault(l => NormalizeTranslateLanguageCode(l.Code) == "vi");
        if (vi != null) return "vi";
        return NormalizeTranslateLanguageCode(languages[0].Code);
    }

    private static string NormalizeTranslateLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "en";
        var normalized = code.Trim().ToLowerInvariant();
        var dashIndex = normalized.IndexOf('-');
        return dashIndex > 0 ? normalized[..dashIndex] : normalized;
    }

    private static string ResolveTtsVoiceLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return "en-US";

        var raw = languageCode.Trim();
        if (raw.Contains('-', StringComparison.Ordinal))
        {
            var parts = raw.Split('-', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Length >= 2)
                return $"{parts[0].ToLowerInvariant()}-{parts[1].ToUpperInvariant()}";
        }

        var normalized = raw.ToLowerInvariant();
        return normalized switch
        {
            "vi" => "vi-VN",
            "en" => "en-US",
            "fr" => "fr-FR",
            "ja" => "ja-JP",
            "ko" => "ko-KR",
            "zh" => "cmn-CN",
            _ => normalized.Length == 2 ? $"{normalized}-{normalized.ToUpperInvariant()}" : "en-US"
        };
    }

    private static Task<string> TranslateIfNeededAsync(string text, string sourceLang, string targetLang, CancellationToken _)
    {
        if (string.IsNullOrEmpty(text)) return Task.FromResult(text);
        if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(text);
        return AutoTranslateAsync(text, sourceLang, targetLang);
    }

    private static async Task<string> AutoTranslateAsync(string text, string sourceLang, string targetLang)
    {
        if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
            return text;
        try
        {
            var res = await Http.GetStringAsync(
                $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={Uri.EscapeDataString(sourceLang)}&tl={Uri.EscapeDataString(targetLang)}&dt=t&q={Uri.EscapeDataString(text)}");
            using var doc = JsonDocument.Parse(res);
            return doc.RootElement[0][0][0].GetString() ?? text;
        }
        catch
        {
            return text;
        }
    }
}
