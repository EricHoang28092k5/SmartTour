using BruTile;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui.Tiling.Layers;
using System.Net.Http;

namespace SmartTourApp.Services.Offline
{
    // Lớp này dùng để tạo Layer cho MapPage gọi
    public static class OfflineOpenStreetMap
    {
        public static TileLayer CreateOfflineTileLayer(PersistentTileCache cache)
        {
            // Cấu hình chuẩn OSM
            var schema = new GlobalSphericalMercator(name: "OSM", yAxis: YAxis.OSM, minZoomLevel: 0, maxZoomLevel: 19);

            // Dùng OfflineTileRequest làm nguồn cấp dữ liệu
            var tileSource = new HttpTileSource(schema, new OfflineTileRequest(cache));

            return new TileLayer(tileSource)
            {
                Name = "SmartTour Offline Map"
            };
        }
    }

    // Đây là "trái tim" xử lý logic Offline cho từng ô gạch bản đồ
    public class OfflineTileRequest : IRequest
    {
        private readonly PersistentTileCache _cache;
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly byte[] EmptyPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

        public OfflineTileRequest(PersistentTileCache cache)
        {
            _cache = cache;
        }

        // Interface mới của BruTile yêu cầu hàm này thay vì GetUri
        public Uri GetUrl(TileInfo info)
        {
            int z = info.Index.Level;
            int x = info.Index.Col;
            int y = info.Index.Row;
            // Xoay vòng server a, b, c để né bị chặn IP
            string sub = ((x + y) % 3) switch { 0 => "a", 1 => "b", _ => "c" };
            return new Uri($"https://{sub}.tile.openstreetmap.org/{z}/{x}/{y}.png");
        }

        public async Task<byte[]> GetAsync(TileInfo info)
        {
            int z = info.Index.Level;
            int x = info.Index.Col;
            int y = info.Index.Row;

            // 1. Kiểm tra túi (Disk Cache) trước
            var cached = _cache.GetTile(z, x, y);
            if (cached != null) return cached;

            // 2. Nếu không có trong túi thì đi xin (Network)
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, GetUrl(info));
                    // Gửi "Căn cước công dân" App để OSM không chặn
                    req.Headers.TryAddWithoutValidation("User-Agent", "SmartTourApp/1.0 (contact@smarttour.vn)");

                    var resp = await _http.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        var data = await resp.Content.ReadAsByteArrayAsync();
                        // Tiện tay cất vào túi luôn để lần sau dùng offline
                        _cache.SaveTile(z, x, y, data);
                        return data;
                    }
                }
                catch { }
            }

            // 3. Nếu mất mạng và không có trong túi -> Dùng ảnh phóng to từ Zoom thấp hơn
            var fallback = _cache.GetFallbackTile(z, x, y);
            if (fallback.HasValue) return fallback.Value.Data;

            // 4. Cuối cùng mới trả về ảnh trống (không để App bị crash)
            return EmptyPng;
        }
    }
}