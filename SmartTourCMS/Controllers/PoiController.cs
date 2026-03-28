using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourBackend.Data;
using System.Net.Http;
using X.PagedList;
using X.PagedList.Extensions; // Nhớ thêm dòng này lên trên cùng nhé
namespace SmartTourCMS.Controllers
{
    // 1. Mở cửa cho cả Admin và Vendor vào quản lý
    [Authorize(Roles = "Admin,Vendor")]
    public class PoiController : Controller
    {
        private readonly AppDbContext _context;
        private readonly Cloudinary _cloudinary;
        private readonly UserManager<IdentityUser> _userManager;
        private static readonly HttpClient _httpClient = new HttpClient();

        public PoiController(AppDbContext context, Cloudinary cloudinary, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _cloudinary = cloudinary;
            _userManager = userManager;
        }

        // --- 1. DANH SÁCH ĐỊA ĐIỂM (Lọc theo quyền) ---
        public async Task<IActionResult> Index(int? page)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            var query = _context.Pois.Include(p => p.AudioFiles).AsQueryable();

            if (!isAdmin)
            {
                // Vendor: Chỉ lấy hàng của mình
                query = query.Where(p => p.VendorId == user.Id);
            }

            // --- PHẦN CODE MỚI THÊM VÀO ĐỂ PHÂN TRANG ---
            // 1. Bắt buộc phải sắp xếp dữ liệu trước khi cắt trang (ví dụ sắp xếp Id mới nhất lên đầu)
            query = query.OrderByDescending(p => p.Id);

            // 2. Cấu hình trang
            int pageSize = 10; // Số địa điểm hiển thị trên mỗi trang (bạn có thể đổi số này)
            int pageNumber = page ?? 1; // Nếu không có tham số page thì mặc định là trang 1

            // 3. Thay thế await query.ToListAsync() bằng ToPagedList()
            var pagedPois = query.ToPagedList(pageNumber, pageSize);
            // -------------------------------------------

            var users = _userManager.Users.ToList();
            var userDict = users.ToDictionary(u => u.Id, u => u.Email);

            ViewBag.VendorDict = userDict;
            ViewBag.IsAdmin = isAdmin;

            // Trả về biến pagedPois thay vì pois
            return View(pagedPois);
        }

        // --- 2. TẠO MỚI (GET) ---
        public IActionResult Create() => View();

        // --- 3. TẠO MỚI (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Poi poi, IFormFile imageFile, List<IFormFile> galleryFiles)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null) poi.VendorId = user.Id;

            // 1. Upload ảnh bìa
            if (imageFile != null && imageFile.Length > 0)
            {
                var uploadParams = new ImageUploadParams()
                {
                    File = new FileDescription(imageFile.FileName, imageFile.OpenReadStream()),
                    Folder = "SmartTour/Pois"
                };
                var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                poi.ImageUrl = uploadResult.SecureUrl.ToString();
            }

            // 2. Lưu POI gốc để lấy cái ID trước
            _context.Pois.Add(poi);
            await _context.SaveChangesAsync();

            // 3. VÒNG LẶP UPLOAD ALBUM ẢNH PHỤ LÊN CLOUDINARY
            if (galleryFiles != null && galleryFiles.Count > 0)
            {
                foreach (var file in galleryFiles)
                {
                    if (file.Length > 0)
                    {
                        var uploadParams = new ImageUploadParams()
                        {
                            File = new FileDescription(file.FileName, file.OpenReadStream()),
                            Folder = "SmartTour/PoiGalleries"
                        };
                        var uploadResult = await _cloudinary.UploadAsync(uploadParams);

                        var poiImage = new PoiImage
                        {
                            PoiId = poi.Id,
                            ImageUrl = uploadResult.SecureUrl.ToString()
                        };
                        _context.PoiImages.Add(poiImage);
                    }
                }
                await _context.SaveChangesAsync();
            }

            // 4. TỰ ĐỘNG DỊCH ĐA NGÔN NGỮ
            try
            {
                var allLanguages = await _context.Languages.ToListAsync();
                foreach (var lang in allLanguages)
                {
                    string translatedTitle, translatedDescription, translatedTts;

                    string baseDescription = $"Chào mừng bạn đến với {poi.Name}. Đây là một địa điểm du lịch tuyệt vời.";
                    string baseTts = $"Chào mừng bạn đến với {poi.Name}. Chúc bạn có một chuyến tham quan vui vẻ!";

                    if (lang.Code.ToLower() == "vi")
                    {
                        translatedTitle = poi.Name;
                        translatedDescription = baseDescription;
                        translatedTts = baseTts;
                    }
                    else
                    {
                        translatedTitle = await AutoTranslateAsync(poi.Name, lang.Code.ToLower());
                        translatedDescription = await AutoTranslateAsync(baseDescription, lang.Code.ToLower());
                        translatedTts = await AutoTranslateAsync(baseTts, lang.Code.ToLower());
                    }

                    var translation = new PoiTranslation
                    {
                        PoiId = poi.Id,
                        LanguageId = lang.Id,
                        Title = translatedTitle,
                        Description = translatedDescription,
                        TtsScript = translatedTts
                    };
                    _context.PoiTranslations.Add(translation);
                }
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi dịch đa ngôn ngữ: " + ex.Message);
            }

            return RedirectToAction(nameof(Index));
        }

        // Hàm hỗ trợ dịch Google
        private async Task<string> AutoTranslateAsync(string text, string targetLang)
        {
            try
            {
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=vi&tl={targetLang}&dt=t&q={Uri.EscapeDataString(text)}";
                var response = await _httpClient.GetStringAsync(url);
                var parts = response.Split('"');
                return parts.Length > 1 ? parts[1] : text;
            }
            catch
            {
                return text;
            }
        }

        // --- 5. SỬA ĐỊA ĐIỂM (POST) ---
        // --- 4. SỬA ĐỊA ĐIỂM (GET) ---
        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            // BƠM THÊM Include(p => p.PoiImages) ĐỂ LẤY ALBUM ẢNH CŨ RA
            var poi = await _context.Pois
                .Include(p => p.PoiImages)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (poi == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && poi.VendorId != user.Id)
            {
                return Forbid();
            }

            return View(poi);
        }

        // --- 5. SỬA ĐỊA ĐIỂM (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        // Thêm tham số newGalleryFiles để hứng ảnh mới up thêm
        public async Task<IActionResult> Edit(int id, Poi poi, IFormFile? imageFile, List<IFormFile>? newGalleryFiles)
        {
            if (id != poi.Id) return NotFound();

            var existingPoi = await _context.Pois.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (existingPoi == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && existingPoi.VendorId != user.Id)
            {
                return Forbid();
            }

            // 1. Cập nhật ảnh bìa chính (như cũ)
            if (imageFile != null && imageFile.Length > 0)
            {
                var uploadParams = new ImageUploadParams()
                {
                    File = new FileDescription(imageFile.FileName, imageFile.OpenReadStream()),
                    Folder = "SmartTour/Pois"
                };
                var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                poi.ImageUrl = uploadResult.SecureUrl.ToString();
            }
            else
            {
                poi.ImageUrl = existingPoi.ImageUrl;
            }

            try
            {
                poi.VendorId = existingPoi.VendorId;
                _context.Update(poi);
                await _context.SaveChangesAsync();

                // 2. NẾU CÓ UP THÊM ẢNH MỚI VÀO ALBUM THÌ CHẠY VÒNG LẶP UP TIẾP
                if (newGalleryFiles != null && newGalleryFiles.Count > 0)
                {
                    foreach (var file in newGalleryFiles)
                    {
                        if (file.Length > 0)
                        {
                            var uploadParams = new ImageUploadParams()
                            {
                                File = new FileDescription(file.FileName, file.OpenReadStream()),
                                Folder = "SmartTour/PoiGalleries"
                            };
                            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

                            var poiImage = new PoiImage
                            {
                                PoiId = poi.Id,
                                ImageUrl = uploadResult.SecureUrl.ToString()
                            };
                            _context.PoiImages.Add(poiImage);
                        }
                    }
                    await _context.SaveChangesAsync();
                }

                TempData["success"] = "Cập nhật địa điểm và Album thành công!";
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Lỗi: " + ex.Message);
                return View(poi);
            }

            return RedirectToAction(nameof(Index));
        }

        // --- 5.5. HÀM MỚI: XÓA LẺ 1 ẢNH TRONG ALBUM ---
        [HttpPost]
        public async Task<IActionResult> DeleteGalleryImage(int imageId)
        {
            var image = await _context.PoiImages.Include(i => i.Poi).FirstOrDefaultAsync(i => i.Id == imageId);
            if (image == null) return NotFound();

            // Bảo mật: Chủ nhà mới được xóa
            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && image.Poi.VendorId != user.Id)
            {
                return Forbid();
            }

            // Xóa khỏi DB
            _context.PoiImages.Remove(image);
            await _context.SaveChangesAsync();

            TempData["success"] = "Đã xóa 1 ảnh khỏi Album!";
            // Xóa xong thì load lại trang Edit của chính cái POI đó
            return RedirectToAction(nameof(Edit), new { id = image.PoiId });
        }

        // --- 6. XÓA ĐỊA ĐIỂM (Đã cập nhật nhổ cỏ cả PoiImages) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            // Bơm thêm Include để lôi đầu đám con cái (Audio và Gallery) lên
            var poi = await _context.Pois
                .Include(p => p.AudioFiles)
                .Include(p => p.PoiImages) // Kéo theo mảng album ảnh phụ
                .FirstOrDefaultAsync(p => p.Id == id);

            if (poi == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(user, "Admin") && poi.VendorId != user.Id)
            {
                return Forbid();
            }

            try
            {
                // BƯỚC 1: Xóa sạch các Bản dịch
                var translations = _context.PoiTranslations.Where(t => t.PoiId == id);
                _context.PoiTranslations.RemoveRange(translations);

                // BƯỚC 2: Xóa sạch các File âm thanh
                if (poi.AudioFiles != null && poi.AudioFiles.Any())
                {
                    _context.AudioFiles.RemoveRange(poi.AudioFiles);
                }

                // BƯỚC 3: Xóa sạch Album ảnh phụ (Dọn rác triệt để)
                if (poi.PoiImages != null && poi.PoiImages.Any())
                {
                    _context.PoiImages.RemoveRange(poi.PoiImages);
                }

                // BƯỚC 4: Cuối cùng mới "trảm" thằng POI gốc
                _context.Pois.Remove(poi);

                await _context.SaveChangesAsync();
                TempData["success"] = "Đã dọn dẹp sạch sẽ và xóa địa điểm thành công!";
            }
            catch (Exception ex)
            {
                TempData["error"] = "Lỗi khi xóa: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // --- 7. ADMIN GIAO ĐỊA ĐIỂM CHO VENDOR ---
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignToVendor(int poiId, string vendorEmail)
        {
            var poi = await _context.Pois.FindAsync(poiId);
            var vendorUser = await _userManager.FindByEmailAsync(vendorEmail);

            if (poi != null && vendorUser != null)
            {
                poi.VendorId = vendorUser.Id;
                await _context.SaveChangesAsync();
                TempData["success"] = $"Đã gán địa điểm cho {vendorEmail} quản lý!";
            }
            else
            {
                TempData["error"] = "Không tìm thấy địa điểm hoặc Email Vendor không tồn tại trong hệ thống!";
            }
            return RedirectToAction("Index");
        }
    }
}