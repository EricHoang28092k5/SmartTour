using SmartTour.Shared.Models;

namespace SmartTourCMS.Models;

public class PremiumPackageOption
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Months { get; set; }
    public long AmountVnd { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class PremiumPoiRow
{
    public int PoiId { get; set; }
    public string PoiName { get; set; } = string.Empty;
    public bool IsPremium { get; set; }
    public DateTime? PremiumExpiresAt { get; set; }
}

public class PremiumPageViewModel
{
    public List<PremiumPoiRow> Pois { get; set; } = new();
    public List<PremiumPackageOption> Packages { get; set; } = new();
    public int? SelectedPoiId { get; set; }
    public string SelectedPackageCode { get; set; } = "month";
}

public class PremiumCheckoutViewModel
{
    public int PoiId { get; set; }
    public string PoiName { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string PackageCode { get; set; } = string.Empty;
    public int Months { get; set; }
    public long AmountVnd { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string PayUrl { get; set; } = string.Empty;
    public string? Deeplink { get; set; }
    public string? QrCodeUrl { get; set; }
    public string CheckoutUrl => !string.IsNullOrWhiteSpace(Deeplink) ? Deeplink! : PayUrl;
    public string QrImageUrl { get; set; } = string.Empty;
}

public class PremiumReturnViewModel
{
    public string OrderId { get; set; } = string.Empty;
    public bool IsPaid { get; set; }
    public string Message { get; set; } = string.Empty;
    public Poi? Poi { get; set; }
}
