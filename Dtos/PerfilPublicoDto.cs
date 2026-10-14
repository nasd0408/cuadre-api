using System.ComponentModel.DataAnnotations;

namespace BarberShop.Dtos;

public class PerfilPublicoDto
{
    [Required, StringLength(150)] public string Nombre { get; set; } = "";
    [StringLength(2000)] public string? Descripcion { get; set; }
    [Required, StringLength(80)] public string Categoria { get; set; } = "";
    [StringLength(30), Phone] public string? TelefonoPublico { get; set; }
    // Usuarios de las plataformas, no URLs arbitrarias.
    [StringLength(100), RegularExpression(@"^@?[a-zA-Z0-9._-]+$")] public string? Instagram { get; set; }
    [StringLength(100), RegularExpression(@"^@?[a-zA-Z0-9._-]+$")] public string? Facebook { get; set; }
    [StringLength(100), RegularExpression(@"^@?[a-zA-Z0-9._-]+$")] public string? TikTok { get; set; }
    public bool Publicado { get; set; }
}

public class SucursalDto : IValidatableObject
{
    [Required, StringLength(150)] public string Nombre { get; set; } = "";
    [StringLength(500)] public string? DireccionPublica { get; set; }
    [Range(-90d, 90d)] public double? Latitud { get; set; }
    [Range(-180d, 180d)] public double? Longitud { get; set; }
    public bool Publicada { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Latitud.HasValue != Longitud.HasValue ||
            (Latitud.HasValue && !double.IsFinite(Latitud.Value)) ||
            (Longitud.HasValue && !double.IsFinite(Longitud.Value)))
            yield return new ValidationResult("Indica ambas coordenadas válidas o ninguna.",
                [nameof(Latitud), nameof(Longitud)]);
    }
}

public record SucursalResponseDto(int Id, string Nombre, string? DireccionPublica,
    double? Latitud, double? Longitud, bool Publicada);

public record EmpresaPublicaDto(int EmpresaId, PerfilPublicoDto Perfil,
    IReadOnlyList<SucursalResponseDto> Sucursales, string? TokenReservas = null);
