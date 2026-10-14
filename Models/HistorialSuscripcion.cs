using System.ComponentModel.DataAnnotations;

namespace BarberShop.Models
{
    public class HistorialSuscripcion
    {
        public int Id { get; set; }

        public int EmpresaId { get; set; }
        public Empresa? Empresa { get; set; }

        public DateTime Fecha { get; set; } = DateTime.UtcNow;

        [MaxLength(50)]
        public string Accion { get; set; } = string.Empty; // Activacion, Extension, Suspension, Reactivacion

        [MaxLength(500)]
        public string? Detalle { get; set; }

        public int? DiasAgregados { get; set; }

        [MaxLength(50)]
        public string? MetodoPago { get; set; }

        public decimal? Monto { get; set; }

        [MaxLength(100)]
        public string? Referencia { get; set; }
    }
}