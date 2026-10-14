using System.ComponentModel.DataAnnotations;

namespace BarberShop.Dtos
{
    public class ClienteCreateDto
    {
        [Required(ErrorMessage = "El nombre es obligatorio")]
        [MaxLength(100)]
        public string Nombre { get; set; } = null!;

        [MaxLength(20)]
        public string? Telefono { get; set; } // string? permite null

        public DateTime? FechaUltimoCorte { get; set; }
    }

    public class ClienteUpdateDto
    {
        public string Nombre { get; set; } = null!;
        public string? Telefono { get; set; } = null!;
        public DateTime? FechaUltimoCorte { get; set; }
    }
    public class ClienteResponseDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = null!;
        public string? Telefono { get; set; }
        public DateTime? FechaUltimoCorte { get; set; }
        public bool Activo { get; set; }

        // Puedes agregar campos calculados si quieres
        // public int TotalCitas { get; set; } 
    }
    public class ClienteRecordatorioDto
    {
        public int ClienteId { get; set; }
        public string Nombre { get; set; } = null!;
        public string? Telefono { get; set; }

        public DateTime UltimaVisita { get; set; }
        public int DiasDesdeUltimaVisita { get; set; }

        public string Servicio { get; set; } = null!;
        public int IntervaloDias { get; set; }

        public int DiasVencido { get; set; }
    }

}
