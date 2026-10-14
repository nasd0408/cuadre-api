using BarberShop.Enum;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace BarberShop.Models
{
    public class CuentaCliente : BaseEntity
    {
        // FK obligatorio a Cliente
        [Required]
        public int ClienteId { get; set; }

        [JsonIgnore] // Evita ciclos JSON
        public Cliente Cliente { get; set; } = null!;

        // FK opcional a Cita
        public int? CitaId { get; set; }

        [JsonIgnore]
        public Cita? Cita { get; set; }

        // Monto con precisión financiera
        [Precision(18, 4)]
        [Range(0, double.MaxValue)]
        public decimal Monto { get; set; }

        // Tipo de movimiento: Cargo o Abono
        [Required]
        public TipoMovimiento Tipo { get; set; }

        // Observaciones opcionales
        [StringLength(250)]
        public string? Observaciones { get; set; }
    }
}
