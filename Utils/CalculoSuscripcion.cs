namespace BarberShop.Helpers
{
    /// <summary>
    /// Reglas de fechas de la suscripción.
    ///
    /// Vive aparte porque estaba implementada DOS veces con criterios distintos
    /// (una con DateTime.Today y otra con DateTime.UtcNow), y es el cálculo que
    /// decide cuántos días de servicio recibe alguien que paga. Un error aquí se
    /// traduce en cobrar y no entregar, o en regalar tiempo.
    /// </summary>
    public static class CalculoSuscripcion
    {
        /// <summary>
        /// Nueva fecha de vencimiento al añadir días.
        ///
        /// Si la suscripción sigue vigente se extiende desde su vencimiento, no
        /// desde hoy: renovar antes de tiempo no debe quitarle al usuario los días
        /// que le quedaban. Si ya venció, se cuenta desde ahora.
        /// </summary>
        public static DateTime CalcularNuevoVencimiento(
            DateTime? vencimientoActual,
            DateTime ahora,
            int dias)
        {
            var baseFecha = vencimientoActual.HasValue && vencimientoActual.Value > ahora
                ? vencimientoActual.Value
                : ahora;

            return baseFecha.AddDays(dias);
        }
    }
}
