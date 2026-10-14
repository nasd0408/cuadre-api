namespace BarberShop.Helpers
{
    /// <summary>
    /// Configuración de anuncios servida a la app móvil dentro de PlanInfoDto.
    /// Se lee de la sección "Anuncios" de appsettings con IOptionsMonitor, así que
    /// editar el JSON en el droplet cambia el comportamiento sin reiniciar el
    /// servicio ni publicar una nueva versión en Play (kill switch).
    /// </summary>
    public class AnunciosOptions
    {
        public const string SectionName = "Anuncios";

        /// <summary>Interruptor maestro. false = ninguna empresa ve anuncios.</summary>
        public bool Habilitado { get; set; } = true;

        public bool BannerHabilitado { get; set; } = true;
        public bool IntersticialHabilitado { get; set; } = true;

        /// <summary>Arranca apagado a propósito: es el formato con más riesgo de desinstalación.</summary>
        public bool AppOpenHabilitado { get; set; } = false;

        /// <summary>Minutos mínimos entre dos intersticiales.</summary>
        public int IntersticialMinutosEntre { get; set; } = 3;

        public int IntersticialMaxPorDia { get; set; } = 6;
        public int AppOpenMaxPorDia { get; set; } = 3;

        /// <summary>Acciones de negocio que el usuario debe completar antes de ver un intersticial.</summary>
        public int AccionesAntesDeIntersticial { get; set; } = 3;

        /// <summary>
        /// true = registrar un servicio desde las acciones rápidas (cobrado o a deber)
        /// muestra anuncio SIEMPRE, saltándose el contador de acciones y la cadencia
        /// de minutos. Es el flujo principal del negocio, así que es la palanca de
        /// ingresos más fuerte y también la más arriesgada para la retención.
        ///
        /// El tope diario sigue aplicando: es el único freno que queda cuando esto
        /// está activo.
        /// </summary>
        public bool IntersticialSiempreEnAccionRapida { get; set; } = true;
    }
}
