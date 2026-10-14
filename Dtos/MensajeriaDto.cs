using System.ComponentModel.DataAnnotations;

namespace BarberShop.Dtos
{
    public class ConfiguracionMensajeriaDto
    {
        public string CodigoPais { get; set; } = "58";
        public string AppDestino { get; set; } = "business";
        public string PlantillaRecordatorio { get; set; } = string.Empty;
        public string PlantillaDeudor { get; set; } = string.Empty;
        public string PlantillaConfirmacion { get; set; } = string.Empty;
    }

    public class UpdateConfiguracionMensajeriaDto
    {
        [Required]
        [StringLength(5)]
        public string CodigoPais { get; set; } = "58";

        [Required]
        [RegularExpression("business|personal|ask", ErrorMessage = "AppDestino debe ser business, personal o ask.")]
        public string AppDestino { get; set; } = "business";

        [Required]
        [StringLength(1000)]
        public string PlantillaRecordatorio { get; set; } = string.Empty;

        [Required]
        [StringLength(1000)]
        public string PlantillaDeudor { get; set; } = string.Empty;

        [Required]
        [StringLength(1000)]
        public string PlantillaConfirmacion { get; set; } = string.Empty;
    }
}
