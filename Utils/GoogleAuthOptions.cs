namespace BarberShop.Helpers
{
    /// <summary>
    /// Configuración de inicio de sesión con Google (sección "Authentication:Google").
    /// </summary>
    public class GoogleAuthOptions
    {
        public const string SectionName = "Authentication:Google";

        /// <summary>
        /// Client IDs aceptados como audiencia del ID token.
        ///
        /// Ojo: con @react-native-google-signin el token que llega desde Android trae
        /// como `aud` el client ID **Web**, no el de Android. Por eso esto es una lista:
        /// se pone el Web (obligatorio) y opcionalmente el de Android/iOS.
        /// </summary>
        public string[] ClientIds { get; set; } = System.Array.Empty<string>();

        /// <summary>true si hay al menos un Client ID no vacio configurado.</summary>
        public bool EstaConfigurado => ClientIds.Length > 0
            && ClientIds.Any(id => !string.IsNullOrWhiteSpace(id));
    }
}
