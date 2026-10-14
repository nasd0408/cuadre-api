using System.Text.Json.Serialization;

namespace BarberShop.Enum
{
    //Para los empleados
    public enum PeriodicidadPago { Semanal, Quincenal, Mensual }
    //Para los pagos de nómina
    public enum EstadoPago { Pendiente, Parcial, Pagado }
    //Para las suscripciones de las empresas
    public enum EstadoSuscripcion { Activo, Moroso, Suspendido }
    //para los movimientos en la cuenta del cliente
    public enum TipoMovimiento { Cargo, Abono }
    public enum EstadoCita
    {
        Pendiente,       // La cita es futura o recién creada
        PagoPendiente,   // Cliente asistió, pero falta pagar
        Pagada,          // Cliente pagó, aún no cerrada
        Cerrada,          // Todo gestionado, comisiones y deuda resueltas
        Cancelada         //Cancelada por motivos agenos a usuario de app
    }
    //para el estado de los recordatorios
    public enum EstadoRecordatorio
    {
        Pendiente,       // Aparece en la lista, sin contactar
        Contactado,      // Lo contacté y respondió
        NoContestó,      // Lo contacté pero no respondió
        Descartado       // Ya vino, se generó nueva cita (se actualiza automático)
    }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PlanEmpresa
    {
        // Los valores se persisten como integer (EmpresaConfig.Plan y
        // PagoSuscripcion.PlanSolicitado). NO reordenar ni insertar en el medio:
        // los planes nuevos van al final con su valor explícito.
        Basico = 0,
        Profesional = 1,
        Premium = 2,
        Gratis = 3
    }

}
