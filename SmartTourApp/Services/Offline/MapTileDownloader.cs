using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SmartTourApp.Services.Offline;

namespace SmartTourApp.Services.Offline
{
    /// <summary>
    /// MapTileDownloader — Background worker tải trước tiles cho khu vực tour.
    ///
    /// ╔══════════════════════════════════════════════════════════════════╗
    /// ║ CHIẾN LƯỢC TẢI TRƯỚC (Proactive Area Loading)                  ║
    /// ║ - Zoom 14-18: đủ thấy đường đi rõ ràng                        ║
    /// ║ - Batch 5 tiles song song, 100ms delay giữa batch              ║
    /// ║   (tránh rate-limit OSM, tuân thủ usage policy)                ║
    /// ║ - Bỏ qua tile đã có trong cache                                ║
    /// ║ - Report progress qua event OnProgress                         ║
    /// ║ - Có thể cancel bất cứ lúc nào                                 ║
    /// ╚══════════════════════════════════════════════════════════════════╝
    /// </summary>
    public class MapTileDownloader
    {
        private readonly PersistentTileCache _cache;
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        // Giới hạn concurrent requests để không bị OSM ban
        private const int MaxConcurrentRequests = 4;
        private const int BatchDelayMs = 150;
        private const int MinZoom = 14;
        private const int MaxZoom = 18;

        public event Action<DownloadProgress>? OnProgress;
        public bool IsDownloading { get; private set; }

        public MapTileDownloader(PersistentTileCache cache)
        {
            _cache = cache;
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: DOWNLOAD AREA
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Tải trước toàn bộ tiles trong khu vực tròn.
        /// Gọi khi user nhấn "Bắt đầu chuyến đi" hoặc "Tải bản đồ Offline".
        /// </summary>
        /// <param name="centerLat">Vĩ độ tâm khu vực</param>
        /// <param name="centerLng">Kinh độ tâm khu vực</param>
        /// <param name="radiusKm">Bán kính khu vực (km), khuyến nghị 2-5km</param>
        /// <param name="minZoom">Zoom tối thiểu (mặc định 14)</param>
        /// <param name="maxZoom">Zoom tối đa (mặc định 18)</param>
        /// <param name="token">Cancellation token</param>
        public async Task DownloadAreaAsync(
            double centerLat, double centerLng,
            double radiusKm = 2.5,
            int minZoom = MinZoom,
            int maxZoom = MaxZoom,
            CancellationToken token = default)
        {
            if (IsDownloading)
            {
                System.Diagnostics.Debug.WriteLine("[TileDownloader] Already downloading");
                return;
            }

            bool isOnline = IsConnected();
            if (!isOnline)
            {
                ReportProgress(0, 0, 0, "Không có kết nối mạng — không thể tải bản đồ");
                return;
            }

            IsDownloading = true;

            try
            {
                var tiles = TileAreaCalculator.GetTilesInArea(
                    centerLat, centerLng, radiusKm, minZoom, maxZoom);

                // Lọc bỏ tiles đã có trong cache
                var toDownload = tiles
                    .Where(t => !_cache.HasTile(t.Z, t.X, t.Y))
                    .ToList();

                int total = toDownload.Count;
                int cached = tiles.Count - total;
                int done = 0;
                int failed = 0;

                System.Diagnostics.Debug.WriteLine(
                    $"[TileDownloader] Plan: {total} new tiles " +
                    $"({cached} already cached), " +
                    $"area: {radiusKm}km @ zoom {minZoom}-{maxZoom}");

                if (total == 0)
                {
                    ReportProgress(tiles.Count, tiles.Count, 0,
                        $"✅ Bản đồ đã sẵn sàng offline ({tiles.Count} tiles)");
                    return;
                }

                ReportProgress(0, total, 0,
                    $"Đang tải {total} tiles bản đồ...");

                // Chia thành batches để tải song song theo OSM policy
                var semaphore = new SemaphoreSlim(MaxConcurrentRequests);
                var tasks = new List<Task>();

                foreach (var batch in ChunkBy(toDownload, MaxConcurrentRequests))
                {
                    if (token.IsCancellationRequested) break;

                    var batchTasks = batch.Select(async t =>
                    {
                        await semaphore.WaitAsync(token);
                        try
                        {
                            bool ok = await DownloadTileAsync(t.Z, t.X, t.Y, token);
                            Interlocked.Add(ref done, 1);
                            if (!ok) Interlocked.Add(ref failed, 1);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    await Task.WhenAll(batchTasks);

                    // Progress report sau mỗi batch
                    ReportProgress(done, total, failed,
                        $"Đang tải... {done}/{total} tiles");

                    // Delay nhẹ giữa batch để không bị rate-limit
                    if (!token.IsCancellationRequested)
                        await Task.Delay(BatchDelayMs, token)
                            .ContinueWith(_ => { });
                }

                // Dọn dẹp cache nếu quá to
                _ = Task.Run(() => _cache.TrimCacheIfNeeded());

                var msg = token.IsCancellationRequested
                    ? $"Đã tạm dừng ({done}/{total} tiles)"
                    : $"✅ Tải xong! {done} tiles mới, {cached} tiles đã có";

                ReportProgress(done, total, failed, msg);

                System.Diagnostics.Debug.WriteLine(
                    $"[TileDownloader] Done: {done}/{total} new, " +
                    $"{failed} failed, {cached} pre-cached");
            }
            catch (OperationCanceledException)
            {
                ReportProgress(0, 0, 0, "Đã hủy tải bản đồ");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[TileDownloader] Error: {ex.Message}");
                ReportProgress(0, 0, 0, $"Lỗi: {ex.Message}");
            }
            finally
            {
                IsDownloading = false;
            }
        }

        /// <summary>
        /// Tải tiles cho nhiều điểm POI cùng lúc (dùng cho tour nhiều điểm).
        /// </summary>
        public async Task DownloadMultipleAreasAsync(
            List<(double Lat, double Lng)> centers,
            double radiusKmEach = 1.0,
            CancellationToken token = default)
        {
            int idx = 0;
            foreach (var (lat, lng) in centers)
            {
                if (token.IsCancellationRequested) break;

                ReportProgress(idx, centers.Count, 0,
                    $"Đang tải khu vực {idx + 1}/{centers.Count}...");

                await DownloadAreaAsync(lat, lng, radiusKmEach,
                    token: token);
                idx++;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // ESTIMATE HELPERS (gọi trước khi tải để show thông tin)
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Ước tính số tile và dung lượng cần tải (để show dialog xác nhận).
        /// </summary>
        public (int TileCount, long SizeBytes, int AlreadyCached)
            EstimateDownload(double centerLat, double centerLng,
                double radiusKm, int minZoom = MinZoom, int maxZoom = MaxZoom)
        {
            var tiles = TileAreaCalculator.GetTilesInArea(
                centerLat, centerLng, radiusKm, minZoom, maxZoom);
            int alreadyCached = tiles.Count(t => _cache.HasTile(t.Z, t.X, t.Y));
            int newTiles = tiles.Count - alreadyCached;
            long sizeBytes = TileAreaCalculator.EstimateSizeBytes(newTiles);
            return (newTiles, sizeBytes, alreadyCached);
        }

        // ══════════════════════════════════════════════════════════════
        // PRIVATE HELPERS
        // ══════════════════════════════════════════════════════════════

        private async Task<bool> DownloadTileAsync(int z, int x, int y,
            CancellationToken token)
        {
            try
            {
                string sub = ((x + y) % 3) switch { 0 => "a", 1 => "b", _ => "c" };
                string url = $"https://{sub}.tile.openstreetmap.org/{z}/{x}/{y}.png";

                using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.TryAddWithoutValidation("User-Agent",
                    "SmartTourApp/1.0 (contact@smarttour.vn)");

                var resp = await _httpClient.SendAsync(req, token);
                if (!resp.IsSuccessStatusCode) return false;

                var data = await resp.Content.ReadAsByteArrayAsync(token);
                if (data.Length > 0)
                {
                    _cache.SaveTile(z, x, y, data);
                    return true;
                }
                return false;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[TileDownloader] Tile {z}/{x}/{y} failed: {ex.Message}");
                return false;
            }
        }

        private static IEnumerable<List<T>> ChunkBy<T>(List<T> list, int size)
        {
            for (int i = 0; i < list.Count; i += size)
                yield return list.GetRange(i, Math.Min(size, list.Count - i));
        }

        private void ReportProgress(int done, int total, int failed, string message)
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                    OnProgress?.Invoke(new DownloadProgress
                    {
                        Done = done,
                        Total = total,
                        Failed = failed,
                        Message = message,
                        Percent = total > 0 ? (double)done / total : 0
                    }));
            }
            catch { }
        }

        private static bool IsConnected()
        {
            try
            {
                var a = Connectivity.Current.NetworkAccess;
                return a == NetworkAccess.Internet ||
                       a == NetworkAccess.ConstrainedInternet;
            }
            catch { return false; }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // PROGRESS DTO
    // ══════════════════════════════════════════════════════════════════

    public class DownloadProgress
    {
        public int Done { get; set; }
        public int Total { get; set; }
        public int Failed { get; set; }
        public double Percent { get; set; }
        public string Message { get; set; } = "";
        public bool IsComplete => Total > 0 && Done >= Total;
    }
}
