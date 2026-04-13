// Extension 2: Expose upsert methods cho TourOfflineManager
namespace SmartTourApp.Services
{
    public partial class OfflineSyncService
    {
        /// <summary>
        /// Upsert trực tiếp một OfflinePoiScript (dùng bởi TourOfflineManager).
        /// </summary>
        public void UpsertPoiScriptDirect(OfflinePoiScript script)
        {
            try
            {
                _offlineDb.UpsertPoiScript(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[OfflineSync] UpsertPoiScriptDirect error: {ex.Message}");
            }
        }

        /// <summary>
        /// Upsert trực tiếp một OfflinePoiVersion (dùng bởi TourOfflineManager).
        /// </summary>
        public void UpsertPoiVersionDirect(OfflinePoiVersion version)
        {
            try
            {
                _offlineDb.UpsertPoiVersion(version);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[OfflineSync] UpsertPoiVersionDirect error: {ex.Message}");
            }
        }

        /// <summary>
        /// Lấy số POI cached (dùng để hiển thị trạng thái offline).
        /// </summary>
        public int GetPendingLogCount() => _offlineDb.GetPendingLogCount();
    }
}
