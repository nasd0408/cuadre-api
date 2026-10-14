// Services/PlanValidationService.cs
using BarberShop.Data;
using BarberShop.Enum;
using BarberShop.Helpers;
using BarberShop.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BarberShop.Services
{
    /// <summary>
    /// Hace cumplir los limites del plan contratado (cuantos empleados/servicios,
    /// que modulos, cuanto historial). Los controladores lo llaman ANTES de crear
    /// algo. Los metodos devuelven tuplas (Permitido, Error): un patron para
    /// informar "no se puede y por que" sin lanzar excepciones.
    /// Registrado sin interfaz (AddScoped&lt;PlanValidationService&gt;()).
    /// </summary>
    public class PlanValidationService
    {
        private readonly AppDbContext _context;
        private readonly IOptionsMonitor<AnunciosOptions> _anuncios;

        /// <summary>Recibe la BD y la configuracion de anuncios (recargable en caliente).</summary>
        public PlanValidationService(AppDbContext context, IOptionsMonitor<AnunciosOptions> anuncios)
        {
            _context = context;
            _anuncios = anuncios;
        }

        /// <summary>
        /// Obtiene la configuración de la empresa. Retorna null si no existe.
        /// </summary>
        public async Task<EmpresaConfig?> GetConfigAsync(int empresaId)
        {
            return await _context.EmpresaConfigs
                .FirstOrDefaultAsync(c => c.EmpresaId == empresaId);
        }

        // ═══════════════════════════════════════════════
        // LÍMITES NUMÉRICOS
        // ═══════════════════════════════════════════════

        /// <summary>
        /// Valida si se puede crear un nuevo empleado.
        /// </summary>
        public async Task<(bool Permitido, string? Error)> PuedeCrearEmpleadoAsync(int empresaId)
        {
            var config = await GetConfigAsync(empresaId);
            if (config == null) return (false, "Empresa sin configuración de plan.");

            if (config.MaxEmpleados == -1) return (true, null); // Ilimitado

            var actuales = await _context.Empleados
                .CountAsync(e => e.EmpresaId == empresaId && e.Activo);

            if (actuales >= config.MaxEmpleados)
                return (false, $"Tu plan {GetPlanNombre(config.Plan)} permite máximo {config.MaxEmpleados} empleado(s). Actualiza tu plan para agregar más.");

            return (true, null);
        }

        /// <summary>
        /// Valida si se puede crear un nuevo servicio.
        /// </summary>
        public async Task<(bool Permitido, string? Error)> PuedeCrearServicioAsync(int empresaId)
        {
            var config = await GetConfigAsync(empresaId);
            if (config == null) return (false, "Empresa sin configuración de plan.");

            if (config.MaxServicios == -1) return (true, null);

            var actuales = await _context.Servicios
                .CountAsync(s => s.EmpresaId == empresaId && s.Activo);

            if (actuales >= config.MaxServicios)
                return (false, $"Tu plan {GetPlanNombre(config.Plan)} permite máximo {config.MaxServicios} servicio(s). Actualiza tu plan para agregar más.");

            return (true, null);
        }

        /// <summary>
        /// Valida si la cantidad de citas offline no excede el límite.
        /// </summary>
        public async Task<(bool Permitido, int MaxPermitido, string? Error)> ValidarCitasOfflineAsync(int empresaId, int cantidadEnviada)
        {
            var config = await GetConfigAsync(empresaId);
            if (config == null) return (false, 0, "Empresa sin configuración de plan.");

            if (config.MaxCitasOffline == -1) return (true, -1, null);

            if (cantidadEnviada > config.MaxCitasOffline)
                return (false, config.MaxCitasOffline, $"Tu plan permite máximo {config.MaxCitasOffline} citas offline. Enviaste {cantidadEnviada}.");

            return (true, config.MaxCitasOffline, null);
        }

        // ═══════════════════════════════════════════════
        // MÓDULOS
        // ═══════════════════════════════════════════════

        /// <summary>
        /// Comprueba si el plan incluye un modulo ("pagos", "reportes",
        /// "recordatorios", "calendario"). Un nombre desconocido cuenta como no
        /// habilitado, asi un error de tipeo nunca abre un modulo de pago.
        /// </summary>
        public async Task<(bool Permitido, string? Error)> TieneModuloAsync(int empresaId, string modulo)
        {
            var config = await GetConfigAsync(empresaId);
            if (config == null) return (false, "Empresa sin configuración de plan.");

            var (habilitado, nombreModulo) = modulo.ToLower() switch
            {
                "pagos" => (config.ModuloPagosEmpleados, "Pagos a empleados"),
                "reportes" => (config.ModuloReportes, "Reportes"),
                "recordatorios" => (config.ModuloRecordatorios, "Recordatorios"),
                "calendario" => (config.ModuloCalendario, "Vista calendario"),
                _ => (false, modulo)
            };

            if (!habilitado)
                return (false, $"El módulo de {nombreModulo} no está disponible en tu plan {GetPlanNombre(config.Plan)}. Actualiza tu plan para acceder.");

            return (true, null);
        }

        // ═══════════════════════════════════════════════
        // RETENCIÓN DE HISTORIAL
        // ═══════════════════════════════════════════════

        /// <summary>
        /// Retorna la fecha mínima desde la cual se puede consultar historial.
        /// null = sin límite.
        /// </summary>
        public async Task<DateTime?> GetFechaMinHistorialAsync(int empresaId)
        {
            var config = await GetConfigAsync(empresaId);
            if (config == null || config.RetencionHistorialMeses == -1)
                return null;

            return DateTime.Today.AddMonths(-config.RetencionHistorialMeses);
        }

        // ═══════════════════════════════════════════════
        // ENDPOINT: Configuración para el frontend
        // ═══════════════════════════════════════════════

        /// <summary>
        /// Retorna los límites y módulos para que el frontend sepa qué mostrar/ocultar.
        /// </summary>
        public async Task<PlanInfoDto?> GetPlanInfoAsync(int empresaId)
        {
            var config = await GetConfigAsync(empresaId);
            if (config == null) return null;

            var empleadosActuales = await _context.Empleados
                .CountAsync(e => e.EmpresaId == empresaId && e.Activo);
            var serviciosActuales = await _context.Servicios
                .CountAsync(s => s.EmpresaId == empresaId && s.Activo);

            return new PlanInfoDto
            {
                Plan = (int)config.Plan,
                PlanNombre = GetPlanNombre(config.Plan),

                MaxEmpleados = config.MaxEmpleados,
                EmpleadosActuales = empleadosActuales,
                MaxServicios = config.MaxServicios,
                ServiciosActuales = serviciosActuales,
                MaxCitasOffline = config.MaxCitasOffline,
                RetencionHistorialMeses = config.RetencionHistorialMeses,

                ModuloPagosEmpleados = config.ModuloPagosEmpleados,
                ModuloReportes = config.ModuloReportes,
                ModuloRecordatorios = config.ModuloRecordatorios,
                ModuloCalendario = config.ModuloCalendario,

                MostrarAnuncios = config.MostrarAnuncios,
                AccesoWeb = config.AccesoWeb,
                Anuncios = _anuncios.CurrentValue,

                FechaVencimiento = config.FechaVencimiento
            };
        }

        // ═══════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════

        /// <summary>Nombre legible del plan para mostrar en mensajes.</summary>
        public static string GetPlanNombre(PlanEmpresa plan) => plan switch
        {
            PlanEmpresa.Gratis => "Gratis",
            PlanEmpresa.Basico => "Básico",
            PlanEmpresa.Profesional => "Profesional",
            PlanEmpresa.Premium => "Premium",
            _ => "Desconocido"
        };
    }

    // DTO que se envía al frontend
    public class PlanInfoDto
    {
        public int Plan { get; set; }
        public string PlanNombre { get; set; } = "";

        // Límites con uso actual
        public int MaxEmpleados { get; set; }
        public int EmpleadosActuales { get; set; }
        public int MaxServicios { get; set; }
        public int ServiciosActuales { get; set; }
        public int MaxCitasOffline { get; set; }
        public int RetencionHistorialMeses { get; set; }

        // Módulos
        public bool ModuloPagosEmpleados { get; set; }
        public bool ModuloReportes { get; set; }
        public bool ModuloRecordatorios { get; set; }
        public bool ModuloCalendario { get; set; }

        // Monetización
        public bool MostrarAnuncios { get; set; }
        public bool AccesoWeb { get; set; }

        /// <summary>Topes y switches de anuncios (appsettings, recargable en caliente).</summary>
        public AnunciosOptions Anuncios { get; set; } = new();

        public DateTime? FechaVencimiento { get; set; }
    }
}