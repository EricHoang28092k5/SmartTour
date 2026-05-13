# SmartTour — NFR (Geofence Simulator & LogRunner)

Thư mục này bổ sung **kiểm thử tải CLI** (k6) và **script minh chứng** cho luồng `POST /api/analytics/visit`, bổ trợ (không thay thế) trang CMS **Geofence Simulator** + hàng đợi file **`ILogRunner`** (`logqueue.txt`).

## Điều kiện

- [k6](https://k6.io/docs/get-started/installation/) (tuỳ chọn nhưng khuyến nghị cho NFR).
- **SmartTourAPI** chạy, DB kết nối được (`GET /api/health`).
- Biết **PoiId** thật (active) cho script visit.

## Rate limit

`POST /api/analytics/visit` dùng policy **DeviceTokenPolicy**: khoảng **60 request / 10 giây / IP**. k6 chạy từ một máy = một IP → các scenario `smarttour-visit-arrival.js` dùng **constant-arrival-rate** mặc định **45/10s** để còn dư địa. Vượt ngưỡng sẽ thấy **HTTP 429**.

**Bulk visit song song cao** từ CMS (`LoadTest/RunBulkVisit`) đi qua **HttpClient phía server** tới API — IP nguồn là máy chạy CMS/API, không phải trình duyệt; dùng để stress khác với k6.

## Script k6

| File | Mô tả |
|------|--------|
| `k6-common.js` | Base URL, header JSON, `postLogVisit`, `getHealth`. |
| `smarttour-smoke.js` | Health + (tuỳ chọn) vài visit nếu có `-e POI_ID=`. |
| `smarttour-visit-arrival.js` | Tải visit theo tốc độ cố định; **bắt buộc** `POI_ID`. |
| `smarttour-visit-geofence.js` | Visit `visitType=0` (Geofence), tọa độ random trong bbox VN. |

Ví dụ (đổi port HTTPS dev của bạn):

```bash
k6 run -e BASE_URL=https://localhost:7123 -e POI_ID=1 tests/nfr/smarttour-smoke.js
k6 run -e BASE_URL=https://localhost:7123 -e POI_ID=1 tests/nfr/smarttour-visit-arrival.js
k6 run -e BASE_URL=https://localhost:7123 -e POI_ID=1 -e ARRIVAL_PER_10S=50 -e DURATION=2m tests/nfr/smarttour-visit-arrival.js
k6 run -e BASE_URL=https://localhost:7123 -e POI_ID=1 tests/nfr/smarttour-visit-geofence.js
```

Ngưỡng mặc định (giống hướng dẫn StreetFood tham chiếu): `http_req_failed < 2%`, `p95(latency) < 800ms` cho response thành công.

## PowerShell

Từ **thư mục gốc repo** SmartTour:

```powershell
powershell -ExecutionPolicy Bypass -File tests/nfr/evidence-geofence-log.ps1 -BaseUrl https://localhost:7123 -PoiId 1 -VisitCount 30
```

Kết quả: `tests/nfr/results/smarttour-geofence-evidence-*.txt`.

Chạy tuần tự ba mức tốc độ arrival (25 / 45 / 58 mỗi 10s) — cần `k6` trong `PATH`:

```powershell
powershell -ExecutionPolicy Bypass -File tests/nfr/run-capacity.ps1 -BaseUrl https://localhost:7123 -PoiId 1
```

## CMS (Admin) — Geofence Simulator & LogRunner

Đăng nhập CMS với role **Admin**:

- **Load test** → **Geofence Simulator**: bản đồ Leaflet, proxy visit (`SIM-DEV-xx`), poll **visit_logs**, lưu log client qua `POST .../SaveSimulatorLog`, tải `logqueue.txt`.
- Trang **Hướng dẫn NFR & k6** (menu Load test): `LoadTest/NfrGuide`.

Cấu hình file log: `appsettings.json` → `LogRunner:FilePath` (không set thì `%TEMP%\SmartTour\logqueue.txt`).

## Console — OverlapLogRunner

Mô phỏng offline ưu tiên POI / telemetry (không gọi API):

```bash
dotnet run --project SmartTour.OverlapLogRunner -- 8
```

Tham số cuối: số thiết bị (1–256). Chi tiết xem `README.txt` trong project runner.
