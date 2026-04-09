using System;
using System.Collections.Generic;

namespace SmartTourApp.Services.Offline
{
    /// <summary>
    /// TileAreaCalculator — Tính toán danh sách tile (z, x, y) cần tải
    /// cho một vùng địa lý xác định bởi tâm + bán kính.
    ///
    /// Dùng hệ tọa độ tile Web Mercator (OSM standard):
    /// https://wiki.openstreetmap.org/wiki/Slippy_map_tilenames
    /// </summary>
    public static class TileAreaCalculator
    {
        public record TileCoord(int Z, int X, int Y);

        /// <summary>
        /// Trả về tất cả tile trong khu vực tròn với bán kính cho trước.
        /// </summary>
        public static List<TileCoord> GetTilesInArea(
            double centerLat, double centerLng,
            double radiusKm,
            int minZoom, int maxZoom)
        {
            var result = new List<TileCoord>();

            for (int z = minZoom; z <= maxZoom; z++)
            {
                // Tính bounding box từ tâm + bán kính
                var (north, south, east, west) =
                    GetBoundingBox(centerLat, centerLng, radiusKm);

                var (xMin, yMin) = LonLatToTile(west, north, z);
                var (xMax, yMax) = LonLatToTile(east, south, z);

                for (int x = xMin; x <= xMax; x++)
                    for (int y = yMin; y <= yMax; y++)
                        result.Add(new TileCoord(z, x, y));
            }

            return result;
        }

        /// <summary>
        /// Tính bounding box hình chữ nhật từ tâm + bán kính (km).
        /// Đơn giản hóa: 1 độ lat ≈ 111km, 1 độ lng ≈ 111km * cos(lat)
        /// </summary>
        public static (double North, double South, double East, double West)
            GetBoundingBox(double lat, double lng, double radiusKm)
        {
            double latDelta = radiusKm / 111.0;
            double lngDelta = radiusKm / (111.0 * Math.Cos(lat * Math.PI / 180.0));

            return (
                North: Math.Min(85.0511, lat + latDelta),
                South: Math.Max(-85.0511, lat - latDelta),
                East: Math.Min(180.0, lng + lngDelta),
                West: Math.Max(-180.0, lng - lngDelta)
            );
        }

        /// <summary>
        /// Chuyển lon/lat sang tile coordinate cho zoom level z.
        /// </summary>
        public static (int X, int Y) LonLatToTile(double lon, double lat, int z)
        {
            int n = 1 << z;
            double latRad = lat * Math.PI / 180.0;

            int x = (int)Math.Floor((lon + 180.0) / 360.0 * n);
            int y = (int)Math.Floor(
                (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI)
                / 2.0 * n);

            // Clamp về phạm vi hợp lệ
            x = Math.Clamp(x, 0, n - 1);
            y = Math.Clamp(y, 0, n - 1);

            return (x, y);
        }

        /// <summary>
        /// Chuyển tile coordinate sang lon/lat (góc trên-trái của tile).
        /// </summary>
        public static (double Lon, double Lat) TileToLonLat(int x, int y, int z)
        {
            int n = 1 << z;
            double lon = x / (double)n * 360.0 - 180.0;
            double latRad = Math.Atan(Math.Sinh(Math.PI * (1 - 2.0 * y / n)));
            double lat = latRad * 180.0 / Math.PI;
            return (lon, lat);
        }

        /// <summary>
        /// Ước tính tổng số tiles cho một khu vực (để hiển thị trước khi tải).
        /// </summary>
        public static int EstimateTileCount(double radiusKm, int minZoom, int maxZoom)
        {
            int total = 0;
            for (int z = minZoom; z <= maxZoom; z++)
            {
                // Ước tính: mỗi zoom tăng 1, số tile tăng 4x, nhưng tính theo diện tích
                double tilesPerKm = Math.Pow(2, z) / (40075.0 / 360.0);
                double areaTiles = Math.PI * (radiusKm * tilesPerKm) * (radiusKm * tilesPerKm);
                total += (int)Math.Ceiling(areaTiles);
            }
            return total;
        }

        /// <summary>
        /// Tính dung lượng ước tính (bytes) — mỗi tile OSM trung bình ~15KB.
        /// </summary>
        public static long EstimateSizeBytes(int tileCount, int avgTileSizeKB = 15)
            => (long)tileCount * avgTileSizeKB * 1024;
    }
}
