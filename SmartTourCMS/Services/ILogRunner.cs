namespace SmartTourCMS.Services;

public interface ILogRunner
{
    string LogFilePath { get; }

    Task OverwriteAsync(string text, string? sessionId = null);

    /// <summary>Nối thêm vào cuối file (cùng writer queue), không xóa nội dung cũ.</summary>
    Task AppendAsync(string text, string? sessionId = null);

    Task<string> ReadAsync();

    Task<byte[]> ReadBytesAsync();
}
