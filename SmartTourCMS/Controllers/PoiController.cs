using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;
using SmartTourBackend.Services; // Đảm bảo namespace này khớp với folder Services của mày
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

        // --- DANH SÁCH POI ---
        public async Task<IActionResult> Index(int? page)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            var query = _context.Pois
                .Include(p => p.PoiTranslations)
                .Include(p => p.AudioFiles)
                .AsQueryable();

            if (!isAdmin) query = query.Where(p => p.VendorId == user.Id);

            query = query.OrderByDescending(p => p.Id);

            const int pageSize = 10;
            var pageNumber = page ?? 1;
            var pagedList = query.ToPagedList(pageNumber, pageSize);

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

            // 1. Upload ảnh lên Cloudinary
            if (imageFile != null)
            {
                var res = await _cloudinary.UploadAsync(new ImageUploadParams { File = new FileDescription(imageFile.FileName, imageFile.OpenReadStream()), Folder = "SmartTour/Pois" });
                poi.ImageUrl = res.SecureUrl.ToString();
            }

            // 2. AI viết kịch bản nếu mày lười không nhập
            if (string.IsNullOrWhiteSpace(poi.Description))
            {
                poi.Description = await GenerateScriptWithAI(poi.Name);
            }
            // Nếu chưa nhập script riêng thì dùng luôn mô tả làm script gốc.
            poi.TtsScript = string.IsNullOrWhiteSpace(poi.TtsScript) ? poi.Description : poi.TtsScript;

            _context.Pois.Add(poi);
            await _context.SaveChangesAsync();

            // 3. Dịch đa ngôn ngữ và TẠO AUDIO — theo từng dòng trong bảng Languages (Code = langcode)
            var languages = await GetOrderedLanguagesAsync();
            var sourceLang = ResolveSourceLanguageCode(languages);
            var baseScript = poi.TtsScript!;
            var translationTasks = languages
                .Select(lang => BuildPoiTranslationAsync(poi, baseScript, sourceLang, lang))
                .ToList();
            var translatedItems = await Task.WhenAll(translationTasks);

            var missingAudio = translatedItems.Count(t => string.IsNullOrWhiteSpace(t.AudioUrl));
            _context.PoiTranslations.AddRange(translatedItems);
            await _context.SaveChangesAsync();
            if (missingAudio > 0)
            {
                TempData["AudioWarning"] =
                    $"Không tạo được audio cho {missingAudio} bản dịch. Kiểm tra: (1) AZURE_SPEECH_KEY/AZURE_SPEECH_REGION, (2) dịch vụ Azure Speech đã active, (3) Cloudinary trong .env.";
            }
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

        // --- CHỈNH SỬA POI (CẬP NHẬT AUDIO NẾU SCRIPT ĐỔI) ---
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
                _context.Update(poi);

                // Nếu Description thay đổi, xóa sạch Translation cũ để AI dịch và tạo Audio lại
                if (poi.Description != existing.Description)
                {
                    var oldTrans = _context.PoiTranslations.Where(t => t.PoiId == id);
                    _context.PoiTranslations.RemoveRange(oldTrans);
                    var missing = await CreateTranslationsAndAudio(poi);
                    if (missing > 0)
                    {
                        TempData["AudioWarning"] =
                            $"Không tạo được audio cho {missing} bản dịch. Kiểm tra AZURE_SPEECH_KEY/AZURE_SPEECH_REGION và Cloudinary.";
                    }
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex) { ModelState.AddModelError("", ex.Message); return View(poi); }
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Tạo lại audio cho mọi bản dịch của POI (khi trước đó Azure TTS/Cloudinary lỗi).
        /// </summary>
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
            foreach (var t in translations)
            {
                var script = t.TtsScript ?? t.Description;
                if (string.IsNullOrWhiteSpace(script)) continue;

                var langCode = langs.TryGetValue(t.LanguageId, out var lang)
                    ? ResolveTtsVoiceLanguageCode(lang.Code)
                    : "vi-VN";
                var url = await _voiceService.GenerateAndUploadAudio(script, langCode);
                if (string.IsNullOrWhiteSpace(url)) missing++;
                else t.AudioUrl = url;
            }

            await _context.SaveChangesAsync();
            if (missing > 0)
                TempData["AudioWarning"] = $"Vẫn còn {missing} bản dịch không tạo được audio. Kiểm tra Azure Speech và Cloudinary.";
            else
                TempData["AudioSuccess"] = "Đã tạo/cập nhật audio cho các bản dịch.";

            return RedirectToAction("Index", "Translation", new { poiId });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var poi = await _context.Pois.FirstOrDefaultAsync(p => p.Id == id);
            if (poi != null)
            {
                _context.Pois.Remove(poi);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // --- HELPER METHODS ---

        /// <returns>Số bản dịch không tạo được audio (URL rỗng).</returns>
        private async Task<int> CreateTranslationsAndAudio(Poi poi)
        {
            var languages = await GetOrderedLanguagesAsync();
            var baseScript = string.IsNullOrWhiteSpace(poi.TtsScript) ? poi.Description : poi.TtsScript!;
            var sourceLang = ResolveSourceLanguageCode(languages);
            var translationTasks = languages
                .Select(lang => BuildPoiTranslationAsync(poi, baseScript, sourceLang, lang))
                .ToList();
            var translatedItems = await Task.WhenAll(translationTasks);

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
            return await _context.Languages
                .OrderBy(l => l.Id)
                .ToListAsync();
        }

        /// <summary>
        /// Ngôn ngữ nguồn cho nội dung POI: ưu tiên mã "vi" nếu có trong bảng Languages, không thì lấy bản ghi Id nhỏ nhất.
        /// </summary>
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

        /// <summary>
        /// Chuẩn hóa mã ngôn ngữ theo BCP-47 (vd: vi-VN) để gửi qua service TTS.
        /// </summary>
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
    }
}