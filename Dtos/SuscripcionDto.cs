namespace BarberShop.Dtos
{
    // =============== EMPRESAS/SUSCRIPCIONES ===============
    public class EmpresaResumenDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Telefono { get; set; }
        public string? Direccion { get; set; }
        public DateTime FechaCreacion { get; set; }

        // Suscripción
        public int Estado { get; set; }
        public string EstadoNombre { get; set; } = string.Empty;
        public DateTime? FechaVencimiento { get; set; }
        public DateTime? FechaBloqueo { get; set; }
        public int DiasRestantes { get; set; }
        public int? Plan { get; set; }
        public string? PlanNombre { get; set; }

        // Estadísticas
        public int TotalEmpleados { get; set; }
        public int TotalClientes { get; set; }
        public int CitasUltimos30Dias { get; set; }
    }

    public class EmpresaDetalleAdminDto : EmpresaResumenDto
    {
        public string? AdminEmail { get; set; }
        public string? AdminNombre { get; set; }
        public List<HistorialSuscripcionDto> HistorialPagos { get; set; } = new();
        public EmpresaConfigDto? Config { get; set; }

    }

    public class HistorialSuscripcionDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string Accion { get; set; } = string.Empty;
        public string? Detalle { get; set; }
        public int? DiasAgregados { get; set; }
        public string? MetodoPago { get; set; }
        public decimal? Monto { get; set; }
    }

    public class ExtenderSuscripcionDto
    {
        public int EmpresaId { get; set; }
        public int Dias { get; set; } // 30 = mensual, 365 = anual
        public string MetodoPago { get; set; } = string.Empty; // PagoMovil, Binance, PayPal, Efectivo
        public decimal? Monto { get; set; }
        public string? Referencia { get; set; }
        public string? Notas { get; set; }
    }

    public class CambiarEstadoSuscripcionDto
    {
        public int EmpresaId { get; set; }
        public int NuevoEstado { get; set; } // 0=Activo, 1=Moroso, 2=Suspendido
        public string? Motivo { get; set; }
    }

    public class ResumenSuperAdminDto
    {
        public int TotalEmpresas { get; set; }
        public int EmpresasActivas { get; set; }
        public int EmpresasMorosas { get; set; }
        public int EmpresasSuspendidas { get; set; }
        public int EmpresasPorVencer7Dias { get; set; }
        public decimal IngresosMes { get; set; }
        public List<EmpresaResumenDto> EmpresasPorVencer { get; set; } = new();
        public List<EmpresaResumenDto> EmpresasMorosasLista { get; set; } = new();
    }

    // =============== AJUSTES DE USUARIO ===============
    public class AjustesUsuarioDto
    {
        public string NombreCompleto { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Telefono { get; set; }
    }

    public class CambiarPasswordDto
    {
        public string PasswordActual { get; set; } = string.Empty;
        public string PasswordNuevo { get; set; } = string.Empty;
    }

    public class AjustesEmpresaDto
    {
        public string Nombre { get; set; } = string.Empty;
        public string? Telefono { get; set; }
        public string? Direccion { get; set; }
        public string? LogoUrl { get; set; }
    }

    public class InfoSuscripcionDto
    {
        public int Estado { get; set; }
        public string EstadoNombre { get; set; } = string.Empty;
        public DateTime? FechaVencimiento { get; set; }
        public int DiasRestantes { get; set; }
        public bool RequierePago { get; set; }
        public string Mensaje { get; set; } = string.Empty;
    }
    // ── Response ──
    public class EmpresaConfigDto
    {
        public int Id { get; set; }
        public int EmpresaId { get; set; }
        public string? EmpresaNombre { get; set; }

        public int Plan { get; set; }
        public string PlanNombre { get; set; } = "";

        // Límites
        public int MaxEmpleados { get; set; }
        public int MaxServicios { get; set; }
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

        // Control
        public DateTime? FechaVencimiento { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime FechaActualizacion { get; set; }
    }

    // ── Asignar plan completo (sobreescribe con defaults) ──
    public class AsignarPlanDto
    {
        /// <summary>0 = Basico, 1 = Profesional, 2 = Premium, 3 = Gratis</summary>
        public int Plan { get; set; }
    }

    // ── Ajuste granular (solo envía lo que quieras cambiar) ──
    public class PatchConfigDto
    {
        // Límites (null = no cambiar, -1 = ilimitado)
        public int? MaxEmpleados { get; set; }
        public int? MaxServicios { get; set; }
        public int? MaxCitasOffline { get; set; }
        public int? RetencionHistorialMeses { get; set; }

        // Módulos (null = no cambiar)
        public bool? ModuloPagosEmpleados { get; set; }
        public bool? ModuloReportes { get; set; }
        public bool? ModuloRecordatorios { get; set; }
        public bool? ModuloCalendario { get; set; }

        // Monetización (null = no cambiar)
        public bool? MostrarAnuncios { get; set; }
        public bool? AccesoWeb { get; set; }

        // Vencimiento
        public DateTime? FechaVencimiento { get; set; }
        public bool? QuitarVencimiento { get; set; }
    }
    public class EliminarCuentaWebDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}