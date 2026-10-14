using System.ComponentModel.DataAnnotations;

namespace BarberShop.Dtos;

public class ConfigurarReservasDto
{
    public bool Activas { get; set; }
    [Range(1, int.MaxValue)] public int? SucursalId { get; set; }
}

public record ProfesionalPublicoDto(int Id, string Nombre, string ZonaHoraria);
public record ServicioPublicoDto(int Id, string Nombre, int DuracionMinutos, decimal Precio);
public record ConfiguracionReservasDto(bool Activas, int? SucursalId, string? Token,
    IReadOnlyList<ProfesionalPublicoDto> Profesionales);
public record CatalogoReservasDto(string Nombre, string? Descripcion, string? Telefono,
    SucursalResponseDto Local, IReadOnlyList<ProfesionalPublicoDto> Profesionales,
    IReadOnlyList<ServicioPublicoDto> Servicios);

public class CrearReservaPublicaDto
{
    // 256 bits generados por el navegador. Se conserva al reintentar tras un timeout.
    [Required, RegularExpression("^[a-f0-9]{64}$")] public string SolicitudToken { get; set; } = "";
    [Range(1, int.MaxValue)] public int EmpleadoId { get; set; }
    [Range(1, int.MaxValue)] public int ServicioId { get; set; }
    public DateTimeOffset InicioUtc { get; set; }
    [Required, StringLength(150, MinimumLength = 2)] public string Nombre { get; set; } = "";
    [Required, StringLength(30), RegularExpression(@"^\+?[0-9 ()-]+$")] public string Telefono { get; set; } = "";
    [StringLength(254), EmailAddress] public string? Email { get; set; }
}

public record ConfirmacionReservaDto(string Negocio, string Profesional, string ZonaHoraria,
    string NombreCliente, DateTime InicioUtc, string Estado, bool PuedeCancelar,
    string Local, string? Direccion, string? TelefonoNegocio,
    IReadOnlyList<ServicioPublicoDto> Servicios);
