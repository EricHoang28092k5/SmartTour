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

        private List<Poi>? cachedPois;
        private DateTime cacheTime;
        private readonly SemaphoreSlim cacheLock = new(1, 1);

        private readonly TimeSpan cacheTTL = TimeSpan.FromMinutes(5);

        public PoiRepository(Database db, ApiService api)
        {
            this.db = db;
            this.api = api;
        }

        // ======================
        // GET POIS
        // ======================

        public async Task<List<Poi>> GetPois()
        {
            // 🔥 RETURN CACHE nếu còn hạn
            if (cachedPois != null &&
                DateTime.UtcNow - cacheTime < cacheTTL)
            {
                return cachedPois;
            }

            await cacheLock.WaitAsync();

            try
            {
                // double check sau khi lock
                if (cachedPois != null &&
                    DateTime.UtcNow - cacheTime < cacheTTL)
                {
                    return cachedPois;
                }

                List<Poi>? server = null;

                // ======================
                // TRY CALL API
                // ======================
                try
                {
                    server = await api.GetPois();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("API FAIL: " + ex.Message);
                    server = null;
                }

                // ======================
                // IF SERVER OK
                // ======================
                if (server != null && server.Count > 0)
                {
                    cachedPois = server;
                    cacheTime = DateTime.UtcNow;

                    // 🔥 background sync xuống local DB
                    _ = Task.Run(async () =>
                    {
                        await SafeBackgroundSync(server);
                    });

                    return cachedPois;
                }

                // ======================
                // FALLBACK LOCAL
                // ======================
                var local = await Task.Run(() => db.GetPois());

                cachedPois = local;
                cacheTime = DateTime.UtcNow;

                return cachedPois;
            }
            finally
            {
                cacheLock.Release();
            }
        }

        // ======================
        // BACKGROUND SYNC
        // ======================

        private async Task SafeBackgroundSync(List<Poi> server)
        {
            try
            {
                // lưu server -> SQLite
                db.AddPois(server);

                // xử lý outbox (nếu có)
                await ProcessOutboxAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SYNC ERROR: " + ex.Message);
            }
        }

        // ======================
        // OUTBOX PROCESSOR
        // ======================

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

                        // 🔥 GỌI API thật nếu có endpoint
                        // await api.UpsertPoi(poi);

                        await Task.Delay(20); // tạm giả lập

                        db.MarkOutboxSynced(item.Id);
                    }
                }
                catch
                {
                    db.IncreaseRetry(item.Id);
                }
            }
        }

        // ======================
        // CACHE
        // ======================

        public List<Poi>? GetCachedPois()
        {
            return cachedPois;
        }

        public void ClearCache()
        {
            cachedPois = null;
            cacheTime = DateTime.MinValue;
        }
    }
}