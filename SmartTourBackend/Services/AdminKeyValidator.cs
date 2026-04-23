namespace SmartTourBackend.Services;

public interface IAdminKeyValidator
{
    bool IsValid(HttpContext context);
    bool IsEnabled { get; }
}

public class AdminKeyValidator : IAdminKeyValidator
{
    private readonly IConfiguration _configuration;
    public AdminKeyValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(GetConfiguredKey());

    public bool IsValid(HttpContext context)
    {
        var configured = GetConfiguredKey();
        if (string.IsNullOrWhiteSpace(configured)) return true;
        if (!context.Request.Headers.TryGetValue("X-Admin-Key", out var header)) return false;
        return string.Equals(header.ToString(), configured, StringComparison.Ordinal);
    }

    private string? GetConfiguredKey()
        => _configuration["Admin:ApiKey"] ?? Environment.GetEnvironmentVariable("ADMIN_API_KEY");
}
