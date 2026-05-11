SmartTour — Overlap LogRunner (mô phỏng + file log)
================================================

Mục đích (theo đoạn chat nhóm / thầy):
- Giả lập N thiết bị (DEV-01 … theo N, mặc định N=4) cùng đứng tại điểm giao nhiều POI.
- Ghi log từng bước: POI nào trong bán kính, thứ tự ưu tiên (Premium → heat → gần → thứ tự list),
  quyết định PLAY / HOLD / bỏ qua vì GPS kém / skip / ghim / ra khỏi vùng.

Cách chạy
---------
1) Mở terminal tại thư mục solution SmartTour.
2) Chạy:
   dotnet run --project SmartTour.OverlapLogRunner/SmartTour.OverlapLogRunner.csproj

3) Mặc định file log nằm ngay trong thư mục project SmartTour.OverlapLogRunner (cạnh .csproj):
   SmartTour.OverlapLogRunner/overlap_simulation_log.txt
   (Khi publish / bố cục khác — không thấy .csproj cạnh build — log mặc định về cạnh file exe.)

4) Ghi log ra đường dẫn tùy chọn:
   dotnet run --project SmartTour.OverlapLogRunner/SmartTour.OverlapLogRunner.csproj -- "D:\Logs\overlap_run.txt"

5) Số thiết bị (1–256), thứ tự tham số tùy ý — số nguyên được nhận diện là N; chuỗi còn lại là đường dẫn file:
   dotnet run --project SmartTour.OverlapLogRunner/SmartTour.OverlapLogRunner.csproj -- 7
   dotnet run --project SmartTour.OverlapLogRunner/SmartTour.OverlapLogRunner.csproj -- "D:\Logs\run.txt" 12
   dotnet run --project SmartTour.OverlapLogRunner/SmartTour.OverlapLogRunner.csproj -- 20 "D:\Logs\run.txt"

   Vai trò kịch bản: máy 1 = skip sau tick 1; máy 2 = tick GPS xấu (≥2 máy); máy 3 = ghim (≥3 máy).
   Các máy còn lại chỉ tham gia tick 1 / 2 / 4 như mọi thiết bị khác.

Cách đọc log cho thầy
----------------------
- Dòng "Trong vùng (đã sắp)": thứ tự đúng với app (GeofencingEngine.GetOrderedOverlappingPois + heat).
- "PLAY": tick đó sẽ gọi NarrationEngine.Play với POI đó (mỗi máy một trạng thái skip/ghim riêng).
- "HOLD": cùng POI đầu chuỗi như tick trước — app không gọi Play lại (tránh spam).
- Sau "SKIP": máy đầu (DEV-01) chọn POI kế trong chuỗi ở tick sau nếu còn trong vùng.
- "accuracy > 120": không auto-play (đúng MarketOverlapPlaybackService).

Lưu ý: đây là mô phỏng độc lập (console), không cần máy thật / emulator; logic thứ tự khớp code app,
không chạy MAUI hay NarrationEngine thật.
