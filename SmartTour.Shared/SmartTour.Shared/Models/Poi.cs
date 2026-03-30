using SQLite;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartTour.Shared.Models;

public class Poi
{
    [PrimaryKey]
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public double Lat { get; set; }

    public double Lng { get; set; }

    public int Radius { get; set; } // Bán kính kích hoạt (mét)

    public string Description { get; set; } = "";

    public string AudioUrl { get; set; } = "";

    public string ImageUrl { get; set; } = "";

    // --- CÁC THUỘC TÍNH BỔ SUNG CHO ĐỒ ÁN XỊN HƠN ---

    public int Priority { get; set; } = 1; // Độ ưu tiên (để giải quyết khi 2 POI đè lên nhau)

    public string? TtsScript { get; set; } // Nội dung để đọc TTS nếu không có file Audio

    public bool IsActive { get; set; } = true;
    [Ignore]
    public bool IsNearest { get; set; }
    public string? CreatedBy { get; set; }
    public string? VendorId { get; set; }
    // Danh sách các ảnh phụ (Gallery)
    [Ignore]
    public ICollection<PoiImage>? PoiImages { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [Ignore]
    public virtual ICollection<AudioFile> AudioFiles { get; set; } = new List<AudioFile>();
    [Ignore]
    // Danh sách các món ăn của địa điểm này (Menu)
    public ICollection<Food>? Foods { get; set; }
    // Thuộc tính này dùng để map với PostGIS trong Database
    // [NotMapped] có nghĩa là App di động sẽ không cần quan tâm đến nó, 
    // chỉ Backend dùng để tính toán không gian.
    [Ignore]
    [NotMapped]
    public object? GeoLocation { get; set; }
    public TimeSpan? OpenTime { get; set; }
    public TimeSpan? CloseTime { get; set; }
}