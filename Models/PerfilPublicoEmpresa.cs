using System.ComponentModel.DataAnnotations;

namespace BarberShop.Models;

// Independiente de Empresa: publicar nunca expone datos administrativos por accidente.
public class PerfilPublicoEmpresa : BaseEntity
{
    [MaxLength(150)] public string Nombre { get; set; } = "";
    [MaxLength(2000)] public string? Descripcion { get; set; }
    [MaxLength(80)] public string Categoria { get; set; } = "";
    [MaxLength(30)] public string? TelefonoPublico { get; set; }
    [MaxLength(100)] public string? Instagram { get; set; }
    [MaxLength(100)] public string? Facebook { get; set; }
    [MaxLength(100)] public string? TikTok { get; set; }
    public bool Publicado { get; set; }
    public bool ReservasActivas { get; set; }
    [MaxLength(64)] public string? TokenReservas { get; set; }
    public int? SucursalReservasId { get; set; }
}
