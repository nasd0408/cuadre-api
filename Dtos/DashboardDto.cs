namespace BarberShop.Dtos
{
    public class DashboardCompleteDto
    {
        // Métricas del período seleccionado
        public MetricasPeriodoDto MetricasPeriodo { get; set; } = new();

        // Comparación con período anterior
        public ComparacionDto Comparacion { get; set; } = new();

        // Datos para gráficos
        public List<DatoGraficoDto> GraficoServicios { get; set; } = new();
        public List<DatoGraficoDto> GraficoIngresos { get; set; } = new();

        // Resumen de nóminas
        public ResumenNominasDto Nominas { get; set; } = new();

        // Cuentas por cobrar
        public decimal CuentasPorCobrar { get; set; }
        public int ClientesDeudores { get; set; }

        // Rendimiento por barbero
        public List<RendimientoBarberoDto> RendimientoBarberos { get; set; } = new();

        // Top servicios
        public List<TopServicioDto> TopServicios { get; set; } = new();

        // Info del período
        public string PeriodoNombre { get; set; } = string.Empty;
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
    }

    public class MetricasPeriodoDto
    {
        public decimal IngresosTotales { get; set; }
        public int TotalServicios { get; set; }
        public int CitasCerradas { get; set; }
        public int CitasPendientes { get; set; }
        public int CitasCanceladas { get; set; }
        public decimal TicketPromedio { get; set; }
    }

    public class ComparacionDto
    {
        public decimal IngresosPeriodoAnterior { get; set; }
        public int ServiciosPeriodoAnterior { get; set; }
        public decimal PorcentajeCambioIngresos { get; set; }
        public decimal PorcentajeCambioServicios { get; set; }
    }

    public class DatoGraficoDto
    {
        public string Etiqueta { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public decimal Valor { get; set; }
        public int Cantidad { get; set; }
    }

    public class ResumenNominasDto
    {
        public decimal TotalPendiente { get; set; }
        public int EmpleadosConPendiente { get; set; }
        public decimal ComisionesSinProcesar { get; set; }
        public int CitasSinProcesar { get; set; }
    }

    public class RendimientoBarberoDto
    {
        public int BarberoId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public int ServiciosCount { get; set; }
        public decimal TotalGenerado { get; set; }
        public decimal ComisionGenerada { get; set; }
        public decimal PorcentajeComision { get; set; }
    }

    public class TopServicioDto
    {
        public int ServicioId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public decimal Ingresos { get; set; }
    }
}