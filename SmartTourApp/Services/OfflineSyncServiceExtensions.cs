// Extension to OfflineSyncService — add helper used by PoiRepository
namespace SmartTourApp.Services
{
    public partial class OfflineSyncService
    {
        /// <summary>
        /// Helper: tổng số script đang lưu trong SQLite (dùng cho progress reporting).
        /// </summary>
        public int GetAllLocalScripts_Count()
        {
            try
            {
                return _offlineDb.GetCachedPoiCount();
            }
            catch
            {
                return 0;
            }
        }
    }
}
