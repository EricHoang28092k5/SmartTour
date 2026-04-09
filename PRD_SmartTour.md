# PRD SmartTour (Bản đầy đủ mô hình chức năng)

| Thuộc tính | Giá trị |
| --- | --- |
| Phiên bản | 6.1 |
| Ngày cập nhật | 2026-04-09 |
| Mục tiêu tài liệu | Giữ đúng 22 mục và mô hình chi tiết cho đầy đủ chức năng đồ án |

## Mục lục nhanh
1. [Giới thiệu](#1-giới-thiệu)
2. [Mục tiêu sản phẩm](#2-mục-tiêu-sản-phẩm)
3. [Personas](#3-personas)
4. [Tính năng chi tiết](#4-tính-năng-chi-tiết)
5. [User stories](#5-user-stories)
6. [Luồng người dùng chính](#6-luồng-người-dùng-chính)
7. [Kiến trúc hệ thống](#7-kiến-trúc-hệ-thống)
8. [CSDL](#8-csdl)
9. [Thiết kế analytics](#9-thiết-kế-analytics)
10. [Yêu cầu phi chức năng (NFR)](#10-yêu-cầu-phi-chức-năng-nfr)
11. [Sơ đồ Use Case](#11-sơ-đồ-use-case)
12. [Sequence diagram](#12-sequence-diagram)
13. [Activity diagram](#13-activity-diagram)
14. [Data Flow Diagram (DFD Level 1)](#14-data-flow-diagram-dfd-level-1)
15. [UI wireframe (MVP)](#15-ui-wireframe-mvp)
16. [API thực tế](#16-api-thực-tế)
17. [Bảo mật](#17-bảo-mật)
18. [Roadmap](#18-roadmap)
19. [Nghiệm thu](#19-nghiệm-thu)
20. [Future](#20-future)
21. [Danh mục tài liệu & mã tham chiếu](#21-danh-mục-tài-liệu--mã-tham-chiếu)
22. [Lịch sử phiên bản PRD](#22-lịch-sử-phiên-bản-prd)

---

## 1. Giới thiệu
SmartTour là hệ thống du lịch thông minh gồm 3 thành phần:
- **Mobile App (.NET MAUI):** bản đồ, POI, audio đa ngôn ngữ, offline map/audio, QR gate.
- **CMS Web (ASP.NET Core MVC):** quản trị POI/Tour/Translation, tạo audio, thống kê.
- **Backend API (ASP.NET Core + EF Core):** cung cấp dữ liệu, xử lý audio, lưu analytics.

Mục tiêu cốt lõi: giúp khách du lịch tự khám phá địa điểm bằng nội dung số đa ngôn ngữ, vận hành ổn định cả khi online lẫn offline.

## 2. Mục tiêu sản phẩm
### 2.1 Mục tiêu nghiệp vụ
- Số hóa hướng dẫn tham quan bằng POI + audio.
- Chuẩn hóa quy trình quản lý nội dung tập trung.
- Tăng mức độ tương tác người dùng thông qua bản đồ, tour và audio.
- Theo dõi hành vi để cải tiến nội dung bằng dữ liệu thực tế.

### 2.2 Mục tiêu kỹ thuật
- API nhất quán cho CMS và App.
- Dữ liệu POI/Tour có bản dịch theo ngôn ngữ, đồng bộ audio theo translation.
- Hỗ trợ bắt buộc quét QR khi vào hệ thống, kèm cơ chế phiên 7 ngày.
- Hỗ trợ hoạt động offline map/audio và đồng bộ lại khi online.

## 3. Personas
- **Traveler (Khách du lịch):** cần truy cập nhanh, xem bản đồ, nghe audio theo ngôn ngữ của mình, không bị gián đoạn khi mất mạng.
- **Vendor (Đơn vị nội dung):** tạo và quản lý POI/Tour trong phạm vi phụ trách, theo dõi nội dung và audio.
- **Admin (Quản trị hệ thống):** kiểm soát người dùng, phân quyền, giám sát chất lượng dữ liệu và analytics.

## 4. Tính năng chi tiết
### 4.1 Mobile App
- Đăng nhập luồng vào app qua **QR Gate** (quét QR hợp lệ mới vào hệ thống).
- Lưu phiên quét QR 7 ngày, hết hạn thì quét lại.
- Hỗ trợ deep link: `smarttour://poi/{id}`, `smarttour://tour/{id}`.
- Xem POI theo map/list, xem chi tiết và nghe audio theo translation.
- Theo dõi route, gửi log nghe, thống kê hành vi.
- Chạy offline với cache map tile và audio local.
- Đồng bộ dữ liệu pending sau khi có mạng.

### 4.2 CMS
- CRUD POI đầy đủ (thông tin, mô tả, tọa độ, ảnh, danh mục).
- CRUD Tour, gán/đổi thứ tự POI trong tour.
- Tự sinh translation theo bảng `Languages` khi tạo POI.
- Sinh audio theo từng translation (từ `TtsScript`/`Description`).
- Regenerate audio theo POI hoặc từng translation.
- Hiển thị QR cho POI/Tour để mở trực tiếp app.
- Màn hình translation để nghe kiểm thử audio từng ngôn ngữ.
- Dashboard Heatmap và route phổ biến.

### 4.3 Backend API
- Cụm API POI/Tour/Food/Translation.
- Cụm API Audio: xem, sinh, sinh lại.
- Cụm API analytics: playlog, heatmap, route session.
- Tích hợp Azure Speech để tổng hợp giọng nói.
- Tích hợp Cloudinary để lưu audio và trả URL.

## 5. User stories
- **US-01:** Là Traveler, tôi muốn quét QR để mở app trực tiếp và vào nhanh màn hình chính.
- **US-02:** Là Traveler, tôi muốn nghe thuyết minh theo ngôn ngữ đang chọn.
- **US-03:** Là Traveler, tôi muốn app vẫn dùng được khi mất mạng.
- **US-04:** Là Traveler, tôi muốn quét mã của POI/Tour và đi thẳng đến nội dung đó.
- **US-05:** Là Vendor, tôi muốn tạo POI một lần và hệ thống tự tạo translation + audio.
- **US-06:** Là Vendor, tôi muốn regenerate audio cho bản dịch bị thiếu/lỗi.
- **US-07:** Là Admin, tôi muốn xem heatmap và route để đánh giá mức độ quan tâm.
- **US-08:** Là Admin, tôi muốn phân quyền rõ ràng cho người vận hành.

## 6. Luồng người dùng chính
### Journey 1: Tạo POI và tự động sinh audio
1. Vendor/Admin tạo POI trên CMS.
2. Backend lấy danh sách ngôn ngữ, tạo `PoiTranslation`.
3. Hệ thống gọi TTS để tạo file âm thanh cho từng bản dịch.
4. Upload audio lên Cloudinary, lưu `AudioUrl`.
5. CMS hiển thị trạng thái tạo thành công.

### Journey 2: Nghe audio POI
1. Traveler mở app, chọn POI trên map/list.
2. App lấy translation đúng ngôn ngữ.
3. App phát audio cloud hoặc audio local cache.
4. App gửi playlog về backend.

### Journey 3: QR Gate + Deep Link
1. App khởi động, kiểm tra phiên QR còn hạn hay không.
2. Nếu hết hạn: mở trang quét QR.
3. Quét hợp lệ: lưu phiên 7 ngày.
4. Nếu QR chứa deep link POI/Tour: điều hướng đúng màn hình đích.

### Journey 4: Offline và đồng bộ lại
1. App tải trước map tile + audio.
2. Khi offline: đọc dữ liệu local.
3. Log phát audio/route được queue lại.
4. Khi online: đẩy pending queue lên API.

## 7. Kiến trúc hệ thống
```mermaid
flowchart LR
    Traveler[Traveler] --> App[SmartTour App]
    Vendor[Vendor/Admin] --> CMS[SmartTour CMS]
    App --> API[SmartTour Backend API]
    CMS --> API
    API --> PG[(PostgreSQL)]
    API --> Cloud[(Cloudinary Storage)]
    API --> Azure[(Azure Speech Service)]
    App --> Local[(SQLite + File Cache)]
    App --> Android[Android Intent Filter smarttour://]
```

## 8. CSDL
### 8.1 Nhóm bảng nghiệp vụ
- `Poi`
- `PoiTranslation`
- `PoiImage`
- `Category`
- `Language`
- `Tour`
- `TourPoi`
- `TourTranslation`
- `Food`
- `PlayLog`
- `HeatmapEntry`
- `RouteSession`
- `RouteSessionPoi`
- `QrCode` (nếu đang dùng trong nhánh hiện tại)

### 8.2 Nhóm bảng Identity
- `AspNetUsers`
- `AspNetRoles`
- `AspNetUserRoles`
- `AspNetUserClaims`
- `AspNetRoleClaims`
- `AspNetUserLogins`
- `AspNetUserTokens`

### 8.3 ERD chi tiết (đầy đủ chức năng chính)
```mermaid
erDiagram
    CATEGORY {
      int Id PK
      string Name
      string Description
    }

    POI {
      int Id PK
      string Name
      string Description
      decimal Latitude
      decimal Longitude
      int CategoryId FK
      datetime CreatedAt
      datetime UpdatedAt
    }

    POI_IMAGE {
      int Id PK
      int PoiId FK
      string ImageUrl
      bool IsPrimary
    }

    LANGUAGE {
      int Id PK
      string Code
      string Name
      bool IsActive
    }

    POI_TRANSLATION {
      int Id PK
      int PoiId FK
      int LanguageId FK
      string Title
      string Description
      string TtsScript
      string AudioUrl
      datetime UpdatedAt
    }

    TOUR {
      int Id PK
      string Name
      string Description
      bool IsActive
      datetime CreatedAt
    }

    TOUR_POI {
      int Id PK
      int TourId FK
      int PoiId FK
      int SortOrder
    }

    TOUR_TRANSLATION {
      int Id PK
      int TourId FK
      int LanguageId FK
      string Name
      string Description
    }

    FOOD {
      int Id PK
      string Name
      string Description
      decimal Latitude
      decimal Longitude
    }

    PLAY_LOG {
      int Id PK
      int PoiId FK
      string DeviceId
      string LanguageCode
      datetime PlayedAt
      int DurationSec
    }

    HEATMAP_ENTRY {
      int Id PK
      int PoiId FK
      decimal Latitude
      decimal Longitude
      datetime CreatedAt
    }

    ROUTE_SESSION {
      long Id PK
      string DeviceId
      datetime StartedAt
      datetime EndedAt
      double TotalDistanceKm
    }

    ROUTE_SESSION_POI {
      long Id PK
      long RouteSessionId FK
      int PoiId FK
      datetime VisitedAt
      int OrderNo
    }

    ASPNET_USERS {
      string Id PK
      string UserName
      string Email
    }

    ASPNET_ROLES {
      string Id PK
      string Name
    }

    ASPNET_USER_ROLES {
      string UserId FK
      string RoleId FK
    }

    ASPNET_USER_CLAIMS {
      int Id PK
      string UserId FK
      string ClaimType
      string ClaimValue
    }

    ASPNET_ROLE_CLAIMS {
      int Id PK
      string RoleId FK
      string ClaimType
      string ClaimValue
    }

    ASPNET_USER_LOGINS {
      string LoginProvider PK
      string ProviderKey PK
      string UserId FK
    }

    ASPNET_USER_TOKENS {
      string UserId FK
      string LoginProvider PK
      string Name PK
      string Value
    }

    CATEGORY ||--o{ POI : groups
    POI ||--o{ POI_IMAGE : has
    POI ||--o{ POI_TRANSLATION : has
    LANGUAGE ||--o{ POI_TRANSLATION : localizes
    TOUR ||--o{ TOUR_POI : contains
    POI ||--o{ TOUR_POI : included_in
    TOUR ||--o{ TOUR_TRANSLATION : has
    LANGUAGE ||--o{ TOUR_TRANSLATION : localizes
    POI ||--o{ PLAY_LOG : generates
    POI ||--o{ HEATMAP_ENTRY : contributes
    ROUTE_SESSION ||--o{ ROUTE_SESSION_POI : tracks
    POI ||--o{ ROUTE_SESSION_POI : visited
    ASPNET_USERS ||--o{ ASPNET_USER_ROLES : maps
    ASPNET_ROLES ||--o{ ASPNET_USER_ROLES : maps
    ASPNET_USERS ||--o{ ASPNET_USER_CLAIMS : has
    ASPNET_ROLES ||--o{ ASPNET_ROLE_CLAIMS : has
    ASPNET_USERS ||--o{ ASPNET_USER_LOGINS : has
    ASPNET_USERS ||--o{ ASPNET_USER_TOKENS : has
```

## 9. Thiết kế analytics
### 9.1 Chỉ số theo dõi
- Lượt nghe theo POI, theo ngôn ngữ, theo khung giờ.
- Điểm nóng truy cập theo tọa độ (heatmap).
- Lộ trình phổ biến theo route session.
- Tỷ lệ dùng offline và đồng bộ thành công.

### 9.2 Nguồn dữ liệu
- `PlayLog` cho hành vi nghe audio.
- `HeatmapEntry` cho mật độ quan tâm khu vực.
- `RouteSession` + `RouteSessionPoi` cho hành trình.

### 9.3 Dashboard đề xuất
- Top POI theo ngày/tuần/tháng.
- Biểu đồ ngôn ngữ được nghe nhiều nhất.
- Heatmap theo mốc thời gian.
- Top tour có tỷ lệ hoàn thành cao.

## 10. Yêu cầu phi chức năng (NFR)
- **Hiệu năng API:** truy vấn phổ biến < 2 giây.
- **Độ ổn định app:** không ANR khi quét QR liên tục.
- **Khả dụng offline:** map/audio dùng được khi không có mạng.
- **Đồng bộ:** retry queue an toàn khi mạng chập chờn.
- **Bảo mật:** secrets lưu env, phân quyền theo role.
- **Khả năng mở rộng:** thêm ngôn ngữ mới không cần đổi kiến trúc.

## 11. Sơ đồ Use Case
### 11.1 Use Case tổng quan đầy đủ chức năng
```mermaid
flowchart LR
    Traveler((Traveler))
    Vendor((Vendor))
    Admin((Admin))
    System((Hệ thống SmartTour))

    subgraph MobileApp[Mobile App]
      UC1[UC1 Quét QR để vào app]
      UC2[UC2 Mở POI bằng deep link]
      UC3[UC3 Xem POI trên map/list]
      UC4[UC4 Nghe audio theo ngôn ngữ]
      UC5[UC5 Dùng offline map/audio]
      UC6[UC6 Gửi playlog và route]
      UC7[UC7 Xóa phiên QR trong Settings]
    end

    subgraph CMSWeb[CMS Web]
      UC8[UC8 Đăng nhập và phân quyền]
      UC9[UC9 CRUD POI]
      UC10[UC10 CRUD Tour và gán POI]
      UC11[UC11 Quản lý Translation]
      UC12[UC12 Generate/Regenerate audio]
      UC13[UC13 Xem Heatmap và Route]
      UC14[UC14 Xuất QR cho POI/Tour]
      UC15[UC15 Quản lý người dùng hệ thống]
    end

    subgraph BackendAPI[Backend API]
      UC16[UC16 Trả API POI/Tour]
      UC17[UC17 Xử lý Audio Service]
      UC18[UC18 Lưu Analytics]
      UC19[UC19 Đồng bộ dữ liệu offline]
    end

    Traveler --> UC1
    Traveler --> UC2
    Traveler --> UC3
    Traveler --> UC4
    Traveler --> UC5
    Traveler --> UC6
    Traveler --> UC7

    Vendor --> UC8
    Vendor --> UC9
    Vendor --> UC10
    Vendor --> UC11
    Vendor --> UC12
    Vendor --> UC13
    Vendor --> UC14

    Admin --> UC8
    Admin --> UC9
    Admin --> UC10
    Admin --> UC11
    Admin --> UC12
    Admin --> UC13
    Admin --> UC14
    Admin --> UC15

    UC9 --> UC16
    UC10 --> UC16
    UC12 --> UC17
    UC6 --> UC18
    UC5 --> UC19
    UC13 --> UC18

    System --- UC16
    System --- UC17
    System --- UC18
    System --- UC19
```

## 12. Sequence diagram
### 12.1 Sequence: Tạo POI -> sinh translation -> sinh audio
```mermaid
sequenceDiagram
    actor VA as Vendor/Admin
    participant CMS as SmartTour CMS
    participant API as Backend API
    participant DB as PostgreSQL
    participant TTS as Azure Speech
    participant CLD as Cloudinary

    VA->>CMS: Nhập form tạo POI
    CMS->>API: POST /api/poi
    API->>DB: Lưu POI
    API->>DB: Lấy Languages đang active
    loop Mỗi ngôn ngữ
        API->>DB: Tạo PoiTranslation mặc định
        API->>TTS: Tổng hợp giọng nói theo TtsScript/Description
        TTS-->>API: Audio stream
        API->>CLD: Upload file audio
        CLD-->>API: AudioUrl
        API->>DB: Cập nhật AudioUrl vào PoiTranslation
    end
    API-->>CMS: Trả kết quả tạo POI thành công
```

### 12.2 Sequence: Người dùng nghe audio (online/offline fallback)
```mermaid
sequenceDiagram
    actor U as Traveler
    participant APP as Mobile App
    participant CACHE as Local Cache
    participant API as Backend API

    U->>APP: Mở màn hình chi tiết POI
    APP->>API: GET /api/pois/{id}/tts-all
    alt Online và có AudioUrl
        API-->>APP: Danh sách translation + AudioUrl
        APP->>APP: Chọn bản dịch đúng ngôn ngữ
        APP->>APP: Phát audio cloud
        APP->>CACHE: Lưu audio để dùng offline
    else Offline hoặc AudioUrl lỗi
        APP->>CACHE: Tìm audio local theo translation
        alt Có file local
            CACHE-->>APP: Trả file audio local
            APP->>APP: Phát audio local
        else Không có file local
            APP->>APP: Fallback TTS cục bộ hoặc thông báo
        end
    end
    APP->>API: POST /api/pois/playlog
```

### 12.3 Sequence: QR Gate + Deep Link POI/Tour
```mermaid
sequenceDiagram
    actor U as User
    participant LD as LoadingPage
    participant PREF as Preferences
    participant QR as QrGatePage
    participant DL as DeepLinkService
    participant NAV as Navigation

    U->>LD: Mở ứng dụng
    LD->>PREF: Đọc QrGateUntil
    alt Session còn hạn
        LD->>NAV: Điều hướng HomePage
    else Session hết hạn/chưa có
        LD->>QR: Mở camera scan QR
        U->>QR: Quét mã smarttour://...
        alt QR hợp lệ
            QR->>PREF: Lưu hạn 7 ngày
            QR->>DL: Publish deeplink (poi/tour)
            QR->>NAV: Về HomePage
            DL->>NAV: Điều hướng đến trang đích
        else QR không hợp lệ
            QR-->>U: Báo lỗi và yêu cầu quét lại
        end
    end
```

### 12.4 Sequence: Đồng bộ offline khi có mạng lại
```mermaid
sequenceDiagram
    participant APP as Mobile App
    participant Q as Pending Queue
    participant API as Backend API
    participant DB as PostgreSQL

    APP->>Q: Lưu playlog/route khi offline
    APP->>APP: Theo dõi trạng thái mạng
    APP->>Q: Đọc danh sách pending khi online
    loop Mỗi bản ghi pending
        APP->>API: POST dữ liệu analytics
        API->>DB: Lưu dữ liệu
        API-->>APP: 200 OK
        APP->>Q: Đánh dấu đã sync
    end
    APP->>Q: Dọn queue đã đồng bộ
```

### 12.5 Sequence: Regenerate audio theo translation
```mermaid
sequenceDiagram
    actor A as Admin/Vendor
    participant CMS as CMS
    participant API as Backend API
    participant DB as PostgreSQL
    participant TTS as Azure Speech
    participant CLD as Cloudinary

    A->>CMS: Bấm Regenerate audio
    CMS->>API: POST /api/audio/translation/{id}/generate
    API->>DB: Lấy translation hiện tại
    API->>TTS: Tổng hợp audio mới
    TTS-->>API: Audio stream
    API->>CLD: Upload audio mới
    CLD-->>API: New AudioUrl
    API->>DB: Cập nhật AudioUrl
    API-->>CMS: Trả kết quả thành công
```

## 13. Activity diagram
### 13.1 Activity: Luồng vào app bằng QR Gate
```mermaid
flowchart TD
    A[Khởi động app] --> B{Phiên QR còn hạn?}
    B -- Có --> C[Đi thẳng HomePage]
    B -- Không --> D[Mở QrGatePage]
    D --> E[Quét mã QR]
    E --> F{QR hợp lệ?}
    F -- Không --> G[Hiển thị lỗi]
    G --> E
    F -- Có --> H[Lưu phiên 7 ngày]
    H --> I{Có deep link POI/Tour?}
    I -- Có --> J[Điều hướng màn hình đích]
    I -- Không --> C
    J --> K[Hiển thị nội dung]
    C --> K
```

### 13.2 Activity: Nghe audio POI đầy đủ nhánh
```mermaid
flowchart TD
    A[User mở POI detail] --> B{Có mạng?}
    B -- Có --> C[Gọi API lấy translation/audio]
    C --> D{AudioUrl hợp lệ?}
    D -- Có --> E[Phát audio cloud]
    D -- Không --> F[Đọc local cache]
    B -- Không --> F
    F --> G{Có audio local?}
    G -- Có --> H[Phát audio local]
    G -- Không --> I[Fallback local TTS/thông báo]
    E --> J[Ghi playlog]
    H --> J
    I --> J
    J --> K[Cập nhật giao diện]
```

### 13.3 Activity: Tạo POI trong CMS
```mermaid
flowchart TD
    A[Vendor/Admin vào form POI] --> B[Nhập dữ liệu]
    B --> C{Dữ liệu hợp lệ?}
    C -- Không --> D[Hiển thị lỗi validation]
    D --> B
    C -- Có --> E[Lưu POI]
    E --> F[Tạo translation theo Languages]
    F --> G[Tạo audio từng translation]
    G --> H[Lưu AudioUrl]
    H --> I[Trả kết quả thành công]
```

### 13.4 Activity: Đồng bộ dữ liệu offline
```mermaid
flowchart TD
    A[App chạy offline] --> B[Lưu pending log/route]
    B --> C[Theo dõi trạng thái mạng]
    C --> D{Online trở lại?}
    D -- Chưa --> C
    D -- Rồi --> E[Đẩy từng bản ghi lên API]
    E --> F{API thành công?}
    F -- Không --> G[Giữ lại pending để retry]
    G --> C
    F -- Có --> H[Đánh dấu synced]
    H --> I{Còn pending?}
    I -- Còn --> E
    I -- Hết --> J[Kết thúc đồng bộ]
```

## 14. Data Flow Diagram (DFD Level 1)
```mermaid
flowchart LR
    Traveler[Traveler]
    Vendor[Vendor/Admin]

    P1((P1 Xác thực vào app bằng QR))
    P2((P2 Tiêu thụ nội dung POI/Tour))
    P3((P3 Ghi nhận analytics))
    P4((P4 Quản trị nội dung CMS))
    P5((P5 Tạo/Sinh lại audio))
    P6((P6 Đồng bộ dữ liệu offline))

    D1[(D1 Session QR 7 ngày)]
    D2[(D2 Master Data POI/Tour/Language)]
    D3[(D3 Audio URLs + Media)]
    D4[(D4 PlayLog/Heatmap/Route)]
    D5[(D5 Local cache map/audio)]

    EXT1[(Azure Speech)]
    EXT2[(Cloudinary)]
    EXT3[(PostgreSQL)]

    Traveler --> P1
    Traveler --> P2
    Traveler --> P3
    Traveler --> P6
    Vendor --> P4
    Vendor --> P5

    P1 <--> D1
    P2 <--> D2
    P2 <--> D3
    P2 <--> D5
    P3 <--> D4
    P4 <--> D2
    P5 <--> D3
    P6 <--> D4
    P6 <--> D5

    P4 <--> EXT3
    P5 <--> EXT1
    P5 <--> EXT2
    P3 <--> EXT3
    P2 <--> EXT3
```

## 15. UI wireframe (MVP)
### 15.1 App Mobile
- **LoadingPage:** kiểm tra phiên QR và điều hướng đầu vào.
- **QrGatePage:** camera scan QR, thông báo hợp lệ/không hợp lệ.
- **HomePage:** vào nhanh map, tour, cài đặt.
- **MapPage:** bản đồ + marker POI + vị trí hiện tại.
- **PoiListPage:** danh sách POI có lọc/sắp xếp.
- **PoiDetailPage:** nội dung POI, bản dịch, play/pause audio.
- **TourPage:** danh sách tour, hỗ trợ mở tour theo deep link.
- **SettingsPage:** nút xóa phiên QR 7 ngày để test.

### 15.2 CMS Web
- **Login/Role UI:** đăng nhập, phân quyền.
- **POI/Index:** danh sách POI, QR code từng POI.
- **POI/Create/Edit:** thông tin chính, mô tả, tọa độ, ảnh.
- **Tour/Index & Details:** CRUD tour, số POI trong tour.
- **Translation/Details:** xem bản dịch và nghe audio từng ngôn ngữ.
- **Heatmap/Index:** bản đồ nhiệt và số liệu quan tâm.

## 16. API thực tế
### 16.1 POI
- `GET /api/pois`
- `GET /api/pois/{poiId}`
- `GET /api/pois/{poiId}/tts-all`
- `POST /api/pois/playlog`
- `GET /api/pois/stats`

### 16.2 Audio
- `GET /api/audio/poi/{poiId}`
- `POST /api/audio/poi/{poiId}/regenerate`
- `POST /api/audio/translation/{translationId}/generate`

### 16.3 Tour
- `GET /api/tours`
- `GET /api/tours/{id}`

### 16.4 Route/Heatmap
- `POST /api/routes/session`
- `GET /api/routes/popular`
- `POST /api/heatmap/entry`
- `GET /api/heatmap`

## 17. Bảo mật
- Dùng Identity để xác thực và phân quyền role `Admin`, `Vendor`.
- Không commit key thật (`Cloudinary`, `Azure Speech`, DB).
- Dùng `.env`/biến môi trường theo từng máy.
- Validate dữ liệu đầu vào cho API (QR payload, ids, content).
- Hạn chế truy cập API nhạy cảm bằng role và policy.

## 18. Roadmap
- **P1 (Hoàn thành):** nền tảng CRUD POI/Tour/Translation.
- **P2 (Hoàn thành):** sinh audio tự động + regenerate.
- **P3 (Hoàn thành):** QR gate + deep link POI/Tour.
- **P4 (Hoàn thành):** offline map/audio + sync.
- **P5 (Kế tiếp):** tối ưu dashboard analytics và bộ lọc báo cáo.

## 19. Nghiệm thu
Checklist nghiệm thu theo chức năng:
- [ ] Tạo POI sinh đủ translation theo `Languages`.
- [ ] Audio được tạo và lưu `AudioUrl` cho từng translation.
- [ ] `GET /api/tours` trả đủ POI trong tour.
- [ ] QR hợp lệ mở app và vào HomePage ổn định.
- [ ] Session QR có hiệu lực 7 ngày, có thể xóa từ Settings.
- [ ] App không ANR khi quét QR.
- [ ] Offline map/audio chạy được khi tắt mạng.
- [ ] Pending queue được sync lại khi có mạng.

## 20. Future
- Gợi ý lịch trình cá nhân hóa theo sở thích.
- Tải trước gói dữ liệu thông minh theo khu vực sắp đến.
- Đề xuất ngôn ngữ/voice profile theo người dùng.
- Chấm điểm chất lượng tour dựa trên analytics.

## 21. Danh mục tài liệu & mã tham chiếu
- Tài liệu chính: `PRD_SmartTour.md`
- Hướng dẫn báo cáo Web: `HUONG_DAN_BAO_CAO_WEB.md`
- Hướng dẫn báo cáo App: `HUONG_DAN_BAO_CAO_APP.md`
- Source code:
  - `SmartTourApp/`
  - `SmartTourCMS/`
  - `SmartTourBackend/`
  - `SmartTour.Shared/`

## 22. Lịch sử phiên bản PRD
| Phiên bản | Ngày | Nội dung cập nhật |
| --- | --- | --- |
| 5.0 | 2026-04-09 | Bản rút gọn để thuyết trình nhanh |
| 6.0 | 2026-04-09 | Chuẩn hóa 22 mục |
| 6.1 | 2026-04-09 | Khôi phục tiếng Việt có dấu + mở rộng đầy đủ mô hình ERD/Use Case/Sequence/Activity/DFD |
