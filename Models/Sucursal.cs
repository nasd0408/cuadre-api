using System.ComponentModel.DataAnnotations;

namespace BarberShop.Models;

public class Sucursal : BaseEntity
{
    [MaxLength(150)] public string Nombre { get; set; } = "";
    [MaxLength(500)] public string? DireccionPublica { get; set; }
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    public bool Publicada { get; set; }
}
