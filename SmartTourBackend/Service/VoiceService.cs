using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.CognitiveServices.Speech;

namespace SmartTourBackend.Services
{
    public class VoiceService : IVoiceService
    {
        private readonly Cloudinary _cloudinary;
        private readonly IConfiguration _configuration;

        public VoiceService(Cloudinary cloudinary, IConfiguration configuration)
        {
            _cloudinary = cloudinary;
            _configuration = configuration;
        }

        public async Task<string> GenerateAndUploadAudio(string text, string langCode)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            try
            {
                // Ưu tiên đọc từ ENV cho deploy, fallback sang appsettings.
                var speechKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY")
                    ?? _configuration["AzureSpeech:Key"];
                var speechRegion = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION")
                    ?? _configuration["AzureSpeech:Region"];

                if (string.IsNullOrWhiteSpace(speechKey) || string.IsNullOrWhiteSpace(speechRegion))
                {
                    throw new Exception("Missing AZURE_SPEECH_KEY/AZURE_SPEECH_REGION.");
                }

                var speechConfig = SpeechConfig.FromSubscription(speechKey, speechRegion);
                speechConfig.SpeechSynthesisVoiceName = ResolveAzureVoice(langCode);
                speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3);

                using var synthesizer = new SpeechSynthesizer(speechConfig, audioConfig: null);
                var result = await synthesizer.SpeakTextAsync(text);
                if (result.Reason != ResultReason.SynthesizingAudioCompleted)
                {
                    var details = SpeechSynthesisCancellationDetails.FromResult(result);
                    throw new Exception($"Azure TTS failed: {details?.ErrorDetails ?? result.Reason.ToString()}");
                }

                await using var stream = new MemoryStream(result.AudioData);
                var uploadParams = new RawUploadParams()
                {
                    File = new FileDescription($"audio_{Guid.NewGuid():N}.mp3", stream),
                    Folder = "smarttour_audios",
                    AccessMode = "public"
                };

                var uploadResult = await _cloudinary.UploadAsync(uploadParams);

                if (uploadResult.Error != null)
                {
                    throw new Exception($"Cloudinary Error: {uploadResult.Error.Message}");
                }

                return uploadResult.SecureUrl.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoiceService Error]: {ex.Message}");
                return string.Empty;
            }
        }

        private static string ResolveAzureVoice(string? langCode)
        {
            var normalized = string.IsNullOrWhiteSpace(langCode)
                ? "vi-vn"
                : langCode.Trim().ToLowerInvariant();

            return normalized switch
            {
                "vi" or "vi-vn" => "vi-VN-HoaiMyNeural", // Vietnamese (female)
                "en" or "en-us" => "en-US-JennyNeural",
                "fr" or "fr-fr" => "fr-FR-DeniseNeural",
                "ja" or "ja-jp" => "ja-JP-NanamiNeural",
                "ko" or "ko-kr" => "ko-KR-SunHiNeural",
                "zh" or "zh-cn" or "cmn-cn" => "zh-CN-XiaoxiaoNeural",
                _ => "en-US-JennyNeural"
            };
        }
    }
}