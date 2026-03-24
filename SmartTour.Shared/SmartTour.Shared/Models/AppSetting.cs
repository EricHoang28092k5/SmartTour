using SQLite;

namespace SmartTour.Shared.Models
{
    public class AppSetting
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string SettingKey { get; set; } = string.Empty;
        public string SettingValue { get; set; } = string.Empty;
    }
}
