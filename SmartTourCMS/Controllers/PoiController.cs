using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SmartTour.Shared.Models;
using SmartTourAPI.Data;
using SmartTourAPI.Services;
using SmartTourCMS.Models;
using System.Globalization; // Bắt buộc phải có cho vụ lột dấu tiếng Việt
using System.Text;
using System.Text.Json;
using X.PagedList.Extensions;

namespace SmartTourCMS.Controllers
{
    [Authorize(Roles = "Admin,Vendor")]
    /// <summary>
    /// Quản lý POI trên CMS:
    /// - CRUD POI có phân quyền Admin/Vendor
    /// - Vendor: tạo POI qua bước xác nhận, phí cố định (cấu hình); premium qua ví
    /// - Tạo lại translation/audio và xử lý script
    /// </summary>
    public class PoiController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Cloudinary _cloudinary;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IVoiceService _voiceService;
        private readonly VendorWalletService _vendorWallet;
        private readonly IConfiguration _configuration;
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string VendorPoiDraftSessionKey = "VendorPoiCreateDraftV1";
        private const int MaxCreateTtsScriptChars = 255;

        private static long GetFixedVendorPoiCreateChargeVnd(IConfiguration configuration) =>
            long.TryParse(configuration["PoiCreation:FixedVendorCreateChargeVnd"], out var v) && v > 0 ? v : 100_000L;

        /// <summary>TTS khi để trống: "Chào mừng bạn đến với " + tên POI.</summary>
        private static string ResolveCreateTtsScript(string? ttsScript, string poiName)
        {
            if (!string.IsNullOrWhiteSpace(ttsScript))
                return ttsScript.Trim();
            return $"Chào mừng bạn đến với {(poiName ?? string.Empty).Trim()}";
        }

        public PoiController(
            AppDbContext context,
            Cloudinary cloudinary,
            UserManager<IdentityUser> userManager,
            IVoiceService voiceService,
            VendorWalletService vendorWallet,
            IConfiguration configuration)
        {
            _context = context;
            _cloudinary = cloudinary;
            _userManager = userManager;
            _voiceService = voiceService;
            _vendorWallet = vendorWallet;
            _configuration = configuration;
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

            // Chủ động ToList trước vì RemoveDiacritics không translate được sang SQL.
            var poiList = await query.OrderByDescending(p => p.Id).ToListAsync();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = RemoveDiacritics(search.Trim().ToLower());

                poiList = poiList.Where(p =>
                    RemoveDiacritics(p.Name.ToLower()).Contains(keyword) ||
                    (p.Description != null && RemoveDiacritics(p.Description.ToLower()).Contains(keyword))
                ).ToList();
            }

            //ViewBag.CountPoi = poiList.Count;
            ViewBag.RejectedPoiCount = !isAdmin
                ? poiList.Count(p => string.Equals(p.ApprovalStatus, "rejected", StringComparison.OrdinalIgnoreCase))
                : 0;

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
        public IActionResult Create()
        {
            var minTop = long.TryParse(_configuration["PoiCreation:MinimumWalletTopUpVnd"], out var m) && m > 0 ? m : 20_000L;
            ViewBag.MinWalletTopUpVnd = minTop;
            ViewBag.FixedVendorPoiCreateChargeVnd = GetFixedVendorPoiCreateChargeVnd(_configuration);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Poi poi, IFormFile imageFile, List<IFormFile> galleryFiles)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null) poi.VendorId = user.Id;
            var isAdmin = user != null && await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin && user == null) return RedirectToAction("Login", "Account");

            poi.ApprovalStatus = "approved";
            poi.IsActive = true;
            poi.ApprovedAt = DateTime.UtcNow;
            poi.ApprovedByUserId = isAdmin ? user?.Id : null;
            poi.CreatedBy = user?.Email ?? user?.UserName ?? user?.Id;
            poi.ApprovalNote = null;

            // 1. Upload ảnh lên Cloudinary
            if (imageFile != null)
            {
                var res = await _cloudinary.UploadAsync(new ImageUploadParams { File = new FileDescription(imageFile.FileName, imageFile.OpenReadStream()), Folder = "SmartTour/Pois" });
                poi.ImageUrl = res.SecureUrl.ToString();
            }

            poi.Description = (poi.Description ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(poi.Description))
                ModelState.AddModelError(nameof(poi.Description), "Vui lòng nhập mô tả địa điểm.");

            var resolvedTts = ResolveCreateTtsScript(poi.TtsScript, poi.Name ?? string.Empty);
            if (resolvedTts.Length > MaxCreateTtsScriptChars)
            {
                ModelState.AddModelError(nameof(poi.TtsScript),
                    $"TTS script tối đa {MaxCreateTtsScriptChars} ký tự (hiện {resolvedTts.Length}).");
            }

            poi.TtsScript = resolvedTts;

            if (!ModelState.IsValid)
            {
                var minTopInv = long.TryParse(_configuration["PoiCreation:MinimumWalletTopUpVnd"], out var mv) && mv > 0 ? mv : 20_000L;
                ViewBag.MinWalletTopUpVnd = minTopInv;
                ViewBag.FixedVendorPoiCreateChargeVnd = GetFixedVendorPoiCreateChargeVnd(_configuration);
                return View(poi);
            }

            if (!isAdmin && user != null)
            {
                if (imageFile == null || string.IsNullOrWhiteSpace(poi.ImageUrl))
                {
                    ModelState.AddModelError(string.Empty, "Vui lòng chọn ảnh bìa cho địa điểm.");
                    ViewBag.MinWalletTopUpVnd = long.TryParse(_configuration["PoiCreation:MinimumWalletTopUpVnd"], out var m0) && m0 > 0 ? m0 : 20_000L;
                    ViewBag.FixedVendorPoiCreateChargeVnd = GetFixedVendorPoiCreateChargeVnd(_configuration);
                    return View(poi);
                }

                var chargeVnd = GetFixedVendorPoiCreateChargeVnd(_configuration);

                var draft = new VendorPoiCreateDraft
                {
                    VendorUserId = user.Id,
                    CreatedBy = poi.CreatedBy,
                    Name = poi.Name.Trim(),
                    Description = poi.Description,
                    TtsScript = poi.TtsScript,
                    Lat = poi.Lat,
                    Lng = poi.Lng,
                    Radius = poi.Radius > 0 ? poi.Radius : 100,
                    ImageUrl = poi.ImageUrl,
                    Priority = poi.Priority,
                    OpenTicks = poi.OpenTime?.Ticks,
                    CloseTicks = poi.CloseTime?.Ticks,
                    CategoryId = poi.CategoryId,
                    ChargeVnd = chargeVnd,
                    TotalTtsChars = 0
                };

                await HttpContext.Session.LoadAsync(HttpContext.RequestAborted);
                var json = JsonSerializer.Serialize(draft, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                HttpContext.Session.SetString(VendorPoiDraftSessionKey, json);
                await HttpContext.Session.CommitAsync(HttpContext.RequestAborted);

                return RedirectToAction(nameof(CreateConfirm));
            }

            _context.Pois.Add(poi);
            await _context.SaveChangesAsync();
            TempData["success"] = "POI đã tạo thành công.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> CreateConfirm()
        {
            if (!User.IsInRole("Vendor")) return RedirectToAction(nameof(Create));
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            await HttpContext.Session.LoadAsync(HttpContext.RequestAborted);
            var draft = ReadVendorPoiDraft();
            if (draft == null || !string.Equals(draft.VendorUserId, user.Id, StringComparison.Ordinal))
            {
                TempData["Error"] = "Phiên xác nhận hết hạn. Vui lòng nhập lại thông tin POI.";
                return RedirectToAction(nameof(Create));
            }

            var chargeVnd = GetFixedVendorPoiCreateChargeVnd(_configuration);
            draft.ChargeVnd = chargeVnd;
            draft.TotalTtsChars = 0;
            var json2 = JsonSerializer.Serialize(draft, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            HttpContext.Session.SetString(VendorPoiDraftSessionKey, json2);
            await HttpContext.Session.CommitAsync(HttpContext.RequestAborted);

            var balance = await _vendorWallet.GetBalanceVndAsync(user.Id);
            var minTop = long.TryParse(_configuration["PoiCreation:MinimumWalletTopUpVnd"], out var m) && m > 0 ? m : 20_000L;
            var vm = new PoiCreateConfirmViewModel
            {
                Draft = draft,
                BalanceVnd = balance,
                SufficientBalance = balance >= chargeVnd,
                MinWalletTopUpVnd = minTop
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateConfirmPost()
        {
            if (!User.IsInRole("Vendor")) return Forbid();
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            await HttpContext.Session.LoadAsync(HttpContext.RequestAborted);
            var draft = ReadVendorPoiDraft();
            if (draft == null || !string.Equals(draft.VendorUserId, user.Id, StringComparison.Ordinal))
            {
                TempData["Error"] = "Phiên hết hạn. Thử lại từ bước tạo POI.";
                return RedirectToAction(nameof(Create));
            }

            var chargeVnd = GetFixedVendorPoiCreateChargeVnd(_configuration);

            static TimeSpan? TicksToTimeSpan(long? ticks) =>
                ticks is long t ? TimeSpan.FromTicks(t) : null;

            var missingAudio = new int[1];
            var insufficient = new bool[1];
            var audioError = new System.Text.StringBuilder();

            var strategy = _context.Database.CreateExecutionStrategy();
            try
            {
                await strategy.ExecuteAsync(async () =>
                {
                    var poi = new Poi
                    {
                        Name = draft.Name,
                        Description = draft.Description,
                        TtsScript = draft.TtsScript ?? ResolveCreateTtsScript(null, draft.Name),
                        Lat = draft.Lat,
                        Lng = draft.Lng,
                        Radius = draft.Radius,
                        ImageUrl = draft.ImageUrl,
                        Priority = draft.Priority,
                        OpenTime = TicksToTimeSpan(draft.OpenTicks),
                        CloseTime = TicksToTimeSpan(draft.CloseTicks),
                        CategoryId = draft.CategoryId,
                        VendorId = draft.VendorUserId,
                        CreatedBy = draft.CreatedBy,
                        ApprovalStatus = "approved",
                        IsActive = true,
                        ApprovedAt = DateTime.UtcNow,
                        ApprovedByUserId = null,
                        ApprovalNote = null,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    await using var tx = await _context.Database.BeginTransactionAsync(HttpContext.RequestAborted);
                    if (!await _vendorWallet.TryDebitAsync(user.Id, chargeVnd, "poi_create", null, false, HttpContext.RequestAborted))
                    {
                        insufficient[0] = true;
                        await tx.RollbackAsync(HttpContext.RequestAborted);
                        return;
                    }

                    _context.Pois.Add(poi);
                    await _context.SaveChangesAsync(HttpContext.RequestAborted);
                    try
                    {
                        missingAudio[0] = await CreateTranslationsAndAudio(poi);
                        await _context.SaveChangesAsync(HttpContext.RequestAborted);
                        await tx.CommitAsync(HttpContext.RequestAborted);
                    }
                    catch (Exception ex)
                    {
                        audioError.Append(ex.Message);
                        await tx.RollbackAsync(HttpContext.RequestAborted);
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                if (audioError.Length > 0)
                {
                    TempData["Error"] = "Không tạo được bản dịch/audio: " + audioError.ToString();
                    return RedirectToAction(nameof(CreateConfirm));
                }

                TempData["Error"] = "Lỗi khi tạo POI: " + ex.Message;
                return RedirectToAction(nameof(CreateConfirm));
            }

            if (insufficient[0])
            {
                TempData["Error"] = $"Số dư ví không đủ (cần {chargeVnd:N0} VNĐ).";
                return RedirectToAction(nameof(CreateConfirm));
            }

            HttpContext.Session.Remove(VendorPoiDraftSessionKey);
            await HttpContext.Session.CommitAsync(HttpContext.RequestAborted);

            if (missingAudio[0] > 0)
                TempData["AudioWarning"] = $"Đã trừ {chargeVnd:N0} VNĐ trong ví. Còn {missingAudio[0]} bản dịch chưa có audio — thử tạo lại audio sau.";
            else
                TempData["success"] = $"POI đã tạo và hiển thị trên app. Đã trừ phí tạo POI {chargeVnd:N0} VNĐ từ ví.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateConfirmCancel()
        {
            await HttpContext.Session.LoadAsync(HttpContext.RequestAborted);
            HttpContext.Session.Remove(VendorPoiDraftSessionKey);
            await HttpContext.Session.CommitAsync(HttpContext.RequestAborted);
            return RedirectToAction(nameof(Create));
        }

        private VendorPoiCreateDraft? ReadVendorPoiDraft()
        {
            var json = HttpContext.Session.GetString(VendorPoiDraftSessionKey);
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<VendorPoiCreateDraft>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
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
                if (!isAdmin)
                {
                    poi.ApprovalStatus = "approved";
                    poi.IsActive = true;
                    poi.ApprovedAt = DateTime.UtcNow;
                    poi.ApprovedByUserId = null;
                    poi.ApprovalNote = null;
                    _context.Update(poi);
                    if (poi.Description != existing.Description ||
                        (poi.TtsScript ?? "") != (existing.TtsScript ?? ""))
                    {
                        var oldTrans = _context.PoiTranslations.Where(t => t.PoiId == id);
                        _context.PoiTranslations.RemoveRange(oldTrans);
                        var missing = await CreateTranslationsAndAudio(poi);
                        if (missing > 0)
                            TempData["AudioWarning"] = $"Đã cập nhật nội dung nhưng còn {missing} bản dịch chưa có audio.";
                    }

                    await _context.SaveChangesAsync();
                    TempData["success"] = "Đã cập nhật POI.";
                    return RedirectToAction(nameof(Index));
                }

                poi.ApprovalStatus = existing.ApprovalStatus;
                poi.ApprovedAt = existing.ApprovedAt;
                poi.ApprovedByUserId = existing.ApprovedByUserId;
                poi.ApprovalNote = existing.ApprovalNote;
                _context.Update(poi);

                // Admin sửa trực tiếp: nếu nội dung đổi, xóa bản dịch cũ để rebuild tránh lệch audio/script.
                if (poi.ApprovalStatus == "approved" && poi.Description != existing.Description)
                {
                    var oldTrans = _context.PoiTranslations.Where(t => t.PoiId == id);
                    _context.PoiTranslations.RemoveRange(oldTrans);
                    var missing = await CreateTranslationsAndAudio(poi);
                    if (missing > 0)
                        TempData["AudioWarning"] = $"Không tạo được audio cho {missing} bản dịch. Kiểm tra AZURE_SPEECH_KEY và Cloudinary.";
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex) { ModelState.AddModelError("", ex.Message); return View(poi); }
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult PendingApprovals(string? requestType = null)
        {
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApprovePoi(int id)
        {
            var poi = await _context.Pois.FirstOrDefaultAsync(p => p.Id == id);
            if (poi == null) return NotFound();
            if (poi.ApprovalStatus != "pending") return RedirectToAction(nameof(Index));

            var admin = await _userManager.GetUserAsync(User);
            poi.ApprovalStatus = "approved";
            poi.IsActive = true;
            poi.ApprovedAt = DateTime.UtcNow;
            poi.ApprovedByUserId = admin?.Id;

            var note = ParseApprovalNote(poi.ApprovalNote);
            var isEditRequest = note?.RequestType == "edit";
            var shouldRebuildTranslation = false;
            if (isEditRequest && note?.Original is not null)
            {
                shouldRebuildTranslation = !string.Equals(note.Original.Description, poi.Description, StringComparison.Ordinal) ||
                                           !string.Equals(note.Original.TtsScript, poi.TtsScript, StringComparison.Ordinal);
            }

            var existingTranslations = await _context.PoiTranslations.AnyAsync(t => t.PoiId == poi.Id);
            if (!existingTranslations || shouldRebuildTranslation)
            {
                if (shouldRebuildTranslation)
                {
                    var oldTrans = _context.PoiTranslations.Where(t => t.PoiId == poi.Id);
                    _context.PoiTranslations.RemoveRange(oldTrans);
                }
                var missingAudio = await CreateTranslationsAndAudio(poi);
                if (missingAudio > 0)
                    TempData["AudioWarning"] = $"POI được duyệt nhưng còn {missingAudio} bản dịch chưa có audio.";
            }

            poi.ApprovalNote = null;
            await _context.SaveChangesAsync();
            TempData["success"] = "Đã duyệt POI thành công.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectPoi(int id)
        {
            var poi = await _context.Pois.FirstOrDefaultAsync(p => p.Id == id);
            if (poi == null) return NotFound();
            if (poi.ApprovalStatus != "pending") return RedirectToAction(nameof(Index));

            var note = ParseApprovalNote(poi.ApprovalNote);
            if (note?.RequestType == "edit" && note.Original is not null)
            {
                // Từ chối chỉnh sửa: hoàn nguyên về bản đã duyệt trước đó.
                ApplySnapshot(poi, note.Original);
                poi.ApprovalStatus = "approved";
                poi.IsActive = true;
                poi.ApprovalNote = null;
                TempData["success"] = "Đã từ chối chỉnh sửa và hoàn nguyên POI về bản trước.";
            }
            else
            {
                poi.ApprovalStatus = "rejected";
                poi.IsActive = false;
                poi.ApprovalNote = null;
                TempData["success"] = "Đã từ chối POI.";
            }
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegenerateTranslationAudios(int poiId)
        {
            //phan quyen
            var poi = await _context.Pois.AsNoTracking().FirstOrDefaultAsync(p => p.Id == poiId);
            if (poi == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            if (!isAdmin && poi.VendorId != user.Id) return Forbid();
            //
            //Lấy toàn bộ danh sách các bản dịch của POI này.
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
                    skipped++;      //kt xem có audio chưa nếu chưa skipp
                    continue;
                }

                var script = t.TtsScript ?? t.Description; // nếu chưa có tts thì lấy script
                if (string.IsNullOrWhiteSpace(script)) continue; //lấy scritp

                var langCode = langs.TryGetValue(t.LanguageId, out var lang)
                    ? ResolveTtsVoiceLanguageCode(lang.Code)     //chuyểnn ngôn ngữ hệ thống sang dạng chuẩn
                    : "vi-VN";
                var url = await _voiceService.GenerateAndUploadAudio(script, langCode); //upload audio
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

            return RedirectToAction("Index", "Translation", new { poiId }); // quay vêf bản dịch để thấy kq
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

            var (ok, err) = await DeletePoiCoreAsync(id);
            if (ok)
                TempData["success"] = "Đã xóa POI và toàn bộ dữ liệu liên quan.";
            else if (string.Equals(err, "notfound", StringComparison.Ordinal))
                return NotFound();
            else
                TempData["Error"] = "Xóa POI thất bại: " + (err ?? "lỗi không xác định");

            return RedirectToAction(nameof(Index));
        }

        /// <summary>Vendor: xóa một lần mọi POI của mình đang ở trạng thái rejected.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAllRejectedVendorPois()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            if (await _userManager.IsInRoleAsync(user, "Admin"))
            {
                TempData["Error"] = "Thao tác này chỉ dành cho tài khoản vendor.";
                return RedirectToAction(nameof(Index));
            }

            var ids = await _context.Pois.AsNoTracking()
                .Where(p => p.VendorId == user.Id && p.ApprovalStatus == "rejected")
                .Select(p => p.Id)
                .ToListAsync();

            if (ids.Count == 0)
            {
                TempData["Error"] = "Không có POI đã từ chối để xóa.";
                return RedirectToAction(nameof(Index));
            }

            var deleted = 0;
            foreach (var id in ids)
            {
                var (ok, _) = await DeletePoiCoreAsync(id);
                if (ok) deleted++;
            }

            TempData["success"] = deleted == ids.Count
                ? $"Đã xóa {deleted} địa điểm đã từ chối."
                : $"Đã xóa {deleted}/{ids.Count} địa điểm đã từ chối (một số bản ghi không xóa được — kiểm tra ràng buộc dữ liệu).";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Xóa POI và các bản ghi phụ thuộc đã được map trong CMS (gọi sau khi kiểm tra quyền).</summary>
        private async Task<(bool ok, string? error)> DeletePoiCoreAsync(int id)
        {
            try
            {
                var poi = await _context.Pois.FirstOrDefaultAsync(p => p.Id == id);
                if (poi == null) return (false, "notfound");

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
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // --- HELPER METHODS ---

        private async Task<int> CreateTranslationsAndAudio(Poi poi)
        {
            var languages = await GetOrderedLanguagesAsync();

            // Luôn đảm bảo có EN để app có fallback đọc ổn định.
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

                // Giãn nhịp gọi translate để giảm bị chặn khi gọi dồn.
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
                // Dùng endpoint translate public (gtx), lỗi sẽ fallback text gốc để không chặn luồng tạo POI.
                var res = await _httpClient.GetStringAsync(
                    $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={Uri.EscapeDataString(sourceLang)}&tl={Uri.EscapeDataString(targetLang)}&dt=t&q={Uri.EscapeDataString(text)}");
                using var doc = JsonDocument.Parse(res);
                return doc.RootElement[0][0][0].GetString() ?? text;
            }
            catch { return text; }
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

        private static PendingPoiApprovalNote? ParseApprovalNote(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try
            {
                return JsonSerializer.Deserialize<PendingPoiApprovalNote>(raw);
            }
            catch
            {
                return null;
            }
        }

        private static PoiApprovalSnapshot SnapshotFromPoi(Poi poi)
        {
            return new PoiApprovalSnapshot
            {
                Name = poi.Name,
                Description = poi.Description,
                TtsScript = poi.TtsScript,
                Lat = poi.Lat,
                Lng = poi.Lng,
                Radius = poi.Radius,
                ImageUrl = poi.ImageUrl,
                Priority = poi.Priority,
                OpenTime = poi.OpenTime,
                CloseTime = poi.CloseTime,
                CategoryId = poi.CategoryId
            };
        }

        private static void ApplySnapshot(Poi poi, PoiApprovalSnapshot s)
        {
            poi.Name = s.Name;
            poi.Description = s.Description;
            poi.TtsScript = s.TtsScript;
            poi.Lat = s.Lat;
            poi.Lng = s.Lng;
            poi.Radius = s.Radius;
            poi.ImageUrl = s.ImageUrl;
            poi.Priority = s.Priority;
            poi.OpenTime = s.OpenTime;
            poi.CloseTime = s.CloseTime;
            poi.CategoryId = s.CategoryId;
        }

        private static List<PoiFieldChangeViewModel> BuildChangedFields(PendingPoiApprovalNote? note, Poi poi)
        {
            if (note?.RequestType != "edit" || note.Original is null) return [];
            var old = note.Original;
            var changed = new List<PoiFieldChangeViewModel>();
            AddChanged(changed, "Tên", old.Name, poi.Name);
            AddChanged(changed, "Mô tả", old.Description, poi.Description);
            AddChanged(changed, "Script", old.TtsScript ?? string.Empty, poi.TtsScript ?? string.Empty);
            AddChanged(changed, "Vĩ độ", old.Lat.ToString(CultureInfo.InvariantCulture), poi.Lat.ToString(CultureInfo.InvariantCulture));
            AddChanged(changed, "Kinh độ", old.Lng.ToString(CultureInfo.InvariantCulture), poi.Lng.ToString(CultureInfo.InvariantCulture));
            AddChanged(changed, "Bán kính", old.Radius.ToString(CultureInfo.InvariantCulture), poi.Radius.ToString(CultureInfo.InvariantCulture));
            AddChanged(changed, "Ảnh đại diện", old.ImageUrl, poi.ImageUrl);
            AddChanged(changed, "Ưu tiên", old.Priority.ToString(CultureInfo.InvariantCulture), poi.Priority.ToString(CultureInfo.InvariantCulture));
            AddChanged(changed, "Mở cửa", old.OpenTime?.ToString() ?? "", poi.OpenTime?.ToString() ?? "");
            AddChanged(changed, "Đóng cửa", old.CloseTime?.ToString() ?? "", poi.CloseTime?.ToString() ?? "");
            AddChanged(changed, "Danh mục", old.CategoryId?.ToString() ?? "", poi.CategoryId?.ToString() ?? "");
            return changed;
        }

        private static void AddChanged(List<PoiFieldChangeViewModel> list, string field, string oldValue, string newValue)
        {
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return;
            list.Add(new PoiFieldChangeViewModel
            {
                Field = field,
                OldValue = oldValue,
                NewValue = newValue
            });
        }

        private sealed class PendingPoiApprovalNote
        {
            public string RequestType { get; set; } = "create";
            public PoiApprovalSnapshot? Original { get; set; }
        }

        private sealed class PoiApprovalSnapshot
        {
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string? TtsScript { get; set; }
            public double Lat { get; set; }
            public double Lng { get; set; }
            public int Radius { get; set; }
            public string ImageUrl { get; set; } = string.Empty;
            public int Priority { get; set; }
            public TimeSpan? OpenTime { get; set; }
            public TimeSpan? CloseTime { get; set; }
            public int? CategoryId { get; set; }
        }
    }
}