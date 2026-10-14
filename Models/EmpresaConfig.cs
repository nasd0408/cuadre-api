// Models/EmpresaConfig.cs
using BarberShop.Enum;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BarberShop.Models
{
    public class EmpresaConfig
    {
        [Key]
        public int Id { get; set; }

        // ── Relación 1:1 con Empresa ──
        [Required]
        public int EmpresaId { get; set; }

        [ForeignKey("EmpresaId")]
        public Empresa Empresa { get; set; } = null!;

        // ── Plan ──
        [Required]
        public PlanEmpresa Plan { get; set; } = PlanEmpresa.Basico;

        // ── Límites ──
        /// <summary>Máximo de empleados. -1 = ilimitado</summary>
        public int MaxEmpleados { get; set; } = 1;

        /// <summary>Máximo de servicios activos. -1 = ilimitado</summary>
        public int MaxServicios { get; set; } = 5;

        /// <summary>Máximo de citas en cola offline. -1 = ilimitado</summary>
        public int MaxCitasOffline { get; set; } = 5;

        /// <summary>Meses de historial visible. -1 = ilimitado</summary>
        public int RetencionHistorialMeses { get; set; } = 3;

        // ── Módulos ──
        public bool ModuloPagosEmpleados { get; set; } = false;
        public bool ModuloReportes { get; set; } = false;
        public bool ModuloRecordatorios { get; set; } = false;
        public bool ModuloCalendario { get; set; } = false;

        // ── Monetización ──
        /// <summary>true = la app móvil muestra anuncios. Lo define el plan, pero
        /// se guarda por empresa para poder exceptuar casos puntuales (socios, demos).</summary>
        public bool MostrarAnuncios { get; set; } = false;

        /// <summary>false = la versión web queda restringida (beneficio de plan pago).</summary>
        public bool AccesoWeb { get; set; } = true;

        // ── Control ──
        /// <summary>null = sin vencimiento (producción). Con fecha = trial o plan temporal</summary>
        public DateTime? FechaVencimiento { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
        public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
    }
}