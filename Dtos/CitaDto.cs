namespace BarberShop.Dtos
{
    public class ServicioCantidadDto
    {
        public int ServicioId { get; set; }
        public int Cantidad { get; set; }
    }

    public class CreateCitaBaseDto
    {
        public int EmpleadoId { get; set; }
        public int ClienteId { get; set; }
        public DateTime Fecha { get; set; }
        public List<ServicioCantidadDto> Servicios { get; set; } = new();
    }

    public class CreateCitaPagadaDto : CreateCitaBaseDto
    {
        public string? ObservacionesPago { get; set; }
    }
    public class UpdateCitaDto
    {
        public int EmpleadoId { get; set; }
        public int ClienteId { get; set; }
        public DateTime Fecha { get; set; }
        public List<ServicioCantidadDto> Servicios { get; set; } = new();
    }
    public class CitaResponseDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public int Estado { get; set; }
        public string EstadoNombre { get; set; } = string.Empty;
        public bool PagadoAlEmpleado { get; set; }

        // Cliente
        public int ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public string? ClienteTelefono { get; set; }

        // Empleado
        public int EmpleadoId { get; set; }
        public string EmpleadoNombre { get; set; } = string.Empty;

        // Servicios
        public List<CitaServicioResponseDto> Servicios { get; set; } = new();

        // Calculados
        public decimal Total { get; set; }
        public string? Observaciones { get; set; }
    }

    public class CitaServicioResponseDto
    {
        public int ServicioId { get; set; }
        public string ServicioNombre { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public int Cantidad { get; set; }
        public int DuracionMinutos { get; set; }
        public decimal Subtotal => Precio * Cantidad;
    }

    public class CitasPagedResponseDto
    {
        public List<CitaResponseDto> Items { get; set; } = new();
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public bool HasNextPage => CurrentPage < TotalPages;
        public bool HasPreviousPage => CurrentPage > 1;
    }

    public class CitasFiltrosDto
    {
        public int? EmpleadoId { get; set; }
        public int? ClienteId { get; set; }
        public int? Estado { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? OrderBy { get; set; } = "fecha_desc"; // fecha_desc, fecha_asc, total_desc
    }
}
