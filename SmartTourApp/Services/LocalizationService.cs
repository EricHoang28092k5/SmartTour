namespace SmartTourApp.Services;

/// <summary>
/// LocalizationService — Quản lý tất cả chuỗi tĩnh trong App theo ngôn ngữ audio.
/// Hỗ trợ: vi, en, ja, zh, ko
/// </summary>
public class LocalizationService
{
    private readonly LanguageService _lang;

    public LocalizationService(LanguageService lang)
    {
        _lang = lang;
    }

    public string Current => _lang.Current;

    public event Action? LanguageChanged;

    public void NotifyChanged() => LanguageChanged?.Invoke();

    // ══════════════════════════════════════════════════════════════
    // COMMON
    // ══════════════════════════════════════════════════════════════
    public string AppName => T("SmartTour", "SmartTour", "スマートツアー", "智慧游", "스마트투어");
    public string OK => T("OK", "OK", "OK", "确定", "확인");
    public string Cancel => T("Hủy", "Cancel", "キャンセル", "取消", "취소");
    public string Close => T("Đóng", "Close", "閉じる", "关闭", "닫기");
    public string Save => T("Lưu", "Save", "保存", "保存", "저장");
    public string Loading => T("Đang tải...", "Loading...", "読み込み中...", "加载中...", "로딩 중...");
    public string Error => T("Lỗi", "Error", "エラー", "错误", "오류");
    public string Retry => T("Thử lại", "Retry", "再試行", "重试", "다시 시도");
    public string NoData => T("Không có dữ liệu", "No data", "データなし", "无数据", "데이터 없음");
    public string Offline => T("Ngoại tuyến", "Offline", "オフライン", "离线", "오프라인");
    public string Online => T("Trực tuyến", "Online", "オンライン", "在线", "온라인");

    // ══════════════════════════════════════════════════════════════
    // BOTTOM TABS
    // ══════════════════════════════════════════════════════════════
    public string HomeTab => T("Trang chủ", "Home", "ホーム", "首页", "홈");
    public string MapTab => T("Bản đồ", "Map", "地図", "地图", "지도");
    public string SettingsTab => T("Cài đặt", "Settings", "設定", "设置", "설정");

    // ══════════════════════════════════════════════════════════════
    // LOADING PAGE
    // ══════════════════════════════════════════════════════════════
    public string LoadingStarting => T("Đang khởi động...", "Starting...", "起動中...", "启动中...", "시작 중...");
    public string LoadingPoi => T("Đang tải địa điểm...", "Loading places...", "スポット読込中...", "加载景点...", "장소 로딩 중...");
    public string LoadingTts => T("Khởi tạo TTS...", "Initializing TTS...", "TTS初期化中...", "初始化TTS...", "TTS 초기화 중...");
    public string LoadingTracking => T("Khởi tạo Tracking...", "Initializing Tracking...", "トラッキング初期化中...", "初始化追踪...", "추적 초기화 중...");
    public string LoadingDone => T("Hoàn tất!", "Done!", "完了！", "完成！", "완료!");

    // ══════════════════════════════════════════════════════════════
    // HOME PAGE
    // ══════════════════════════════════════════════════════════════
    public string GreetingMorning => T("Chào buổi sáng 👋", "Good morning 👋", "おはようございます 👋", "早上好 👋", "좋은 아침 👋");
    public string GreetingAfternoon => T("Chào buổi chiều 👋", "Good afternoon 👋", "こんにちは 👋", "下午好 👋", "안녕하세요 👋");
    public string GreetingEvening => T("Chào buổi tối 👋", "Good evening 👋", "こんばんは 👋", "晚上好 👋", "좋은 저녁 👋");
    public string NearestBadge => T("📍 Gần bạn nhất", "📍 Nearest to you", "📍 最寄り", "📍 最近", "📍 가장 가까운");
    public string ListenNow => T("🎧  Nghe ngay", "🎧  Listen now", "🎧  今すぐ聴く", "🎧  立即收听", "🎧  지금 듣기");
    public string NowPlaying => T("🔊  Đang phát...", "🔊  Playing...", "🔊  再生中...", "🔊  播放中...", "🔊  재생 중...");
    public string NearbyPlaces => T("Địa điểm gần bạn", "Nearby Places", "近くのスポット", "附近景点", "주변 장소");
    public string ExploreAround => T("Khám phá các điểm tham quan xung quanh", "Explore attractions around you", "周辺の観光スポットを探索", "探索周边景点", "주변 명소 탐색");
    public string ViewAll => T("Xem tất cả →", "View all →", "すべて見る →", "查看全部 →", "전체 보기 →");
    public string Places => T("Địa điểm", "Places", "スポット", "景点", "장소");
    public string Journeys => T("Hành trình", "Journeys", "旅程", "行程", "여정");
    public string Rating => T("Đánh giá", "Rating", "評価", "评分", "평점");
    public string DistanceM => T("{0} m", "{0} m", "{0} m", "{0} 米", "{0} m");
    public string DistanceKm => T("Cách bạn {0} m", "{0} m away", "距離 {0} m", "距离 {0} 米", "{0} m 거리");
    public string DistanceKmFar => T("Cách bạn {0} km", "{0} km away", "距離 {0} km", "距离 {0} 千米", "{0} km 거리");

    // ══════════════════════════════════════════════════════════════
    // MAP PAGE
    // ══════════════════════════════════════════════════════════════
    public string MapTitle => T("Bản đồ", "Map", "マップ", "地图", "지도");
    public string OfflineMapHint => T("Chế độ ngoại tuyến — bản đồ chỉ hiện vùng đã tải", "Offline — showing cached map only", "オフライン — キャッシュ済みエリアのみ表示", "离线 — 仅显示已缓存区域", "오프라인 — 캐시된 지역만 표시");
    public string DownloadMap => T("Tải bản đồ", "Download map", "マップDL", "下载地图", "지도 다운로드");
    public string DownloadingMap => T("Đang tải bản đồ...", "Downloading map...", "マップDL中...", "地图下载中...", "지도 다운로드 중...");
    public string CalculatingRoute => T("Đang tính đường đi...", "Calculating route...", "ルート計算中...", "路线计算中...", "경로 계산 중...");
    public string Directions => T("Đường đi", "Directions", "経路", "路线", "경로");

    /// <summary>YC4: Localized label for the Google Maps directions button in bottom POI card.</summary>
    public string DirectionsBtn => T("Chỉ đường", "Get Directions", "ルートを表示", "获取路线", "길 안내");

    public string Start => T("Bắt đầu", "Start", "開始", "开始", "시작");
    public string Call => T("Gọi", "Call", "電話", "拨打", "전화");
    public string SavePlace => T("Lưu", "Save", "保存", "收藏", "저장");
    public string RouteError => T("Không thể tính đường đi: ", "Cannot calculate route: ", "ルート計算不可: ", "无法计算路线: ", "경로 계산 불가: ");
    public string DownloadAreaTitle => T("Tải bản đồ offline", "Download offline map", "オフラインマップDL", "下载离线地图", "오프라인 지도 다운로드");
    public string AlreadyCached => T("Đã có sẵn", "Already cached", "キャッシュ済み", "已缓存", "이미 캐시됨");
    public string MapAlreadyDownloaded => T("Bản đồ khu vực này đã được tải!\n{0} tiles trong cache.", "This area is already downloaded!\n{0} tiles cached.", "このエリアはダウンロード済み！\n{0}タイル", "该区域地图已下载！\n{0}个瓦片", "이 지역은 이미 다운로드됨!\n{0}개 타일");
    public string DownloadNow => T("Tải ngay", "Download now", "今すぐDL", "立即下载", "지금 다운로드");
    public string NoGps => T("Lỗi", "Error", "エラー", "错误", "오류");
    public string NoGpsMsg => T("Không lấy được vị trí GPS.", "Cannot get GPS location.", "GPS位置取得不可。", "无法获取GPS位置。", "GPS 위치를 가져올 수 없습니다.");
    public string Cancelling => T("Đã hủy tải bản đồ", "Download cancelled", "DLキャンセル", "取消下载", "다운로드 취소");
    public string NoNetwork => T("Không có mạng", "No network", "ネットワークなし", "无网络", "네트워크 없음");
    public string NoNetworkMsg => T("Bạn đang offline. Kết nối mạng để tải bản đồ.", "You are offline. Connect to download map.", "オフライン中。マップDLにはネット接続が必要。", "您处于离线状态。连接网络以下载地图。", "오프라인 상태입니다. 지도를 다운로드하려면 네트워크에 연결하세요.");
    public string Downloading => T("Đang tải...", "Downloading...", "ダウンロード中...", "下载中...", "다운로드 중...");

    // ══════════════════════════════════════════════════════════════
    // POI DETAIL PAGE
    // ══════════════════════════════════════════════════════════════
    public string Overview => T("Tổng quan", "Overview", "概要", "概览", "개요");
    public string Menu => T("Thực đơn", "Menu", "メニュー", "菜单", "메뉴");
    public string Introduction => T("GIỚI THIỆU", "INTRODUCTION", "紹介", "介绍", "소개");
    public string NoDescription => T("Chưa có mô tả", "No description yet", "説明なし", "暂无描述", "설명 없음");
    public string AudioGuide => T("Audio Guide", "Audio Guide", "音声ガイド", "语音导览", "오디오 가이드");
    public string NarrationPoint => T("Thuyết minh điểm tham quan", "Attraction narration", "観光スポット解説", "景点解说", "관광지 해설");
    public string OpenAllDay => T("Mở cả ngày", "Open all day", "終日営業", "全天开放", "종일 영업");
    public string IsOpen => T("Đang mở cửa", "Open now", "営業中", "营业中", "영업 중");
    public string IsClosed => T("Đã đóng cửa", "Closed", "閉店", "已关闭", "영업 종료");
    public string ClosesAt => T("Đóng lúc {0}", "Closes at {0}", "{0}に閉店", "{0}关闭", "{0}에 닫힘");
    public string OpensAt => T("Mở lúc {0}", "Opens at {0}", "{0}に開店", "{0}开放", "{0}에 열림");
    public string StopPlaying => T("■  Dừng phát", "■  Stop", "■  停止", "■  停止", "■  정지");
    public string TapToClose => T("Nhấn để đóng", "Tap to close", "タップして閉じる", "点击关闭", "탭하여 닫기");

    // ══════════════════════════════════════════════════════════════
    // SETTINGS PAGE
    // ══════════════════════════════════════════════════════════════
    public string SettingsTitle => T("Settings", "Settings", "設定", "设置", "설정");
    public string SettingsSubtitle => T("CÀI ĐẶT", "SETTINGS", "設定", "设置", "설정");
    public string Language => T("NGÔN NGỮ", "LANGUAGE", "言語", "语言", "언어");
    public string NarrationLanguage => T("Ngôn ngữ thuyết minh", "Narration language", "解説言語", "解说语言", "해설 언어");
    public string ChooseLanguage => T("Chọn ngôn ngữ", "Choose language", "言語を選択", "选择语言", "언어 선택");
    public string AutoNarration => T("THUYẾT MINH TỰ ĐỘNG", "AUTO NARRATION", "自動解説", "自动解说", "자동 해설");
    public string AutoPlay => T("Tự động phát thuyết minh", "Auto-play narration", "自動再生", "自动播放解说", "자동 재생 해설");
    public string AutoPlayDesc => T("Tự động phát khi bước vào vùng điểm tham quan", "Auto-plays when entering attraction zone", "観光スポットエリア入場時に自動再生", "进入景点区域时自动播放", "관광지 구역 진입 시 자동 재생");
    public string AutoPlayOn => T("Thuyết minh sẽ tự động phát khi bạn tiếp cận điểm tham quan.", "Narration auto-plays when approaching attractions.", "観光スポット接近時に自動再生されます。", "接近景点时将自动播放解说。", "관광지에 접근할 때 해설이 자동으로 재생됩니다.");
    public string AutoPlayOff => T("Tự động phát đã tắt. Heatmap vẫn được ghi nhận.", "Auto-play off. Heatmap still tracked.", "自動再生オフ。ヒートマップは記録中。", "自动播放已关闭。热力图仍在记录。", "자동 재생 끄기. 히트맵은 계속 기록됩니다.");
    public string StatsHeatmap => T("THỐNG KÊ & HEATMAP", "STATS & HEATMAP", "統計・ヒートマップ", "统计与热力图", "통계 및 히트맵");
    public string HeatmapData => T("Dữ liệu heatmap", "Heatmap data", "ヒートマップデータ", "热力图数据", "히트맵 데이터");
    public string HeatmapDesc => T("Hệ thống luôn ghi nhận lượt vào vùng để thống kê, kể cả khi tắt tự động phát", "System always tracks zone entries for analytics, even when auto-play is off", "自動再生オフでもゾーン入場を記録", "即使关闭自动播放，系统仍记录进入区域", "자동 재생 끄기 시에도 구역 진입을 기록합니다");
    public string OfflineMapTitle => T("BẢN ĐỒ NGOẠI TUYẾN", "OFFLINE MAP", "オフラインマップ", "离线地图", "오프라인 지도");
    public string MapSaved => T("Bản đồ đã lưu", "Saved map", "保存済みマップ", "已保存地图", "저장된 지도");
    public string ClearMapCache => T("Xóa bản đồ offline", "Clear offline map", "オフラインマップ削除", "清除离线地图", "오프라인 지도 삭제");
    public string ClearMapCacheDesc => T("Giải phóng bộ nhớ — tải lại khi cần", "Free storage — reload when needed", "ストレージ解放 — 必要時に再DL", "释放存储 — 需要时重新下载", "저장공간 해제 — 필요시 재다운로드");
    public string MapCacheInfo => T("Bản đồ tự động lưu khi bạn duyệt có kết nối mạng. Nhấn ⬇ trên bản đồ để tải toàn bộ khu vực trước chuyến đi.", "Map auto-saves while browsing with network. Tap ⬇ on map to pre-download area.", "ネット接続時に自動保存。マップの⬇でエリアを事前DL。", "联网浏览时自动保存。点击地图上⬇预下载区域。", "네트워크 연결 시 자동 저장. 지도의 ⬇를 눌러 사전 다운로드.");
    public string SecurityAccess => T("BẢO MẬT TRUY CẬP", "SECURITY", "セキュリティ", "访问安全", "보안 액세스");
    public string ClearQrSession => T("Xóa phiên quét QR (7 ngày)", "Clear QR session (7 days)", "QRセッション削除(7日)", "清除QR会话(7天)", "QR 세션 지우기 (7일)");
    public string ClearQrSessionDesc => T("Buộc ứng dụng quét lại QR ở lần mở tiếp theo", "Force app to re-scan QR next launch", "次回起動時にQR再スキャンを強制", "强制应用在下次启动时重新扫描QR", "다음 실행 시 QR 재스캔 강제");
    public string SaveSettings => T("💾 Lưu cài đặt", "💾 Save settings", "💾 設定を保存", "💾 保存设置", "💾 설정 저장");
    public string LanguageUnchanged => T("Ngôn ngữ không thay đổi", "Language unchanged", "言語変更なし", "语言未更改", "언어 변경 없음");
    public string Reloading => T("Đang tải lại ứng dụng...", "Reloading app...", "アプリ再読込中...", "正在重载应用...", "앱 다시 로딩 중...");
    public string Notice => T("Thông báo", "Notice", "お知らせ", "通知", "알림");
    public string Success => T("Thành công", "Success", "成功", "成功", "성공");
    public string EmptyCache => T("Cache bản đồ đang trống.", "Map cache is empty.", "マップキャッシュは空です。", "地图缓存为空。", "지도 캐시가 비어 있습니다.");
    public string ClearMapConfirm => T("Xóa bản đồ offline", "Clear offline map", "オフラインマップ削除", "清除离线地图", "오프라인 지도 삭제");
    public string ClearMapConfirmMsg => T("Xóa {0} tiles ({1}MB)?\nBản đồ sẽ cần tải lại khi có mạng.", "Delete {0} tiles ({1}MB)?\nMap will reload when online.", "{0}タイル({1}MB)を削除？\nオンライン時に再DLが必要。", "删除{0}个瓦片({1}MB)?\n联网时需重新下载。", "{0}개 타일({1}MB) 삭제?\n온라인 시 재다운로드 필요.");
    public string Delete => T("Xóa", "Delete", "削除", "删除", "삭제");
    public string ClearQrConfirmTitle => T("Xóa phiên quét QR", "Clear QR session", "QRセッション削除", "清除QR会话", "QR 세션 삭제");
    public string ClearQrConfirmMsg => T("Sau khi xóa, lần mở app tiếp theo sẽ bắt buộc quét QR lại. Bạn chắc chắn chứ?", "After clearing, next app launch will require QR scan. Are you sure?", "削除後、次回起動時にQR再スキャンが必要です。よろしいですか？", "清除后，下次启动需重新扫描QR。确定吗？", "삭제 후 다음 실행 시 QR 재스캔이 필요합니다. 확실합니까?");
    public string ClearQrSuccess => T("Đã xóa phiên QR. Mở lại app sẽ yêu cầu quét lại.", "QR session cleared. Reopen app to re-scan.", "QRセッションを削除しました。再起動で再スキャン。", "已清除QR会话。重新打开应用以重新扫描。", "QR 세션이 삭제되었습니다. 앱을 다시 열어 재스캔하세요.");
    public string MapCacheCleared => T("Đã xóa cache bản đồ.", "Map cache cleared.", "マップキャッシュを削除しました。", "地图缓存已清除。", "지도 캐시가 삭제되었습니다.");
    public string TilesInfo => T("{0:N0} tiles (~{1}MB)", "{0:N0} tiles (~{1}MB)", "{0:N0}タイル (~{1}MB)", "{0:N0}个瓦片 (~{1}MB)", "{0:N0}개 타일 (~{1}MB)");
    public string TilesCached => T("✅ {0} tiles đã lưu", "✅ {0} tiles cached", "✅ {0}タイル保存済み", "✅ {0}个瓦片已保存", "✅ {0}개 타일 저장됨");
    public string NoOfflineMap => T("Chưa có bản đồ offline", "No offline map yet", "オフラインマップなし", "暂无离线地图", "오프라인 지도 없음");

    // ══════════════════════════════════════════════════════════════
    // HELPER
    // ══════════════════════════════════════════════════════════════
    private string T(string vi, string en, string ja, string zh, string ko)
    {
        return _lang.Current switch
        {
            "en" => en,
            "ja" => ja,
            "zh" => zh,
            "ko" => ko,
            _ => vi
        };
    }
}
