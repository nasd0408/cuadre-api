namespace BarberShop.Dtos
{
    public class CrearPagoNominaDto
    {
        public int EmpleadoId { get; set; }
        public DateTime FechaCorte { get; set; }
        public decimal MontoTotal { get; set; }
    }

    public class CrearAbonoPagoDto
    {
        public int PagoNominaId { get; set; }
        public decimal Monto { get; set; }
    }
    public class CrearAbonoNominaDto
    {
        public int PagoNominaId { get; set; }
        public decimal Monto { get; set; }
    }
    namespace BarberShop.Dtos
    {
        public class PagoNominaResponseDto
        {
            public int Id { get; set; }
            public int EmpleadoId { get; set; }
            public string EmpleadoNombre { get; set; } = string.Empty;
            public DateTime FechaCorte { get; set; }
            public decimal MontoTotal { get; set; }
            public decimal MontoPagado { get; set; }
            public decimal MontoPendiente => MontoTotal - MontoPagado;
            public int Estado { get; set; }
            public string EstadoNombre { get; set; } = string.Empty;
            public List<AbonoResponseDto> Abonos { get; set; } = new();
        }

        public class AbonoResponseDto
        {
            public int Id { get; set; }
            public decimal Monto { get; set; }
            public DateTime FechaAbono { get; set; }
        }


        public class ResumenNominaEmpleadoDto
        {
            public int EmpleadoId { get; set; }
            public string EmpleadoNombre { get; set; } = string.Empty;
            public decimal PorcentajeComision { get; set; }
            public int CitasPendientes { get; set; }
            public decimal ComisionPendiente { get; set; }
            public DateTime? UltimoPago { get; set; }
            public List<PagoNominaResponseDto> NominasPendientes { get; set; } = new();
        }

        public class ResumenGeneralNominasDto
        {
            public decimal TotalPendientePagar { get; set; }
            public int EmpleadosConPendientes { get; set; }
            public List<ResumenNominaEmpleadoDto> Empleados { get; set; } = new();
        }
    }

}
