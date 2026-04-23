# PRD SmartTour (Bám sát chức năng đã thêm)

| Thuộc tính | Giá trị |
| --- | --- |
| Phiên bản | 1.0 |
| Ngày cập nhật | 2026-04-17 |
| Phạm vi | SmartTour CMS + SmartTour Backend + SmartTour App |
| Mục tiêu | Mô tả đúng các chức năng đang có trong code sau các đợt nâng cấp gần đây |

---

## 1) Tổng quan sản phẩm
SmartTour là hệ thống du lịch thông minh gồm:
- `SmartTourCMS`: web quản trị nội dung, người dùng, dashboard, premium.
- `SmartTourBackend`: API nghiệp vụ, analytics, audio, premium payment, presence.
- `SmartTourApp`: app MAUI cho khách tham quan, hỗ trợ đa ngôn ngữ và offline.

Mục tiêu cốt lõi:
- Quản lý và phân phối nội dung POI/Food/Tour đa ngôn ngữ.
- Cho phép trải nghiệm audio tham quan trên app, kể cả khi mất mạng.
- Theo dõi hành vi sử dụng để tối ưu nội dung.
- Hỗ trợ mô hình Premium cho POI qua MoMo.

---

## 2) Vai trò người dùng
- **Admin**: toàn quyền quản trị hệ thống, người dùng, nội dung, dashboard.
- **Vendor**: quản lý nội dung thuộc phạm vi của mình, nâng cấp premium POI.
- **Traveler**: sử dụng app để xem bản đồ, xem POI, nghe audio, theo dõi lộ trình.

---

## 3) Chức năng chính đã triển khai

### 3.1 CMS (Web)
1. **Đăng nhập và phân quyền Admin/Vendor**
   - Điều hướng dashboard theo role.
2. **Quản lý POI**
   - Tạo, sửa, xóa POI; quản lý thông tin, mô tả, tọa độ, ảnh.
3. **Quản lý Food theo POI**
   - CRUD món ăn, tìm kiếm, lọc theo POI.
4. **Quản lý Tour**
   - Tạo/sửa tour và danh sách POI trong tour.
5. **Quản lý Translation**
   - Quản lý bản dịch cho POI/Food/Tour.
6. **Quản lý Audio/TTS**
   - Tạo hoặc tạo lại audio theo bản dịch.
   - Hạn chế regenerate toàn bộ không cần thiết.
7. **Dashboard analytics**
   - Chỉ số tổng quan, heatmap, dữ liệu nghe audio.
   - Vendor chỉ xem dữ liệu trong phạm vi của mình.
8. **Dashboard thiết bị online (mới thêm)**
   - Hiển thị số thiết bị đang hoạt động theo ngưỡng thời gian.
   - Hiển thị chi tiết thiết bị: device id, ip, model, platform, app version, last seen.
   - Tự động refresh định kỳ.
9. **Nâng cấp POI lên Premium qua MoMo (mới thêm)**
   - Chọn POI, chọn gói tuần/tháng/năm.
   - Tạo link/QR thanh toán MoMo.
   - Nhận callback/IPN để cập nhật trạng thái thanh toán.

### 3.2 Backend API
1. **API xác thực và phân quyền**
   - JWT + role cho CMS/App.
2. **API POI/Food/Tour/Translation**
   - Cung cấp dữ liệu cho app và CMS.
3. **API Audio**
   - Quản lý script, tổng hợp audio, trả URL audio.
4. **API Analytics**
   - Playlog audio, heatmap, route session.
5. **API Presence (mới thêm)**
   - `heartbeat`: cập nhật trạng thái online thiết bị app.
   - `offline`: đánh dấu thiết bị ngắt hoạt động nhanh.
6. **API Premium/MoMo (đang dùng trong luồng premium)**
   - Tạo đơn thanh toán.
   - Xử lý IPN, cập nhật trạng thái đơn và thời hạn premium.

### 3.3 Mobile App
1. **QR Gate bắt buộc khi vào app**
   - Quét QR hợp lệ mới vào hệ thống.
   - Giữ phiên trong 7 ngày, hết hạn thì quét lại.
2. **Hiển thị POI trên map + danh sách**
   - Xem chi tiết POI và nội dung liên quan.
3. **Đa ngôn ngữ**
   - Thay đổi ngôn ngữ hiển thị nội dung POI/Food theo cấu hình người dùng.
4. **Audio thuyết minh**
   - Phát audio theo ngôn ngữ.
   - Ghi nhận sự kiện nghe để phục vụ analytics.
5. **Offline map/audio**
   - Tải dữ liệu dùng offline.
   - Đồng bộ lại khi có mạng.
6. **Đồng bộ POI đúng dữ liệu server (mới sửa)**
   - Xóa POI cũ khỏi local nếu server đã xóa.
   - Hạn chế hiển thị POI stale.
7. **Tối ưu hiệu năng tải dữ liệu (mới sửa)**
   - Throttle full sync POI theo chu kỳ.
   - Giới hạn concurrency các request dịch/audio để giảm lag.
8. **Heartbeat thiết bị (mới thêm)**
   - Gửi heartbeat định kỳ khi app hoạt động.
   - Gửi offline signal khi app sleep/thoát.

---

## 4) Luồng nghiệp vụ quan trọng

### 4.1 Luồng nâng cấp Premium qua MoMo
1. Vendor/Admin vào màn hình Premium.
2. Chọn POI và gói thanh toán.
3. Hệ thống tạo order và ký request gửi MoMo.
4. Người dùng thanh toán qua link/QR MoMo.
5. MoMo gọi IPN về hệ thống.
6. Hệ thống xác thực chữ ký IPN, cập nhật trạng thái đơn.
7. Nếu thành công, POI được gia hạn Premium theo gói.

### 4.2 Luồng thiết bị online trên dashboard
1. App gửi `heartbeat` kèm thông tin thiết bị.
2. Backend lưu `LastSeenUtc` và metadata thiết bị.
3. CMS đọc danh sách thiết bị theo ngưỡng active.
4. Dashboard auto-refresh và hiển thị online/offline gần thời gian thực.
5. Khi app ngủ/thoát, app gọi `offline` để trạng thái đổi nhanh.

### 4.3 Luồng đồng bộ POI chống dữ liệu cũ
1. App gọi API lấy danh sách POI mới.
2. So khớp với local SQLite.
3. Xóa local POI không còn trên server.
4. Upsert POI còn hiệu lực.
5. UI cập nhật theo tập POI mới.

---

## 5) Yêu cầu chức năng (Functional Requirements)

### FR-01 Phân quyền
- Hệ thống phải phân quyền rõ Admin/Vendor.
- Vendor không truy cập được chức năng chỉ dành cho Admin.

### FR-02 Quản lý nội dung
- CMS phải cho phép CRUD POI/Food/Tour.
- Dữ liệu chỉnh sửa phải phản ánh lên app sau đồng bộ.

### FR-03 Đa ngôn ngữ
- Nội dung hiển thị trên app phải theo ngôn ngữ đang chọn.
- Bản dịch và audio phải có cơ chế tạo/cập nhật lại.

### FR-04 Offline-first trên app
- Khi mất mạng, app vẫn xem được dữ liệu đã tải.
- Khi có mạng lại, app đồng bộ dữ liệu và log pending.

### FR-05 Premium payment
- Hệ thống phải tạo được giao dịch MoMo theo POI + gói.
- Trạng thái premium chỉ cập nhật sau khi IPN hợp lệ.

### FR-06 Device presence
- CMS phải hiển thị số thiết bị online theo ngưỡng thời gian.
- Phải có bảng chi tiết thiết bị và trạng thái hoạt động.

### FR-07 Đồng bộ POI chính xác
- POI bị xóa ở server phải bị xóa khỏi local app.
- Không được hiển thị POI cũ sau chu kỳ sync.

---

## 6) Yêu cầu phi chức năng (NFR)
- **Hiệu năng:** thao tác chính trên app phản hồi ổn định, tránh gọi API dồn dập.
- **Độ tin cậy:** có xử lý fallback khi mất mạng; không crash khi API chậm.
- **Bảo mật:** kiểm tra role cho endpoint nhạy cảm; xác thực chữ ký MoMo IPN.
- **Quan sát vận hành:** lưu trạng thái đơn premium, lỗi tích hợp, log presence.
- **Khả năng mở rộng:** tách rõ CMS/API/App để mở rộng độc lập.

---

## 7) Mô hình dữ liệu chính
- Nội dung: `Poi`, `PoiTranslation`, `PoiImage`, `Food`, `FoodTranslation`, `Tour`, `TourPoi`.
- Analytics: `PlayLog`, `HeatmapEntry`, `RouteSession`, `RouteSessionPoi`.
- Premium: `VendorPremiumOrder` + thuộc tính premium trong `Poi`.
- Presence: `DevicePresence`.
- Danh mục và hệ thống: `Language`, Identity tables.

---

## 8) API/Module tiêu biểu theo chức năng mới
- Presence:
  - `POST /api/presence/heartbeat`
  - `POST /api/presence/offline`
- Premium CMS:
  - `GET /Premium`
  - `POST /Premium/CreatePayment`
  - `GET /payment/return`
  - `POST /payment/momo-ipn`
- Dashboard trạng thái thiết bị:
  - `GET /api/cms-dashboard/device-status`

---

## 9) Tiêu chí nghiệm thu
1. **Premium**
   - Tạo link MoMo thành công cho từng gói.
   - IPN hợp lệ cập nhật đơn sang paid và gia hạn POI Premium.
2. **Presence**
   - Mở app: thiết bị lên trạng thái online nhanh.
   - Tắt app/sleep: dashboard cập nhật offline theo ngưỡng cấu hình.
3. **POI sync**
   - Xóa POI trên CMS, app không còn hiển thị POI đó sau sync.
4. **Offline**
   - Offline từ đầu vẫn xem được dữ liệu/map đã tải.
5. **Đa ngôn ngữ**
   - Đổi ngôn ngữ, nội dung chính trên app đổi tương ứng.

---

## 10) Rủi ro và lưu ý vận hành
- URL `ngrok` thay đổi theo phiên có thể làm sai `RedirectUrl/IpnUrl` của MoMo.
- Sai bộ `PartnerCode/AccessKey/SecretKey` gây lỗi chữ ký (`-11007`).
- Nếu process CMS/Backend đang giữ file DLL, build/deploy sẽ lỗi lock file.
- Cần theo dõi giới hạn API bên thứ ba (MoMo, TTS, storage).

---

## 11) Lộ trình đề xuất tiếp theo
1. Hoàn tất checklist e2e MoMo trong môi trường test.
2. Bổ sung màn hình/endpoint debug lịch sử IPN theo order.
3. Chuẩn hóa i18n cho toàn bộ thông báo app còn lại.
4. Bổ sung bộ test hồi quy cho sync POI và offline map.

