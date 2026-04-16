using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartTour.Shared.Models
{
    [Table("FoodTranslation")]
    public class FoodTranslation
    {
        [Key]
        public int Id { get; set; }

        public int FoodId { get; set; }
        public int LanguageId { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public Food? Food { get; set; }
        public Language? Language { get; set; }
    }
}
