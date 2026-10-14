using BarberShop.Enum;
using System.ComponentModel.DataAnnotations;

namespace BarberShop.Dtos
{
    public class ServicioDto
    {
    }
    public class CreateServicioDto
    {
        [Required(ErrorMessage = "El nombre es obligatorio")]
        [StringLength(150)]
        public string Nombre { get; set; } = null!;

        [StringLength(500)]
        public string? Descripcion { get; set; }

        // Mantenemos las validaciones de negocio aquí
        [Range(0, double.MaxValue, ErrorMessage = "El precio debe ser mayor a 0")]
        public decimal Precio { get; set; }

        [Range(1, 600, ErrorMessage = "La duración debe ser entre 1 y 600 minutos")]
        public int DuracionMinutos { get; set; }
        public int? IntervaloDias { get; set; }

        [Required]
        [StringLength(50)]
        public string Categoria { get; set; } = null!;

        [StringLength(7)] // Ej: #FFFFFF
        public string? ColorHex { get; set; }

        public bool EnPromocion { get; set; } = false;

        [StringLength(500)]
        public string? ImagenUrl { get; set; }

        // NOTA: No incluimos 'Activo' porque al crear se asume true por defecto en la entidad.
        // No incluimos 'EmpresaId' porque lo pone el DbContext.
        // No incluimos 'Id' porque lo pone la DB.
    }
    public class UpdateServicioDto
    {
        [Required(ErrorMessage = "El nombre es obligatorio")]
        [StringLength(150)]
        public string Nombre { get; set; } = null!;

        [StringLength(500)]
        public string? Descripcion { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Precio { get; set; }

        [Range(1, 600)]
        public int DuracionMinutos { get; set; }
        public int? IntervaloDias { get; set; }

        [Required]
        [StringLength(50)]
        public string Categoria { get; set; } = null!;

        [StringLength(7)]
        public string? ColorHex { get; set; }

        public bool EnPromocion { get; set; }

        [StringLength(500)]
        public string? ImagenUrl { get; set; }

        // En el Update SÍ permitimos cambiar el estado Activo/Inactivo
        public bool Activo { get; set; } = true;
    }
    public class CambiarEstadoRecordatorioDto
    {
        public EstadoRecordatorio Estado { get; set; }
        public string? Nota { get; set; }
    }
}
