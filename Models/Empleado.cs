using BarberShop.Enum;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace BarberShop.Models
{
    public class Empleado : BaseEntity
    {
        [Required]
        [StringLength(150)]
        public string Nombre { get; set; } = null!;

        // 0.00 – 100.00 %
        [Range(0, 100)]
        [Precision(5, 2)]
        public decimal PorcentajeComision { get; set; }

        public PeriodicidadPago Periodicidad { get; set; }

        public DateTime? FechaUltimoPago { get; set; }

        public bool Activo { get; set; } = true;

        [MaxLength(100)]
        public string? ZonaHorariaAgenda { get; set; }
        public bool HorarioAgendaActivo { get; set; }

        [JsonIgnore]
        public ICollection<Cita> Citas { get; set; } = new List<Cita>();
    }
}
