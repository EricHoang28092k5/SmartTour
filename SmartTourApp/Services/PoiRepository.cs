using SmartTour.Shared.Models;
using SmartTourApp.Data;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartTourApp.Services
{
    public class PoiRepository
    {
        private readonly Database db;
        private List<Poi>? cachedPois;
        private DateTime cacheTime;
        private readonly SemaphoreSlim cacheLock = new(1, 1);

        private readonly TimeSpan cacheTTL = TimeSpan.FromMinutes(5);

        public PoiRepository(Database db)
        {
            this.db = db;
        }

        // ======================
        // GET POIS
        // ======================

        public async Task<List<Poi>> GetPois()
        {
            if (cachedPois != null &&
                DateTime.UtcNow - cacheTime < cacheTTL)
            {
                return cachedPois;
            }

            await cacheLock.WaitAsync();

            try
            {
                if (cachedPois != null &&
                    DateTime.UtcNow - cacheTime < cacheTTL)
                {
                    return cachedPois;
                }

                // TRY SERVER (GIẢ LẬP - KHÔNG API CŨ)
                List<Poi>? server = null;

                try
                {
                    await Task.Delay(50); // simulate API
                    server = null; // chưa có backend
                }
                catch
                {
                    server = null;
                }

                if (server != null)
                {
                    cachedPois = server;
                    cacheTime = DateTime.UtcNow;

                    _ = Task.Run(async () =>
                    {
                        await SafeBackgroundSync(server);
                    });

                    return cachedPois;
                }

                // fallback local
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
                db.AddPois(server);
                await ProcessOutboxAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        // ======================
        // OUTBOX PROCESSOR (SAFE)
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

                        // 🔥 NO API (FIX LỖI UpsertPoi)
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