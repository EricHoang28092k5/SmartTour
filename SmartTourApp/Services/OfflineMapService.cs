using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartTour.Shared.Models;
using SmartTourApp.Services.Offline;

namespace SmartTourApp.Services
{
    /// <summary>
    /// OfflineMapService — Orchestrator cho toàn bộ offline map pipeline.
    ///
    /// ╔══════════════════════════════════════════════════════════════════╗
    /// ║ TRÁCH NHIỆM                                                     ║
    /// ║ 1. Expose PersistentTileCache cho MapPage                       ║
    /// ║ 2. Quản lý download lifecycle (start/cancel/progress)           ║
    /// ║ 3. Monitor connectivity, emit sự kiện khi mất/có mạng           ║
    /// ║ 4. Tự động tải tiles cho POI gần nhất khi có mạng               ║
    /// ║ 5. Provide cache stats cho Settings UI                          ║
    /// ╚══════════════════════════════════════════════════════════════════╝
    /// </summary>
    public class OfflineMapService : IDisposable
    {
        // ══════════════════════════════════════════════════════════════
        // COMPONENTS
        // ══════════════════════════════════════════════════════════════
        public PersistentTileCache TileCache { get; }
        public MapTileDownloader Downloader { get; }

        // ══════════════════════════════════════════════════════════════
        // STATE
        // ══════════════════════════════════════════════════════════════
        private CancellationTokenSource? _downloadCts;
        private bool _lastConnected = true;
        private System.Timers.Timer? _connectivityTimer;

        // ── Events ──
        public event Action<bool>? OnConnectivityChanged;
        public event Action<DownloadProgress>? OnDownloadProgress;
        public event Action<string>? OnStatusMessage;

        // ── Settings ──
        private const double DefaultRadiusKm = 2.5;
        private const int AutoDownloadMinZoom = 14;
        private const int AutoDownloadMaxZoom = 17; // 17 for auto (save data), 18 for manual

        public OfflineMapService()
        {
            TileCache = new PersistentTileCache();
            Downloader = new MapTileDownloader(TileCache);

            Downloader.OnProgress += p =>
            {
                OnDownloadProgress?.Invoke(p);
                OnStatusMessage?.Invoke(p.Message);
            };

            StartConnectivityMonitor();
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: MANUAL DOWNLOAD (user trigger)
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// User nhấn "Tải bản đồ offline" — tải khu vực với độ phân giải cao nhất.
        /// </summary>
        public async Task DownloadAreaForTourAsync(
            double centerLat, double centerLng,
            double radiusKm = DefaultRadiusKm)
        {
            // Hủy download cũ nếu đang chạy
            _downloadCts?.Cancel();
            _downloadCts = new CancellationTokenSource();
            var token = _downloadCts.Token;

            try
            {
                await Downloader.DownloadAreaAsync(
                    centerLat, centerLng, radiusKm,
                    minZoom: AutoDownloadMinZoom,
                    maxZoom: 18, // High res khi manual
                    token: token);
            }
            catch (OperationCanceledException)
            {
                OnStatusMessage?.Invoke("Đã hủy tải bản đồ");
            }
        }

        /// <summary>
        /// Tải trước khu vực cho danh sách POI trong tour.
        /// Gọi khi user nhấn "Bắt đầu hành trình".
        /// </summary>
        public async Task PrefetchTourMapAsync(
            List<Poi> pois,
            double radiusKmPerPoi = 1.5,
            CancellationToken token = default)
        {
            if (!IsConnected())
            {
                OnStatusMessage?.Invoke("Offline — dùng bản đồ đã tải");
                return;
            }

            var centers = pois
                .Select(p => (p.Lat, p.Lng))
                .Distinct()
                .ToList();

            OnStatusMessage?.Invoke(
                $"Đang tải bản đồ cho {pois.Count} điểm...");

            await Downloader.DownloadMultipleAreasAsync(centers, radiusKmPerPoi, token);
        }

        /// <summary>
        /// Hủy download đang chạy.
        /// </summary>
        public void CancelDownload()
        {
            _downloadCts?.Cancel();
            _downloadCts = null;
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: AUTO-CACHE khi user scroll map (gọi từ MapPage)
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Gọi khi Mapsui render xong một tile có mạng — lưu vào cache.
        /// MapPage gọi hàm này sau khi nhận tile từ network.
        /// </summary>
        public void OnTileRendered(int z, int x, int y, byte[] data)
        {
            if (data == null || data.Length == 0) return;
            // Fire-and-forget — không block UI thread
            _ = Task.Run(() => TileCache.SaveTile(z, x, y, data));
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: ESTIMATE (cho dialog xác nhận trước khi tải)
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Ước tính trước khi tải — hiển thị cho user xác nhận.
        /// </summary>
        public (int NewTiles, long SizeMB, int AlreadyCached, string Description)
            GetDownloadEstimate(double centerLat, double centerLng,
                double radiusKm = DefaultRadiusKm)
        {
            var (tiles, bytes, cached) = Downloader.EstimateDownload(
                centerLat, centerLng, radiusKm,
                AutoDownloadMinZoom, 18);

            long mb = bytes / 1024 / 1024;
            string desc = tiles == 0
                ? "Bản đồ khu vực này đã được tải đầy đủ!"
                : $"{tiles} tiles mới (~{mb}MB), {cached} tiles đã có sẵn";

            return (tiles, mb, cached, desc);
        }

        // ══════════════════════════════════════════════════════════════
        // PUBLIC: CACHE STATS (cho Settings UI)
        // ══════════════════════════════════════════════════════════════

        public int GetCachedTileCount() => TileCache.GetCachedTileCount();

        public long GetCacheSizeMB() => TileCache.GetCacheSizeBytes() / 1024 / 1024;

        public void ClearMapCache()
        {
            TileCache.ClearAll();
            OnStatusMessage?.Invoke("Đã xóa toàn bộ bản đồ offline");
        }

        /// <summary>
        /// Kiểm tra một khu vực đã đủ tiles chưa.
        /// </summary>
        public double GetAreaCoveragePercent(
            double centerLat, double centerLng, double radiusKm = DefaultRadiusKm)
        {
            var (cached, total) = TileCache.GetAreaCoverageStats(
                centerLat, centerLng, radiusKm, AutoDownloadMinZoom, 17);
            return total > 0 ? (double)cached / total * 100 : 0;
        }

        // ══════════════════════════════════════════════════════════════
        // CONNECTIVITY MONITOR
        // ══════════════════════════════════════════════════════════════

        private void StartConnectivityMonitor()
        {
            // Poll connectivity mỗi 10s để detect thay đổi
            _connectivityTimer = new System.Timers.Timer(10_000);
            _connectivityTimer.Elapsed += (_, _) =>
            {
                bool connected = IsConnected();
                if (connected != _lastConnected)
                {
                    _lastConnected = connected;
                    MainThread.BeginInvokeOnMainThread(() =>
                        OnConnectivityChanged?.Invoke(connected));

                    System.Diagnostics.Debug.WriteLine(
                        $"[OfflineMap] Connectivity: {(connected ? "🌐 Online" : "📴 Offline")}");
                }
            };
            _connectivityTimer.Start();
        }

        // ══════════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════════

        public static bool IsConnected()
        {
            try
            {
                var a = Connectivity.Current.NetworkAccess;
                return a == NetworkAccess.Internet ||
                       a == NetworkAccess.ConstrainedInternet;
            }
            catch { return false; }
        }

        public void Dispose()
        {
            _downloadCts?.Cancel();
            _connectivityTimer?.Stop();
            _connectivityTimer?.Dispose();
        }
    }
}
