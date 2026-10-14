using BarberShop.Data;
using BarberShop.Enum;
using BarberShop.Helpers;
using BarberShop.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Controllers
{
    /// <summary>
    /// Endpoint que la app consulta para saber en que estado esta la suscripcion
    /// (activa, morosa, suspendida, plan Gratis) y que mensaje mostrar.
    /// [Route("api/[controller]")] usa el nombre de la clase sin "Controller":
    /// queda api/subscription.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SubscriptionController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IUserContext _userContext;

        /// <summary>Recibe BD y contexto del usuario por DI.</summary>
        public SubscriptionController(AppDbContext context, IUserContext userContext)
        {
            _context = context;
            _userContext = userContext;
        }

        /// <summary>
        /// Get current subscription status for the user's empresa
        /// </summary>
        /// <summary>
        /// Calcula el estado REAL a partir de las fechas (no solo del campo Estado),
        /// porque el job de suscripciones corre cada hora y el dato guardado puede ir
        /// atrasado. Si ya paso la fecha de bloqueo, aplica aqui mismo la bajada a
        /// Gratis. Devuelve dias restantes y un mensaje listo para mostrar.
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            if (!_userContext.EmpresaId.HasValue)
                return BadRequest(new { message = "Empresa no encontrada" });

            var empresa = await _context.Empresas
                .FirstOrDefaultAsync(e => e.Id == _userContext.EmpresaId);

            if (empresa == null)
                return NotFound(new { message = "Empresa no encontrada" });

            var now = DateTime.UtcNow;

            var config = await _context.EmpresaConfigs
                .FirstOrDefaultAsync(c => c.EmpresaId == empresa.Id);

            var esGratis = config?.Plan == PlanEmpresa.Gratis;

            // Determine actual status based on dates
            var estadoActual = empresa.Estado;

            // Una asignación manual a Gratis también elimina cualquier fecha vieja
            // de la suscripción anterior. La suspensión manual es la única excepción.
            if (esGratis && empresa.Estado != EstadoSuscripcion.Suspendido)
            {
                var necesitaGuardar = empresa.Estado != EstadoSuscripcion.Activo ||
                    empresa.FechaVencimiento.HasValue || empresa.FechaBloqueo.HasValue;

                empresa.Estado = EstadoSuscripcion.Activo;
                empresa.FechaVencimiento = null;
                empresa.FechaBloqueo = null;
                estadoActual = EstadoSuscripcion.Activo;

                if (necesitaGuardar)
                    await _context.SaveChangesAsync();
            }
            // El job nocturno realiza la misma transición, pero este endpoint no
            // puede bloquear al usuario durante las horas que falten para ese job.
            // Una suspensión manual sí se conserva.
            else if (!esGratis &&
                empresa.Estado != EstadoSuscripcion.Suspendido &&
                empresa.FechaBloqueo.HasValue &&
                now > empresa.FechaBloqueo.Value)
            {
                if (config == null)
                {
                    config = PlanDefaults.CrearConfig(empresa.Id, PlanEmpresa.Gratis);
                    _context.EmpresaConfigs.Add(config);
                }
                else
                {
                    PlanDefaults.AplicarA(config, PlanEmpresa.Gratis);
                }

                config.FechaVencimiento = null;
                empresa.Estado = EstadoSuscripcion.Activo;
                empresa.FechaVencimiento = null;
                empresa.FechaBloqueo = null;
                await _context.SaveChangesAsync();

                esGratis = true;
                estadoActual = EstadoSuscripcion.Activo;
            }
            else if (!esGratis &&
                     empresa.Estado != EstadoSuscripcion.Suspendido &&
                     empresa.FechaVencimiento.HasValue &&
                     now > empresa.FechaVencimiento)
            {
                estadoActual = EstadoSuscripcion.Moroso;
            }

            // Calculate days
            int? diasHastaVencimiento = empresa.FechaVencimiento.HasValue
                ? (int)(empresa.FechaVencimiento.Value - now).TotalDays
                : null;

            int? diasHastaBloqueo = empresa.FechaBloqueo.HasValue
                ? (int)(empresa.FechaBloqueo.Value - now).TotalDays
                : null;

            return Ok(new SubscriptionStatusResponse
            {
                Estado = estadoActual.ToString(),
                EstadoCodigo = (int)estadoActual,
                EmpresaNombre = empresa.Nombre,
                FechaVencimiento = empresa.FechaVencimiento,
                FechaBloqueo = empresa.FechaBloqueo,
                DiasHastaVencimiento = diasHastaVencimiento,
                DiasHastaBloqueo = diasHastaBloqueo,
                Mensaje = GetMensajeEstado(estadoActual, diasHastaBloqueo, esGratis),
                PuedeOperar = estadoActual != EstadoSuscripcion.Suspendido
            });
        }

        /// <summary>Texto amigable para el usuario segun el estado y los dias que quedan.</summary>
        private string GetMensajeEstado(EstadoSuscripcion estado, int? diasHastaBloqueo, bool esGratis)
        {
            // El plan Gratis no tiene suscripción que vencer: hablarle de
            // "suscripción activa" a quien nunca pagó solo confunde.
            if (esGratis && estado == EstadoSuscripcion.Activo)
                return "Estás en el plan Gratis.";

            return estado switch
            {
                EstadoSuscripcion.Activo => "Tu suscripción está activa.",
                EstadoSuscripcion.Moroso => diasHastaBloqueo.HasValue && diasHastaBloqueo > 0
                    ? $"Tu suscripción venció. Tienes {diasHastaBloqueo} días para renovar antes de pasar al plan Gratis."
                    : "Tu suscripción venció. Renueva pronto antes de pasar al plan Gratis.",
                EstadoSuscripcion.Suspendido => "Tu cuenta está suspendida. Contacta a soporte para reactivarla.",
                _ => ""
            };
        }
    }

    /// <summary>Forma del JSON que devuelve GET api/subscription/status.</summary>
    public class SubscriptionStatusResponse
    {
        public string Estado { get; set; } = string.Empty;
        public int EstadoCodigo { get; set; }
        public string EmpresaNombre { get; set; } = string.Empty;
        public DateTime? FechaVencimiento { get; set; }
        public DateTime? FechaBloqueo { get; set; }
        public int? DiasHastaVencimiento { get; set; }
        public int? DiasHastaBloqueo { get; set; }
        public string Mensaje { get; set; } = string.Empty;
        public bool PuedeOperar { get; set; }
    }
}
