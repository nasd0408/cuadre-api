using System.ComponentModel.DataAnnotations;

namespace BarberShop.Models;

// El enlace privado y la solicitud se guardan como hashes; no se reutiliza un JWT.
public class ReservaPublica : BaseEntity
{
    [MaxLength(64)] public string TokenHash { get; set; } = "";
    [MaxLength(64)] public string SolicitudHash { get; set; } = "";
    public int CitaId { get; set; }
    public Cita Cita { get; set; } = null!;
    [MaxLength(150)] public string NombreCliente { get; set; } = "";
    [MaxLength(20)] public string Telefono { get; set; } = "";
    [MaxLength(254)] public string? Email { get; set; }
    [MaxLength(150)] public string NombreLocal { get; set; } = "";
    [MaxLength(500)] public string? DireccionLocal { get; set; }
}
