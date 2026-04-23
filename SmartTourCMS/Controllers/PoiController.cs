using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;
using SmartTourBackend.Services;
using System.Globalization; // Bắt buộc phải có cho vụ lột dấu tiếng Việt
using System.Text;
using System.Text.Json;
using X.PagedList.Extensions;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    public class PoiController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Cloudinary _cloudinary;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IVoiceService _voiceService;
        private static readonly HttpClient _httpClient = new HttpClient();

        public PoiController(
            AppDbContext context,
            Cloudinary cloudinary,
            UserManager<IdentityUser> userManager,
            IVoiceService voiceService)
        {
            _context = context;
            _cloudinary = cloudinary;
            _userManager = userManager;
            _voiceService = voiceService;
        }

        // --- DANH SÁCH POI VÀ TÌM KIẾM KHÔNG DẤU ---
        public async Task<IActionResult> Index(string? search, int? page)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            var query = _context.Pois
                .Include(p => p.PoiTranslations)
                .Include(p => p.AudioFiles)
                .AsQueryable();

            if (isAdmin)
            {
                query = query.Where(p => p.ApprovalStatus != "rejected");
            }
            else
            {
                query = query.Where(p => p.VendorId == user.Id);
            }

            // Ép nó lấy dữ liệu ra List trước (để C# so sánh không dấu)
            var poiList = await query.OrderByDescending(p => p.Id).ToListAsync();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = RemoveDiacritics(search.Trim().ToLower());

                poiList = poiList.Where(p =>
                    RemoveDiacritics(p.Name.ToLower()).Contains(keyword) ||
                    (p.Description != null && RemoveDiacritics(p.Description.ToLower()).Contains(keyword))
                ).ToList();
            }

            const int pageSize = 10;
            var pageNumber = page ?? 1;
            var pagedList = poiList.ToPagedList(pageNumber, pageSize);

            ViewBag.CurrentSearch = search;
            ViewBag.IsAdmin = isAdmin;

            if (isAdmin)
            {
                var vendorIds = pagedList
                    .Where(p => !string.IsNullOrEmpty(p.VendorId))
                    .Select(p => p.VendorId!)
                    .Distinct()
                    .ToList();
                var vendorDict = new Dictionary<string, string>();
                foreach (var vid in vendorIds)
                {
                    var u = await _userManager.FindByIdAsync(vid);
                    if (u != null)
                        vendorDict[vid] = u.UserName ?? u.Email ?? vid;
                }
                ViewBag.VendorDict = vendorDict;
            }

            return View(pagedList);
        }

        [HttpGet]
        public IActionResult Create() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Poi poi, IFormFile imageFile, List<IFormFile> galleryFiles)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null) poi.VendorId = user.Id;
            var isAdmin = user != null && await _userManager.IsInRoleAsync(user, "Admin");

            poi.ApprovalStatus = isAdmin ? "approved" : "pending";
            poi.IsActive = isAdmin;
            poi.ApprovedAt = isAdmin ? DateTime.UtcNow : null;
            poi.ApprovedByUserId = isAdmin ? user?.Id : null;
            poi.CreatedBy = user?.Email ?? user?.UserName ?? user?.Id;

            // 1. Upload ảnh lên Cloudinary
            if (imageFile != null)
            {
                var res = await _cloudinary.UploadAsync(new ImageUploadParams { File = new FileDescription(imageFile.FileName, imageFile.OpenReadStream()), Folder = "SmartTour/Pois" });
                poi.ImageUrl = res.SecureUrl.ToString();
            }

            // 2. AI viết kịch bản nếu lười
            if (string.IsNullOrWhiteSpace(poi.Description))
            {
                poi.Description = await GenerateScriptWithAI(poi.Name);
            }
            poi.TtsScript = string.IsNullOrWhiteSpace(poi.TtsScript) ? poi.Description : poi.TtsScript;

            _context.Pois.Add(poi);
            await _context.SaveChangesAsync();

            // Vendor tạo POI phải chờ admin duyệt trước khi generate translation/audio.
            if (!isAdmin)
            {
                TempData["success"] = "POI đã được gửi duyệt. Admin phê duyệt xong mới hiển thị trên app.";
                return RedirectToAction(nameof(Index));
            }

            // Chỉ tạo bản dịch/audio tại bước duyệt POI.
            TempData["success"] = "POI đã tạo thành công.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var poi = await _context.Pois.Include(p => p.PoiImages).FirstOrDefaultAsync(p => p.Id == id);
            var user = await _userManager.GetUserAsync(User);
            if (poi == null) return NotFound();
            if (user == null) return RedirectToAction("Login", "Account");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin && poi.VendorId != user.Id) return Forbid();
            return View(poi);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Poi poi, IFormFile? imageFile, List<IFormFile>? newGalleryFiles)
        {
            if (id != poi.Id) return NotFound();

            var existing = await _context.Pois.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (existing == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin && existing.VendorId != user.Id) return Forbid();

            if (imageFile != null)
            {
                var res = await _cloudinary.UploadAsync(new ImageUploadParams { File = new FileDescription(imageFile.FileName, imageFile.OpenReadStream()), Folder = "SmartTour/Pois" });
                poi.ImageUrl = res.SecureUrl.ToString();
            }
            else { poi.ImageUrl = existing.ImageUrl; }

            try
            {
                poi.VendorId = existing.VendorId;
                poi.ApprovalStatus = existing.ApprovalStatus;
                poi.ApprovedAt = existing.ApprovedAt;
                poi.ApprovedByUserId = existing.ApprovedByUserId;
                _context.Update(poi);

                // Chỉ tạo/làm mới bản dịch khi POI đã được duyệt.
                if (poi.ApprovalStatus == "approved" && poi.Description != existing.Description)
                {
                    var oldTrans = _context.PoiTranslations.Where(t => t.PoiId == id);
                    _context.PoiTranslations.RemoveRange(oldTrans);
                    var missing = await CreateTranslationsAndAudio(poi);
                    if (missing > 0)
                    {
                        TempData["AudioWarning"] = $"Không tạo được audio cho {missing} bản dịch. Kiểm tra AZURE_SPEECH_KEY và Cloudinary.";
                    }
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex) { ModelState.AddModelError("", ex.Message); return View(poi); }
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> PendingApprovals()
        {
            var pending = await _context.Pois
                .Where(p => p.ApprovalStatus == "pending")
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
            return View(pending);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApprovePoi(int id)
        {
            var poi = await _context.Pois.FirstOrDefaultAsync(p => p.Id == id);
            if (poi == null) return NotFound();
            if (poi.ApprovalStatus != "pending") return RedirectToAction(nameof(PendingApprovals));

            var admin = await _userManager.GetUserAsync(User);
            poi.ApprovalStatus = "approved";
            poi.IsActive = true;
            poi.ApprovedAt = DateTime.UtcNow;
            poi.ApprovedByUserId = admin?.Id;

            var existingTranslations = await _context.PoiTranslations.AnyAsync(t => t.PoiId == poi.Id);
            if (!existingTranslations)
            {
                var missingAudio = await CreateTranslationsAndAudio(poi);
                if (missingAudio > 0)
                    TempData["AudioWarning"] = $"POI được duyệt nhưng còn {missingAudio} bản dịch chưa có audio.";
            }

            await _context.SaveChangesAsync();
            TempData["success"] = "Đã duyệt POI thành công.";
            return RedirectToAction(nameof(PendingApprovals));
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectPoi(int id)
        {
            var poi = await _context.Pois.FirstOrDefaultAsync(p => p.Id == id);
            if (poi == null) return NotFound();
            if (poi.ApprovalStatus != "pending") return RedirectToAction(nameof(PendingApprovals));

            poi.ApprovalStatus = "rejected";
            poi.IsActive = false;
            poi.ApprovalNote = null;
            await _context.SaveChangesAsync();
            TempData["success"] = "Đã từ chối POI.";
            return RedirectToAction(nameof(PendingApprovals));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegenerateTranslationAudios(int poiId)
        {
            var poi = await _context.Pois.AsNoTracking().FirstOrDefaultAsync(p => p.Id == poiId);
            if (poi == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin && poi.VendorId != user.Id) return Forbid();

            var translations = await _context.PoiTranslations
                .Where(t => t.PoiId == poiId)
                .ToListAsync();
            var langIds = translations.Select(t => t.LanguageId).Distinct().ToList();
            var langs = await _context.Languages
                .Where(l => langIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id);

            var missing = 0;
            var generated = 0;
            var skipped = 0;
            foreach (var t in translations)
            {
                if (!string.IsNullOrWhiteSpace(t.AudioUrl))
                {
                    skipped++;
                    continue;
                }

                var script = t.TtsScript ?? t.Description;
                if (string.IsNullOrWhiteSpace(script)) continue;

                var langCode = langs.TryGetValue(t.LanguageId, out var lang)
                    ? ResolveTtsVoiceLanguageCode(lang.Code)
                    : "vi-VN";
                var url = await _voiceService.GenerateAndUploadAudio(script, langCode);
                if (string.IsNullOrWhiteSpace(url)) missing++;
                else
                {
                    t.AudioUrl = url;
                    generated++;
                }
            }

            await _context.SaveChangesAsync();
            if (missing > 0)
                TempData["AudioWarning"] = $"Vẫn còn {missing} bản dịch không tạo được audio. Kiểm tra Azure Speech và Cloudinary.";
            else
                TempData["AudioSuccess"] = $"Đã tạo audio cho {generated} bản dịch thiếu. Bỏ qua {skipped} bản dịch đã có audio.";

            return RedirectToAction("Index", "Translation", new { poiId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var poi = await _context.Pois.FirstOrDefaultAsync(p => p.Id == id);
            if (poi == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin && poi.VendorId != user.Id) return Forbid();

            try
            {
                // Dọn dữ liệu liên quan (ĐÃ XÓA TOUR VÀ ROUTE THEO YÊU CẦU CỦA MÀY)
                var poiTranslations = _context.PoiTranslations.Where(x => x.PoiId == id);
                var poiImages = _context.PoiImages.Where(x => x.PoiId == id);
                var foods = _context.Food.Where(x => x.PoiId == id);
                var foodIds = await foods.Select(f => f.Id).ToListAsync();
                var foodTranslations = _context.FoodTranslations.Where(x => foodIds.Contains(x.FoodId));
                var playLogs = _context.PlayLog.Where(x => x.PoiId == id);
                var heatmapEntries = _context.HeatmapEntries.Where(x => x.PoiId == id);

                _context.FoodTranslations.RemoveRange(foodTranslations);
                _context.Food.RemoveRange(foods);
                _context.PoiTranslations.RemoveRange(poiTranslations);
                _context.PoiImages.RemoveRange(poiImages);
                _context.PlayLog.RemoveRange(playLogs);
                _context.HeatmapEntries.RemoveRange(heatmapEntries);
                _context.Pois.Remove(poi);

                await _context.SaveChangesAsync();
                TempData["success"] = "Đã xóa POI và toàn bộ dữ liệu liên quan.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Xóa POI thất bại: " + ex.Message;
            }
            return RedirectToAction(nameof(Index));
        }

        // --- HELPER METHODS ---

        private async Task<int> CreateTranslationsAndAudio(Poi poi)
        {
            var languages = await GetOrderedLanguagesAsync();

            if (!languages.Any(l => NormalizeTranslateLanguageCode(l.Code) == "en"))
            {
                var enLang = new Language { Name = "English", Code = "en" };
                _context.Languages.Add(enLang);
                await _context.SaveChangesAsync();
                languages.Add(enLang);
            }

            var baseScript = string.IsNullOrWhiteSpace(poi.TtsScript) ? poi.Description : poi.TtsScript!;
            var sourceLang = ResolveSourceLanguageCode(languages);

            var translatedItems = new List<PoiTranslation>();
            foreach (var lang in languages)
            {
                var item = await BuildPoiTranslationAsync(poi, baseScript, sourceLang, lang);
                translatedItems.Add(item);

                // Nghỉ 500ms chống Google block
                await Task.Delay(500);
            }

            var missingAudio = translatedItems.Count(t => string.IsNullOrWhiteSpace(t.AudioUrl));
            _context.PoiTranslations.AddRange(translatedItems);
            await _context.SaveChangesAsync();
            return missingAudio;
        }

        private async Task<PoiTranslation> BuildPoiTranslationAsync(Poi poi, string baseScript, string sourceLang, Language lang)
        {
            var targetLang = NormalizeTranslateLanguageCode(lang.Code);
            string title = await TranslateIfNeededAsync(poi.Name, sourceLang, targetLang);
            string desc = await TranslateIfNeededAsync(poi.Description, sourceLang, targetLang);
            string localizedScript = await TranslateIfNeededAsync(baseScript, sourceLang, targetLang);
            string audioUrl = await _voiceService.GenerateAndUploadAudio(localizedScript, ResolveTtsVoiceLanguageCode(lang.Code));

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

        private async Task<List<Language>> GetOrderedLanguagesAsync()
        {
            return await _context.Languages.OrderBy(l => l.Id).ToListAsync();
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

        private async Task<string> TranslateIfNeededAsync(string text, string sourceLang, string targetLang)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
                return text;
            return await AutoTranslateAsync(text, sourceLang, targetLang);
        }

        private async Task<string> AutoTranslateAsync(string text, string sourceLang, string targetLang)
        {
            if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
                return text;
            try
            {
                var res = await _httpClient.GetStringAsync(
                    $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={Uri.EscapeDataString(sourceLang)}&tl={Uri.EscapeDataString(targetLang)}&dt=t&q={Uri.EscapeDataString(text)}");
                using var doc = JsonDocument.Parse(res);
                return doc.RootElement[0][0][0].GetString() ?? text;
            }
            catch { return text; }
        }

        private async Task<string> GenerateScriptWithAI(string poiName)
        {
            string apiKey = Environment.GetEnvironmentVariable("GGKEYAPI");
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";
            var payload = new { contents = new[] { new { parts = new[] { new { text = $"Viết thuyết minh du lịch 150 chữ cho: {poiName}. Không tiêu đề, không dùng dấu *." } } } } };
            try
            {
                var json = JsonSerializer.Serialize(payload);
                var res = await _httpClient.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
                if (res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(body);
                    return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                }
            }
            catch { }
            return $"Chào mừng bạn đến với {poiName}.";
        }

        // HÀM LỘT DẤU TIẾNG VIỆT
        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC)
                .Replace("đ", "d").Replace("Đ", "D");
        }
    }
}