using System.ComponentModel.DataAnnotations;

namespace BarberShop.Dtos
{
    public class LandingPagoDto
    {
        [Required]
        [StringLength(150)]
        public string Nombre { get; set; } = null!;

        [Required]
        [EmailAddress]
        [StringLength(200)]
        public string Email { get; set; } = null!;

        [Required]
        [StringLength(50)]
        public string Plan { get; set; } = null!;           // "basico" | "profesional" | "empresarial"

        [Required]
        [StringLength(50)]
        public string MetodoPago { get; set; } = null!;     // "binance" | "pago_movil" | "transferencia" | "otro"

        [Required]
        [StringLength(100)]
        public string ReferenciaPago { get; set; } = null!;
    }

}
