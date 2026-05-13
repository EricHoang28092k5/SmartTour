using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourAPI.Data;

namespace SmartTourAPI.Services;

public class PremiumWalletPurchaseService
{
    private readonly AppDbContext _db;
    private readonly VendorWalletService _wallet;

    public PremiumWalletPurchaseService(AppDbContext db, VendorWalletService wallet)
    {
        _db = db;
        _wallet = wallet;
    }

    public async Task<(bool ok, string? error)> TryPurchaseWithWalletAsync(
        int poiId,
        string vendorUserId,
        int months,
        long priceVnd,
        CancellationToken ct = default)
    {
        var okBox = new bool[1];
        var errBox = new string?[1];
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var poi = await _db.Pois.FirstOrDefaultAsync(p => p.Id == poiId, ct);
            if (poi == null)
            {
                errBox[0] = "Không tìm thấy POI.";
                return;
            }

            if (!string.Equals(poi.VendorId, vendorUserId, StringComparison.Ordinal))
            {
                errBox[0] = "POI không thuộc tài khoản của bạn.";
                return;
            }

            var balance = await _wallet.GetBalanceVndAsync(vendorUserId, ct);
            if (balance < priceVnd)
            {
                errBox[0] = "Số dư ví không đủ. Vui lòng nạp thêm.";
                return;
            }

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            if (!await _wallet.TryDebitAsync(vendorUserId, priceVnd, "premium_purchase", $"poi:{poiId}", saveImmediately: false, ct))
            {
                errBox[0] = "Số dư ví không đủ.";
                await tx.RollbackAsync(ct);
                return;
            }

            var now = DateTime.UtcNow;
            var start = poi.PremiumExpiresAt.HasValue && poi.PremiumExpiresAt > now
                ? poi.PremiumExpiresAt.Value
                : now;
            var durationMonths = months;
            poi.IsPremium = true;
            poi.PremiumActivatedAt ??= now;
            poi.PremiumExpiresAt = durationMonths == 0
                ? start.AddDays(7)
                : start.AddMonths(Math.Max(1, durationMonths));

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            okBox[0] = true;
        });

        return (okBox[0], errBox[0]);
    }
}
