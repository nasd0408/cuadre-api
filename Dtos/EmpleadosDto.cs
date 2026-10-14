using BarberShop.Enum;
using System.ComponentModel.DataAnnotations;

namespace BarberShop.Dtos
{
    public class EmpleadosDto
    {
    }
    public class CreateEmpleadoDto
    {
        [Required]
        [StringLength(150)]
        public string Nombre { get; set; } = null!;

        [Range(0, 100)]
        public decimal PorcentajeComision { get; set; }

        public PeriodicidadPago Periodicidad { get; set; }
    }
    public class UpdateEmpleadoDto
    {
        [Required(ErrorMessage = "El nombre es obligatorio")]
        [StringLength(150)]
        public string Nombre { get; set; } = null!;

        [Range(0, 100, ErrorMessage = "La comisión debe estar entre 0 y 100")]
        public decimal PorcentajeComision { get; set; }

        // Aquí podrías decidir si permites cambiar la periodicidad o no. 
        // Si no quieres que la cambien, quita esta propiedad.
        [Required]
        public PeriodicidadPago Periodicidad { get; set; }

        // Campo nuevo exclusivo para Updates
        public bool Activo { get; set; }
    }
    public class EmpleadoDetalleDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public decimal PorcentajeComision { get; set; }
        public PeriodicidadPago Periodicidad { get; set; }
        public string PeriodicidadNombre { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public DateTime? FechaUltimoPago { get; set; }

        // Rendimiento
        public RendimientoDto RendimientoHoy { get; set; } = new();
        public RendimientoDto RendimientoSemana { get; set; } = new();
        public RendimientoDto RendimientoMes { get; set; } = new();

        // Comisiones
        public decimal ComisionPendiente { get; set; }
        public int CitasPendientesPago { get; set; }

        // Histórico
        public List<ResumenDiarioDto> UltimosDias { get; set; } = new();
    }

    public class RendimientoDto
    {
        public int TotalCitas { get; set; }
        public int CitasCerradas { get; set; }
        public decimal TotalGenerado { get; set; }
        public decimal ComisionGenerada { get; set; }
    }

    public class ResumenDiarioDto
    {
        public DateTime Fecha { get; set; }
        public int Citas { get; set; }
        public decimal Total { get; set; }
        public decimal Comision { get; set; }
    }
    public class CitaHistorialDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Total { get; set; }
        public decimal Comision { get; set; }
        public bool PagadoAlEmpleado { get; set; }
        public int NumeroServicios { get; set; }
    }
}
