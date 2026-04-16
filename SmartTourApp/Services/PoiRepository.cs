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
            if (cachedPois != null &&
                string.Equals(cachedLang, languageService.Current, StringComparison.OrdinalIgnoreCase) &&
                DateTime.UtcNow - cacheTime < cacheTTL)
            {
                return cachedPois;
            }

            await cacheLock.WaitAsync();

            try
            {
                if (cachedPois != null &&
                    string.Equals(cachedLang, languageService.Current, StringComparison.OrdinalIgnoreCase) &&
                    DateTime.UtcNow - cacheTime < cacheTTL)
                {
                    return cachedPois;
                }

                List<Poi>? server = null;

                try
                {
                    server = await api.GetPois();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("API FAIL: " + ex.Message);
                    server = null;
                }

                if (server != null && server.Count > 0)
                {
                    cachedPois = server;
                    cacheTime = DateTime.UtcNow;
                    cachedLang = languageService.Current;

                    // ── Yêu cầu 1: Version Check + Selective Pre-fetch ──
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // 1. Sync local DB
                            db.AddPois(cachedPois);

                            // 2. Kiểm tra POI nào bị stale (server mới hơn local)
                            var staleIds = await offlineSync.CheckStalePoiIdsAsync(cachedPois);

                            if (staleIds.Count > 0)
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"[PoiRepo] {staleIds.Count} stale POIs → refreshing...");
                                await offlineSync.RefreshStaleDataAsync(staleIds, cachedPois);
                            }

                            // 3. Preload audio URLs (top 5 gần nhất)
                            var urls = new List<string>();
                            foreach (var poi in cachedPois.Take(5))
                            {
                                var scripts = await api.GetTtsScripts(poi.Id);
                                foreach (var s in scripts)
                                {
                                    if (!string.IsNullOrWhiteSpace(s.AudioUrl))
                                        urls.Add(s.AudioUrl!);
                                }
                            }

                            if (urls.Count > 0)
                                await audio.Preload(urls);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[PoiRepo] BG sync error: {ex.Message}");
                        }
                    });

                    return cachedPois;
                }

                // Fallback local SQLite
                var local = await Task.Run(() => db.GetPois());
                cachedPois = local;
                cacheTime = DateTime.UtcNow;
                cachedLang = languageService.Current;
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
                catch
                {
                    db.IncreaseRetry(item.Id);
                }
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
        }
    }
}
