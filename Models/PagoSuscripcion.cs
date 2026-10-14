using BarberShop.Enum;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace BarberShop.Models
{
    public class PagoSuscripcion : BaseEntity
    {
        [Required]
        public PlanEmpresa PlanSolicitado { get; set; }

        [Required]
        [StringLength(100)]
        public string NumeroReferencia { get; set; } = null!;

        [Required]
        public DateTime FechaPago { get; set; }

        [Required]
        [StringLength(50)]
        public string MetodoPago { get; set; } = null!;

        [Precision(18, 4)]
        public decimal Monto { get; set; }

        [StringLength(500)]
        public string? Notas { get; set; }

        // Estado para tu gestión: "Pendiente", "Aprobado", "Rechazado"
        [Required]
        public string Estado { get; set; } = "Pendiente";
    }
}