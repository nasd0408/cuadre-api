using BarberShop.Enum;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace BarberShop.Models
{
    public class PagoNomina : BaseEntity
    {
        [Required]
        public int EmpleadoId { get; set; }

        [JsonIgnore]
        public Empleado Empleado { get; set; } = null!;

        public DateTime FechaCorte { get; set; }

        [Precision(18, 4)]
        public decimal MontoTotal { get; set; }

        [Precision(18, 4)]
        public decimal MontoPagado { get; set; }

        public EstadoPago Estado { get; set; }

        public List<AbonoPago> Abonos { get; set; } = new();

        [NotMapped]
        public decimal MontoPendiente => MontoTotal - MontoPagado;
    }
}
