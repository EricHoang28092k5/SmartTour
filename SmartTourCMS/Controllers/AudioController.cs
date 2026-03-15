using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
// Nhớ đổi tên Namespace cho đúng project của cu
using SmartTourBackend.Data;
using SmartTour.Shared.Models;

public class AudioController : Controller
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _hostEnvironment;

    public AudioController(AppDbContext context, IWebHostEnvironment hostEnvironment)
    {
        _context = context;
        _hostEnvironment = hostEnvironment;
    }

    // Trang Upload
    public IActionResult Upload()
    {
        // Lấy danh sách POI để đổ vào Dropdown cho cu chọn
        ViewBag.Pois = new SelectList(_context.Pois, "Id", "Name");
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Upload(int poiId, IFormFile audioFile)
    {
        if (audioFile != null && audioFile.Length > 0)
        {
            // 1. Tạo thư mục lưu file nếu chưa có
            string uploadsFolder = Path.Combine(_hostEnvironment.WebRootPath, "uploads/audio");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            // 2. Tạo tên file duy nhất để không bị trùng
            string fileName = Guid.NewGuid().ToString() + "_" + audioFile.FileName;
            string filePath = Path.Combine(uploadsFolder, fileName);

            // 3. Lưu file vật lý vào máy
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await audioFile.CopyToAsync(fileStream);
            }

            // 4. Lưu thông tin vào bảng AudioFiles (Bảng cu đã có sẵn)
            var audioEntry = new AudioFile
            {
                PoiId = poiId,
                FileUrl = "/uploads/audio/" + fileName
            };
            _context.AudioFiles.Add(audioEntry);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", "Poi"); // Xong thì về trang danh sách
        }
        return View();
    }
}