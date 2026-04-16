using SmartTour.Shared.Models;
using SmartTourApp.Data;
using SmartTour.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartTourApp.Services
{
    public class PoiRepository
    {
        private readonly Database db;
        private readonly ApiService api;
        private readonly AudioService audio;
        private readonly OfflineSyncService offlineSync;
        private readonly LanguageService languageService;

        private List<Poi>? cachedPois;
        private DateTime cacheTime;
        private string cachedLang = string.Empty;
        private readonly SemaphoreSlim cacheLock = new(1, 1);

        // YC1: Flag để biết đã pre-fetch audio chưa (tránh pre-fetch nhiều lần)
        private bool _audioPreFetchDone = false;
        private string _audioPreFetchLang = "";

        private readonly TimeSpan cacheTTL = TimeSpan.FromMinutes(5);

        public PoiRepository(Database db, ApiService api, AudioService audio,
            LanguageService languageService,
            OfflineSyncService offlineSync)
        {
            this.db = db;
            this.api = api;
            this.audio = audio;
            this.languageService = languageService;
            this.offlineSync = offlineSync;
        }

        // ══════════════════════════════════════════════════════════════
        // GET POIS
        // ══════════════════════════════════════════════════════════════

        public async Task<List<Poi>> GetPois()
        {
            var currentLang = languageService.Current;

            if (cachedPois != null &&
                string.Equals(cachedLang, currentLang, StringComparison.OrdinalIgnoreCase) &&
                DateTime.UtcNow - cacheTime < cacheTTL)
            {
                return cachedPois;
            }

            await cacheLock.WaitAsync();
            try
            {
                currentLang = languageService.Current;
                if (cachedPois != null &&
                    string.Equals(cachedLang, currentLang, StringComparison.OrdinalIgnoreCase) &&
                    DateTime.UtcNow - cacheTime < cacheTTL)
                {
                    return cachedPois;
                }

                List<Poi>? server = null;
                try { server = await api.GetPois(); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("API FAIL: " + ex.Message);
                    server = null;
                }

                if (server != null && server.Count > 0)
                {
                    cachedPois = server;
                    cacheTime = DateTime.UtcNow;
                    cachedLang = currentLang;

                    var capturedPois = new List<Poi>(server);
                    var capturedLang = currentLang;

                    // Ghi POI xuống SQLite NGAY để nếu user thoát app sớm vẫn có dữ liệu offline.
                    try { db.AddPois(capturedPois); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[PoiRepo] Sync save pois error: {ex.Message}");
                    }

                    // ── Background: sync DB + pre-fetch toàn bộ data offline ──
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // 1. Sync local DB
                            db.AddPois(capturedPois);

                            // 2. YC1: Pre-fetch toàn bộ scripts + foods nếu chưa làm
                            //    hoặc ngôn ngữ đã thay đổi (cần refresh food translations)
                            bool needFetch = !_audioPreFetchDone ||
                                !string.Equals(_audioPreFetchLang, capturedLang, StringComparison.OrdinalIgnoreCase);

                            if (needFetch)
                            {
                                System.Diagnostics.Debug.WriteLine("[PoiRepo] Starting full offline pre-fetch...");
                                await offlineSync.PrefetchPoiDataAsync(capturedPois);
                                _audioPreFetchDone = true;
                                _audioPreFetchLang = capturedLang;
                            }
                            else
                            {
                                // Chỉ check stale data
                                var staleIds = await offlineSync.CheckStalePoiIdsAsync(capturedPois);
                                if (staleIds.Count > 0)
                                {
                                    System.Diagnostics.Debug.WriteLine(
                                        $"[PoiRepo] {staleIds.Count} stale POIs → refreshing...");
                                    await offlineSync.RefreshStaleDataAsync(staleIds, capturedPois);
                                }
                            }

                            // 3. Preload audio URLs (top 5)
                            var urls = new List<string>();
                            foreach (var poi in capturedPois.Take(5))
                            {
                                var localScripts = offlineSync.GetAllLocalScripts(poi.Id);
                                foreach (var s in localScripts)
                                {
                                    if (!string.IsNullOrWhiteSpace(s.AudioUrl))
                                        urls.Add(s.AudioUrl);
                                }
                            }
                            if (urls.Count > 0)
                                await audio.Preload(urls);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[PoiRepo] BG sync error: {ex.Message}");
                        }
                    });

                    return cachedPois;
                }

                // Fallback local SQLite
                var local = await Task.Run(() => db.GetPois());
                cachedPois = local;
                cacheTime = DateTime.UtcNow;
                cachedLang = currentLang;
                return cachedPois;
            }
            finally
            {
                cacheLock.Release();
            }
        }

        // ══════════════════════════════════════════════════════════════
        // BACKGROUND SYNC
        // ══════════════════════════════════════════════════════════════

        private async Task SafeBackgroundSync(List<Poi> server)
        {
            try
            {
                db.AddPois(server);
                await ProcessOutboxAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SYNC ERROR: " + ex.Message);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // OUTBOX PROCESSOR
        // ══════════════════════════════════════════════════════════════

        public async Task ProcessOutboxAsync()
        {
            var items = db.GetOutboxItems();
            foreach (var item in items)
            {
                try
                {
                    if (item.Type == "POI_UPSERT")
                    {
                        var poi = System.Text.Json.JsonSerializer.Deserialize<Poi>(item.Payload);
                        if (poi == null) continue;
                        await Task.Delay(20);
                        db.MarkOutboxSynced(item.Id);
                    }
                }
                catch { db.IncreaseRetry(item.Id); }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // CACHE
        // ══════════════════════════════════════════════════════════════

        public List<Poi>? GetCachedPois() => cachedPois;

        public void ClearCache()
        {
            cachedPois = null;
            cacheTime = DateTime.MinValue;
            cachedLang = string.Empty;
            _audioPreFetchDone = false;
            _audioPreFetchLang = "";
        }
    }
}
