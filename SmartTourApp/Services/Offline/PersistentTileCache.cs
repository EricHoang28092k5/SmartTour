using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SmartTourApp.Services.Offline
{
    /// <summary>
    /// PersistentTileCache — Lưu trữ bền vững các tile bản đồ xuống bộ nhớ vật lý.
    ///
    /// Cấu trúc thư mục: AppData/MapCache/{z}/{x}/{y}.png
    /// Chiến lược: "Thấy một lần, nhớ mãi mãi"
    ///   1. Ghi tile khi tải từ OSM (có mạng)
    ///   2. Đọc tile từ disk khi offline (không cần mạng)
    ///   3. Upscale tile từ zoom thấp hơn nếu tile hiện tại chưa có (Fallback)
    /// </summary>
    public class PersistentTileCache
    {
        private readonly string _cacheRoot;
        private readonly long _maxCacheSizeBytes;
        private static readonly SemaphoreSlim _writeLock = new(1, 1);

        // Mặc định giới hạn 500MB cache
        public PersistentTileCache(long maxCacheSizeBytes = 500L * 1024 * 1024)
        {
            _cacheRoot = Path.Combine(FileSystem.AppDataDirectory, "MapCache");
            _maxCacheSizeBytes = maxCacheSizeBytes;
            Directory.CreateDirectory(_cacheRoot);
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: READ
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Tìm tile trong cache. Trả về null nếu chưa có.
        /// </summary>
        public byte[]? GetTile(int z, int x, int y)
        {
            var path = GetTilePath(z, x, y);
            if (!File.Exists(path)) return null;

            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[TileCache] Read error {z}/{x}/{y}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Kiểm tra tile có tồn tại trong cache không.
        /// </summary>
        public bool HasTile(int z, int x, int y)
            => File.Exists(GetTilePath(z, x, y));

        /// <summary>
        /// Fallback Upscale: nếu tile ở zoom z không có, thử tìm tile cha ở zoom thấp hơn.
        /// Mapsui sẽ scale lên — user thấy ảnh mờ hơn nhưng không trắng xóa.
        /// Trả về (data, parentZoom) hoặc null.
        /// </summary>
        public (byte[] Data, int ParentZ)? GetFallbackTile(int z, int x, int y)
        {
            // Mở rộng phạm vi fallback để giảm khả năng map trắng khi mở app offline từ đầu.
            // Nếu tile zoom hiện tại chưa có, cho phép lấy từ zoom thấp hơn sâu hơn (tối đa 8 cấp).
            for (int parentZ = z - 1; parentZ >= Math.Max(0, z - 8); parentZ--)
            {
                int scale = 1 << (z - parentZ);
                int parentX = x / scale;
                int parentY = y / scale;

                var data = GetTile(parentZ, parentX, parentY);
                if (data != null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[TileCache] Fallback: {z}/{x}/{y} → using parent {parentZ}/{parentX}/{parentY}");
                    return (data, parentZ);
                }
            }
            return null;
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: WRITE
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Lưu tile vào cache (async, thread-safe).
        /// </summary>
        public async Task SaveTileAsync(int z, int x, int y, byte[] data,
            CancellationToken token = default)
        {
            if (data == null || data.Length == 0) return;

            var path = GetTilePath(z, x, y);
            var dir = Path.GetDirectoryName(path)!;

            await _writeLock.WaitAsync(token);
            try
            {
                Directory.CreateDirectory(dir);
                await File.WriteAllBytesAsync(path, data, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[TileCache] Write error {z}/{x}/{y}: {ex.Message}");
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Sync version cho background tasks không cần await.
        /// </summary>
        public void SaveTile(int z, int x, int y, byte[] data)
        {
            if (data == null || data.Length == 0) return;
            var path = GetTilePath(z, x, y);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, data);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[TileCache] Sync write error {z}/{x}/{y}: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: STATS & MAINTENANCE
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Đếm tổng số tiles đã cache.
        /// </summary>
        public int GetCachedTileCount()
        {
            try
            {
                return Directory.GetFiles(_cacheRoot, "*.png",
                    SearchOption.AllDirectories).Length;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Tổng dung lượng cache (bytes).
        /// </summary>
        public long GetCacheSizeBytes()
        {
            try
            {
                long total = 0;
                foreach (var f in Directory.GetFiles(_cacheRoot, "*.png",
                    SearchOption.AllDirectories))
                    total += new FileInfo(f).Length;
                return total;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Dọn dẹp tiles cũ nhất khi vượt quá giới hạn dung lượng.
        /// </summary>
        public void TrimCacheIfNeeded()
        {
            try
            {
                var files = Directory.GetFiles(_cacheRoot, "*.png",
                    SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.LastAccessTime)
                    .ToList();

                long total = files.Sum(f => f.Length);
                int deleted = 0;

                while (total > _maxCacheSizeBytes && files.Count > 0)
                {
                    var oldest = files[0];
                    total -= oldest.Length;
                    oldest.Delete();
                    files.RemoveAt(0);
                    deleted++;
                }

                if (deleted > 0)
                    System.Diagnostics.Debug.WriteLine(
                        $"[TileCache] Trimmed {deleted} tiles, " +
                        $"new size: {total / 1024 / 1024}MB");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[TileCache] Trim error: {ex.Message}");
            }
        }

        /// <summary>
        /// Xóa toàn bộ cache.
        /// </summary>
        public void ClearAll()
        {
            try
            {
                if (Directory.Exists(_cacheRoot))
                    Directory.Delete(_cacheRoot, recursive: true);
                Directory.CreateDirectory(_cacheRoot);
                System.Diagnostics.Debug.WriteLine("[TileCache] Cleared all tiles");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[TileCache] Clear error: {ex.Message}");
            }
        }

        /// <summary>
        /// Kiểm tra một khu vực đã được tải đầy đủ chưa (dùng cho progress UI).
        /// </summary>
        public (int Cached, int Total) GetAreaCoverageStats(
            double centerLat, double centerLng, double radiusKm,
            int minZoom = 14, int maxZoom = 18)
        {
            var tiles = TileAreaCalculator.GetTilesInArea(
                centerLat, centerLng, radiusKm, minZoom, maxZoom);
            int cached = tiles.Count(t => HasTile(t.Z, t.X, t.Y));
            return (cached, tiles.Count);
        }

        // ══════════════════════════════════════════════════════════════
        // PRIVATE HELPERS
        // ══════════════════════════════════════════════════════════════

        private string GetTilePath(int z, int x, int y)
            => Path.Combine(_cacheRoot, z.ToString(), x.ToString(), $"{y}.png");
    }
}
