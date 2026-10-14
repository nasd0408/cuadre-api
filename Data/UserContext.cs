using BarberShop.Interfaces;
using System.Security.Claims;

namespace BarberShop.Data
{
    /// <summary>
    /// Implementacion de IUserContext: responde "quien esta haciendo esta
    /// peticion" leyendo los claims del JWT ya validado. Un claim es un par
    /// clave/valor firmado dentro del token (ver AuthController.GenerarJwt).
    ///
    /// IHttpContextAccessor permite leer la peticion HTTP actual desde una clase
    /// que no es un controlador. Fuera de una peticion (p. ej. en un
    /// BackgroundService) HttpContext es null y todo devuelve "sin usuario".
    /// </summary>
    public class UserContext : IUserContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>Recibe el accesor al HttpContext por inyeccion de dependencias.</summary>
        public UserContext(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        // Extraemos el claim "EmpresaId" que meteremos en el JWT más adelante
        public int? EmpresaId =>
            int.TryParse(_httpContextAccessor.HttpContext?.User?.FindFirst("EmpresaId")?.Value, out var id)
            ? id : null;

        // Sin sesion no hay usuario. Se devuelve cadena vacia y no null porque el
        // contrato de IUserContext declara string, y quien lo consume comprueba
        // IsNullOrEmpty: devolver null era una NullReferenceException esperando.
        public string UsuarioId =>
            _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? string.Empty;

        // true si el JWT trae el rol SuperAdmin (el dueno de la plataforma, que
        // usa BarberShopAdmin). Con esto el filtro de tenant deja ver todo.
        public bool IsSuperAdmin =>
            _httpContextAccessor.HttpContext?.User?.IsInRole("SuperAdmin") ?? false;
    
    }
}
