using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace BarberShop.Models
{
    public class AbonoPago : BaseEntity
    {
        [Required]
        public int PagoNominaId { get; set; }
        // La rellena EF solo si se hace Include; el `null!` se lo dice al compilador.
        public PagoNomina PagoNomina { get; set; } = null!;
        [Precision(18, 4)]
        [Range(0, double.MaxValue)]
        public decimal Monto { get; set; }

        public DateTime FechaAbono { get; set; } = DateTime.UtcNow;
    }
}
