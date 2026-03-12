using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartTour.Shared.Models
{
    public class QrCode
    {
        public int Id { get; set; }
        public int PoiId { get; set; }
        public string QrToken { get; set; } = string.Empty;
        public string? LocationName { get; set; }
    }
}
