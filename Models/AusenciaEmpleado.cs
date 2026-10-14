using System.ComponentModel.DataAnnotations;

namespace BarberShop.Models;

public class AusenciaEmpleado : BaseEntity
{
    public int EmpleadoId { get; set; }
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }
    [MaxLength(100)] public string ZonaHoraria { get; set; } = "UTC";
    public DateTime InicioUtc { get; set; }
    public DateTime FinUtc { get; set; }
    [MaxLength(250)] public string? Motivo { get; set; }
}
