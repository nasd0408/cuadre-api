namespace BarberShop.Dtos
{
    // =============== CLIENTES DEUDORES ===============
    public class ClienteDeudorDto
    {
        public int ClienteId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Telefono { get; set; }
        public decimal Deuda { get; set; }
        public DateTime? FechaPrimeraDeuda { get; set; }
        public DateTime? FechaUltimaVisita { get; set; }
        public int CitasPendientes { get; set; }
        public List<DeudaDetalleDto> Detalle { get; set; } = new();
    }

    public class DeudaDetalleDto
    {
        public int CitaId { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Monto { get; set; }
        public string Servicios { get; set; } = string.Empty;
    }

    public class ReporteDeudoresDto
    {
        public decimal TotalDeuda { get; set; }
        public int TotalDeudores { get; set; }
        public decimal DeudaPromedio { get; set; }
        public List<ClienteDeudorDto> Deudores { get; set; } = new();
    }

    // =============== HISTORIAL DE INGRESOS ===============
    public class FiltrosHistorialDto
    {
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string Agrupacion { get; set; } = "dia"; // dia, semana, mes
        public int? EmpleadoId { get; set; }
    }

    public class ResumenPeriodoDto
    {
        public string Etiqueta { get; set; } = "";
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public decimal Ingresos { get; set; }
        public int Servicios { get; set; }
        public int Citas { get; set; }
        public decimal TicketPromedio { get; set; }
        public decimal Nominas { get; set; }
        public decimal Neto { get; set; }
    }
    // ── Desglose de pagos por empleado en el período ──
    public class DesglosePagoEmpleadoDto
    {
        public int EmpleadoId { get; set; }
        public string EmpleadoNombre { get; set; } = "";
        public decimal TotalPagado { get; set; }
        public int CantidadAbonos { get; set; }
    }
    // ── Respuesta principal ──
    public class ReporteHistorialDto
    {
        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }
        public string Agrupacion { get; set; } = "dia";

        // Bruto
        public decimal IngresosTotales { get; set; }
        public int ServiciosTotales { get; set; }
        public int CitasTotales { get; set; }
        public decimal TicketPromedio { get; set; }
        public decimal IngresosPeriodoAnterior { get; set; }
        public decimal PorcentajeCambio { get; set; }

        // Nóminas (salidas)
        public decimal TotalNominas { get; set; }
        public decimal NominasPeriodoAnterior { get; set; }

        // Neto
        public decimal IngresoNeto { get; set; }
        public decimal PorcentajeCambioNeto { get; set; }

        // Clientela: la salud del negocio a mediano plazo. Los ingresos dicen cómo
        // fue el mes; esto dice si el mes que viene va a haber a quién atender.
        public ClientelaDto Clientela { get; set; } = new();

        // Detalle
        public List<ResumenPeriodoDto> Periodos { get; set; } = new();
        public List<RendimientoBarberoDto> TopEmpleados { get; set; } = new();
        public List<TopServicioDto> TopServicios { get; set; } = new();
        public List<DesglosePagoEmpleadoDto> DesglosePagos { get; set; } = new();
    }

    public class ClientelaDto
    {
        /// <summary>Clientes distintos atendidos en el período.</summary>
        public int Atendidos { get; set; }

        /// <summary>De los atendidos, los que vinieron por primera vez.</summary>
        public int Nuevos { get; set; }

        /// <summary>De los atendidos, los que ya habían venido antes.</summary>
        public int Recurrentes { get; set; }

        /// <summary>
        /// Porcentaje de atendidos que ya eran clientes. Es la métrica de fidelidad:
        /// un negocio que solo ve caras nuevas no está reteniendo a nadie.
        /// </summary>
        public decimal PorcentajeRecurrencia { get; set; }

        /// <summary>Clientes con historial que llevan más de <see cref="DiasParaRiesgo"/> sin volver.</summary>
        public int EnRiesgo { get; set; }

        public int DiasParaRiesgo { get; set; }
    }
}