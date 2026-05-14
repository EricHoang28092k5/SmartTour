using Microsoft.EntityFrameworkCore;
using SmartTour.Shared.Models;
using SmartTourAPI.Data;

namespace SmartTourAPI.Services;

public class VendorWalletService
{
    private readonly AppDbContext _db;

    public VendorWalletService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<long> GetBalanceVndAsync(string vendorUserId, CancellationToken ct = default)
    {
        // Nếu vendorUserId trống, trả về 0 thay vì ném lỗi để tránh lỗi không mong muốn ở caller.
        if (string.IsNullOrWhiteSpace(vendorUserId)) return 0;
        //
        var row = await _db.VendorWallets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.VendorUserId == vendorUserId, ct);
        return row?.BalanceVnd ?? 0;
    }

    public async Task CreditAsync(string vendorUserId, long amountVnd, string kind, string? reference, bool saveImmediately = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vendorUserId) || amountVnd <= 0) return;

        var wallet = await _db.VendorWallets.FirstOrDefaultAsync(x => x.VendorUserId == vendorUserId, ct);
        if (wallet == null)
        {
            wallet = new VendorWallet { VendorUserId = vendorUserId, BalanceVnd = 0, UpdatedAt = DateTime.UtcNow };
            _db.VendorWallets.Add(wallet);
        }

        wallet.BalanceVnd += amountVnd;
        wallet.UpdatedAt = DateTime.UtcNow;
        _db.VendorWalletLedgerEntries.Add(new VendorWalletLedgerEntry
        {
            VendorUserId = vendorUserId,
            DeltaVnd = amountVnd,
            BalanceAfterVnd = wallet.BalanceVnd,
            Kind = kind,
            Reference = reference,
            CreatedAt = DateTime.UtcNow
        });
        if (saveImmediately)
            await _db.SaveChangesAsync(ct);
    }

    /// <summary>Trừ ví nếu đủ số dư. saveImmediately=false khi gọi trong transaction lớn hơn.</summary>
    public async Task<bool> TryDebitAsync(string vendorUserId, long amountVnd, string kind, string? reference, bool saveImmediately = true, CancellationToken ct = default)
    {
        // Nếu vendorUserId trống hoặc amountVnd không hợp lệ, trả về false thay vì ném lỗi để caller dễ xử lý.
        if (string.IsNullOrWhiteSpace(vendorUserId) || amountVnd <= 0) return false;
        // Lấy wallet, nếu chưa có thì tạo mới với số dư 0. Cách này giúp tránh lỗi không mong muốn ở caller khi vendorUserId chưa có wallet.
        var wallet = await _db.VendorWallets.FirstOrDefaultAsync(x => x.VendorUserId == vendorUserId, ct);
        if (wallet == null)
        {
            wallet = new VendorWallet { VendorUserId = vendorUserId, BalanceVnd = 0, UpdatedAt = DateTime.UtcNow };
            _db.VendorWallets.Add(wallet);
        }

        if (wallet.BalanceVnd < amountVnd)
            return false;

        wallet.BalanceVnd -= amountVnd;
        wallet.UpdatedAt = DateTime.UtcNow;
        _db.VendorWalletLedgerEntries.Add(new VendorWalletLedgerEntry
        {
            VendorUserId = vendorUserId,
            DeltaVnd = -amountVnd,
            BalanceAfterVnd = wallet.BalanceVnd,
            Kind = kind,
            Reference = reference,
            CreatedAt = DateTime.UtcNow
        });
        if (saveImmediately)
            await _db.SaveChangesAsync(ct);
        return true;
    }
}
