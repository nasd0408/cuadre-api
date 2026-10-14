using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BarberShop.Middleware
{
    /// <summary>
    /// Convierte cualquier excepcion no capturada en una respuesta que el cliente
    /// pueda entender, y deja traza de la que si es nuestra culpa.
    ///
    /// Antes de esto no habia manejador global: la mayoria de acciones no tienen
    /// try/catch, asi que un DbUpdateException salia como 500 con CUERPO VACIO y
    /// sin ninguna linea de log. Desde la app eso era indistinguible de un fallo
    /// de red, y desde el servidor, invisible.
    ///
    /// Traduce ademas los fallos previsibles a su codigo correcto, para que la app
    /// pueda reaccionar en vez de ensenar "error inesperado":
    ///   - conflicto de concurrencia  -> 409
    ///   - violacion de clave unica   -> 409
    ///   - violacion de clave foranea -> 409
    ///   - argumento invalido         -> 400
    ///
    /// NUNCA se devuelve ex.Message al cliente en un 500: los mensajes de Npgsql
    /// llevan nombres de tabla, columna y restriccion. El traceId permite cruzar
    /// lo que ve el usuario con el log del servidor.
    /// </summary>
    public class ManejadorGlobalDeExcepciones
    {
        private readonly RequestDelegate _siguiente;
        private readonly ILogger<ManejadorGlobalDeExcepciones> _logger;

        /// <summary>
        /// Un middleware "por convencion" recibe en el constructor el
        /// RequestDelegate, que representa AL SIGUIENTE middleware de la cadena.
        /// Se construye una sola vez (es efectivamente singleton).
        /// </summary>
        public ManejadorGlobalDeExcepciones(
            RequestDelegate siguiente,
            ILogger<ManejadorGlobalDeExcepciones> logger)
        {
            _siguiente = siguiente;
            _logger = logger;
        }

        /// <summary>
        /// ASP.NET llama a InvokeAsync en cada peticion. Envolver la llamada al
        /// siguiente middleware en try/catch hace que cualquier excepcion lanzada
        /// "mas abajo" (otros middleware, controladores, EF) suba hasta aqui.
        /// </summary>
        public async Task InvokeAsync(HttpContext contexto)
        {
            try
            {
                await _siguiente(contexto);
            }
            catch (Exception ex)
            {
                await EscribirRespuestaAsync(contexto, ex);
            }
        }

        /// <summary>
        /// Registra la excepcion en el log y escribe una respuesta JSON con formato
        /// ProblemDetails (RFC 7807, el estandar de errores HTTP que usa ASP.NET):
        /// { status, title, detail, instance, traceId }.
        /// </summary>
        private async Task EscribirRespuestaAsync(HttpContext contexto, Exception ex)
        {
            // Si la respuesta ya empezo a enviarse no se puede cambiar el estado;
            // lo unico util que queda es registrarlo.
            if (contexto.Response.HasStarted)
            {
                _logger.LogError(ex,
                    "Excepcion con la respuesta ya iniciada en {Metodo} {Ruta}",
                    contexto.Request.Method, contexto.Request.Path);
                return;
            }

            var (estado, titulo, detalle) = Clasificar(ex);
            var traceId = contexto.TraceIdentifier;

            // Los 5xx son culpa nuestra y van como error; los 4xx son entrada del
            // cliente y no deben llenar el log de ruido.
            if (estado >= StatusCodes.Status500InternalServerError)
            {
                _logger.LogError(ex,
                    "Error no controlado en {Metodo} {Ruta}. TraceId {TraceId}",
                    contexto.Request.Method, contexto.Request.Path, traceId);
            }
            else
            {
                _logger.LogWarning(
                    "Peticion rechazada ({Estado}) en {Metodo} {Ruta}: {Detalle}. TraceId {TraceId}",
                    estado, contexto.Request.Method, contexto.Request.Path, detalle, traceId);
            }

            var problema = new ProblemDetails
            {
                Status = estado,
                Title = titulo,
                Detail = detalle,
                Instance = contexto.Request.Path,
            };
            problema.Extensions["traceId"] = traceId;

            contexto.Response.Clear();
            contexto.Response.StatusCode = estado;
            contexto.Response.ContentType = "application/problem+json";

            await contexto.Response.WriteAsync(JsonSerializer.Serialize(problema,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }

        /// <summary>
        /// Decide el codigo HTTP, titulo y mensaje segun el TIPO de excepcion,
        /// usando una "switch expression" con pattern matching de C#.
        /// El detalle de un 4xx SI se ensena al usuario, asi que va redactado para
        /// que pueda hacer algo con el. El de un 500 es siempre generico.
        /// </summary>
        private static (int estado, string titulo, string detalle) Clasificar(Exception ex) => ex switch
        {
            // Alguien mas guardo la misma fila entre nuestra lectura y nuestra
            // escritura. Tipicamente un doble toque en el movil.
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "Conflicto de concurrencia",
                "Alguien más modificó esta información mientras la editabas. " +
                "Vuelve a cargarla y repite la operación."),

            DbUpdateException dbEx when EsViolacion(dbEx, "23505") => (
                StatusCodes.Status409Conflict,
                "Registro duplicado",
                "Ese registro ya existe."),

            DbUpdateException dbEx when EsViolacion(dbEx, "23503") => (
                StatusCodes.Status409Conflict,
                "Registro en uso",
                "No se puede completar porque el registro está siendo usado por otros datos."),

            PostgresException { SqlState: "55P03" } => (
                StatusCodes.Status409Conflict,
                "Agenda ocupada",
                "Hay otra operación modificando la agenda. Vuelve a intentarlo."),

            ArgumentException or InvalidOperationException => (
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                ex.Message),

            // 499 es la convencion de nginx para "el cliente cerro antes de
            // recibir respuesta". No esta en StatusCodes de ASP.NET.
            OperationCanceledException => (
                499,
                "Solicitud cancelada",
                "La solicitud se canceló antes de completarse."),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Error interno",
                // Generico a proposito: ex.Message de Npgsql expone el esquema.
                "Ocurrió un error inesperado. Si continúa, contáctanos indicando el código de seguimiento."),
        };

        /// <summary>
        /// true si el error de EF viene de Postgres con el codigo SQLSTATE dado.
        /// 23505 = unique_violation, 23503 = foreign_key_violation.
        /// </summary>
        private static bool EsViolacion(DbUpdateException ex, string sqlState) =>
            ex.InnerException is PostgresException pg && pg.SqlState == sqlState;
    }
}
