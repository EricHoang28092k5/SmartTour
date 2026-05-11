namespace SmartTourAPI.Services
{
    public interface IVoiceService
    {
        Task<string> GenerateAndUploadAudio(string text, string langCode);
    }
}