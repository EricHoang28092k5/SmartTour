using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmartTour.Shared.Models
{
    public class Language
    {
        public int Id { get; set; }
        public string Code { get; set; } = "vi"; // vi, en, ja...
        public string Name { get; set; } = string.Empty;
    }
}
