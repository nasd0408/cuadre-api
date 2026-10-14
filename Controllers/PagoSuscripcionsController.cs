using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Helpers;
using BarberShop.Interfaces;
using BarberShop.Models;
using BarberShop.Services;
using BarberShop.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace BarberShop.Controllers
{
    /// <summary>
    /// Pagos MANUALES de suscripcion: el cliente transfiere (Pago Movil, Binance,
    /// transferencia), registra la referencia aqui o en la landing, y el
    /// SuperAdmin la revisa y aprueba o rechaza desde BarberShopAdmin. Aprobar
    /// extiende 30 dias la suscripcion y aplica el plan pedido. (Integrar una
    /// pasarela de pago real automatizaria justo este flujo.)
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PagosSuscripcionController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IUserContext _userContext;
        private readonly UserManager<Usuario> _userManager;
        private readonly GmailEmailSender _emailSender;
        private readonly ILogger<PagosSuscripcionController> _logger;

        // Cada pago aprobado extiende la suscripción este número de días
        private const int DIAS_POR_PAGO = 30;

        /// <summary>Recibe BD, contexto, UserManager, emisor de correo y logger por DI.</summary>
        public PagosSuscripcionController(
            AppDbContext context,
            IUserContext userContext,
            UserManager<Usuario> userManager,
            GmailEmailSender emailSender,
            ILogger<PagosSuscripcionController> logger)
        {
            _context = context;
            _userContext = userContext;
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        // 1. El Admin sube su comprobante
        /// <summary>El Admin registra su pago (plan, referencia, monto, metodo). Queda "Pendiente".</summary>
        [HttpPost("registrar")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<PagoSuscripcion>> RegistrarPago([FromBody] CreatePagoSuscripcionDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (!System.Enum.IsDefined(dto.PlanSolicitado) || dto.PlanSolicitado == PlanEmpresa.Gratis)
                return BadRequest(new { message = "Selecciona un plan de pago válido." });

            var empresaId = _userContext.EmpresaId;
            if (!empresaId.HasValue) return Unauthorized();

            var pago = new PagoSuscripcion
            {
                PlanSolicitado = dto.PlanSolicitado,
                NumeroReferencia = dto.NumeroReferencia,
                FechaPago = dto.FechaPago,
                MetodoPago = dto.MetodoPago,
                Monto = dto.Monto,
                Notas = dto.Notas,
                Estado = "Pendiente"
            };

            _context.PagoSuscripcion.Add(pago);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Pago registrado. Esperando validación del SuperAdmin.", idPago = pago.Id });
        }

        // 2. El SuperAdmin ve todos los pagos pendientes de todas las empresas
        /// <summary>Pagos pendientes de todas las empresas (el SuperAdmin ve todo gracias a IsSuperAdmin en el filtro).</summary>
        [HttpGet("pendientes")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<IEnumerable<PagoSuscripcion>>> GetPagosPendientes()
        {
            return await _context.PagoSuscripcion
                .Where(p => p.Estado == "Pendiente")
                .OrderByDescending(p => p.FechaCreacion)
                .ToListAsync();
        }
        // 2. El SuperAdmin ve todos los pagos pendientes de todas las empresas
        /// <summary>Todos los pagos de todas las empresas. Solo SuperAdmin.</summary>
        [HttpGet("todo-pagos")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<IEnumerable<PagoSuscripcion>>> GetTodosLosPagos()
        {
            return await _context.PagoSuscripcion
                .OrderByDescending(p => p.FechaCreacion)
                .ToListAsync();
        }

        /// <summary>Pagos de una empresa concreta. Solo SuperAdmin.</summary>
        [HttpGet("empresa/{empresaId:int}")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<ActionResult<IEnumerable<PagoSuscripcion>>> GetPagosEmpresa(int empresaId)
        {
            return await _context.PagoSuscripcion
                .Where(p => p.EmpresaId == empresaId)
                .OrderByDescending(p => p.FechaCreacion)
                .ToListAsync();
        }
        // 3. El SuperAdmin aprueba o rechaza un pago.
        // Aprobar activa la suscripción completa: extiende vencimiento,
        // aplica el plan solicitado, registra historial y notifica por email.
        /// <summary>
        /// El SuperAdmin aprueba o rechaza un pago. Al aprobar, en una transaccion:
        /// extiende el vencimiento (CalculoSuscripcion), aplica el plan, reactiva la
        /// empresa y guarda el historial. El correo de confirmacion va despues y si
        /// falla no deshace la aprobacion ("best-effort").
        /// </summary>
        [HttpPut("validar/{id:int}")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> ValidarPago(int id, [FromBody] string nuevoEstado)
        {
            if (nuevoEstado != "Aprobado" && nuevoEstado != "Rechazado")
                return BadRequest(new { message = "Estado no válido. Use \"Aprobado\" o \"Rechazado\"." });

            var pago = await _context.PagoSuscripcion.FindAsync(id);
            if (pago == null) return NotFound();

            if (pago.Estado != "Pendiente")
                return BadRequest(new { message = $"Este pago ya fue procesado (estado actual: {pago.Estado})." });

            pago.Estado = nuevoEstado;

            if (nuevoEstado == "Rechazado")
            {
                await _context.SaveChangesAsync();
                return NoContent();
            }

            // ---- Aprobado: activar la suscripción ----
            if (pago.EmpresaId is not int empresaId || empresaId == 0)
                return BadRequest(new { message = "El pago no tiene empresa asociada." });

            var empresa = await _context.Empresas.FindAsync(empresaId);
            if (empresa == null)
                return NotFound(new { message = "La empresa del pago no existe." });

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                empresa.FechaVencimiento = CalculoSuscripcion.CalcularNuevoVencimiento(
                    empresa.FechaVencimiento, DateTime.UtcNow, DIAS_POR_PAGO);
                empresa.Estado = EstadoSuscripcion.Activo;
                empresa.FechaBloqueo = null;

                // Aplicar el plan solicitado (defaults del plan si cambia)
                var config = await _context.EmpresaConfigs
                    .FirstOrDefaultAsync(c => c.EmpresaId == empresaId);

                if (config == null)
                {
                    _context.EmpresaConfigs.Add(PlanDefaults.CrearConfig(empresaId, pago.PlanSolicitado));
                }
                else if (config.Plan != pago.PlanSolicitado)
                {
                    PlanDefaults.AplicarA(config, pago.PlanSolicitado);
                }

                _context.HistorialSuscripciones.Add(new HistorialSuscripcion
                {
                    EmpresaId = empresaId,
                    Accion = "Renovacion",
                    Detalle = $"Pago #{pago.Id} aprobado. Plan {pago.PlanSolicitado}, +{DIAS_POR_PAGO} días",
                    DiasAgregados = DIAS_POR_PAGO,
                    MetodoPago = pago.MetodoPago,
                    Monto = pago.Monto,
                    Referencia = pago.NumeroReferencia
                });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error al aprobar pago {PagoId}", id);
                return StatusCode(500, new { message = "Error interno al aprobar el pago." });
            }

            // Email de confirmación: best-effort, no debe revertir la aprobación
            try
            {
                var adminUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.EmpresaId == empresaId);

                if (adminUser != null && empresa.FechaVencimiento.HasValue)
                {
                    var emailService = new SubscriptionEmailService(_emailSender);
                    await emailService.SendRenewalConfirmationEmailAsync(
                        adminUser, empresa.Nombre, empresa.FechaVencimiento.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pago {PagoId} aprobado pero falló el email de confirmación", id);
            }

            return NoContent();
        }

        // 4. El Admin ve su propio historial de pagos
        /// <summary>Pagos de la empresa del Admin logueado.</summary>
        [HttpGet("mis-pagos")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<IEnumerable<PagoSuscripcion>>> GetMisPagos()
        {
            return await _context.PagoSuscripcion
                .OrderByDescending(p => p.FechaCreacion)
                .ToListAsync();
        }
        // ---------- Endpoint público (sin [Authorize]) ----------
        /// <summary>
        /// Recibe pagos desde la landing page externa.
        /// Busca el email en la tabla de usuarios para asociar la empresa.
        /// No requiere autenticación.
        /// </summary>
        [HttpPost("landing")]
        [AllowAnonymous]
        [EnableRateLimiting("codes")]
        public async Task<IActionResult> RegistrarPagoDestinoLanding([FromBody] LandingPagoDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // UserManager NO pasa por el query filter de tenant
            var usuario = await _userManager.FindByEmailAsync(dto.Email.Trim());

            if (usuario == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "El correo no está registrado. Descarga la app y crea tu cuenta primero."
                });
            }

            if (usuario.EmpresaId == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Tu cuenta no tiene un negocio asociado."
                });
            }

            if (!TryMapPlan(dto.Plan, out var planEnum))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Plan no válido."
                });
            }

            var pago = new PagoSuscripcion
            {
                EmpresaId = usuario.EmpresaId,  // ← esto ya viene seteado, SaveChanges lo respeta
                PlanSolicitado = planEnum,
                NumeroReferencia = dto.ReferenciaPago.Trim(),
                MetodoPago = MapMetodoPago(dto.MetodoPago),
                FechaPago = DateTime.UtcNow,
                Monto = GetMontoPlan(planEnum),
                Estado = "Pendiente",
                Notas = $"Landing: {dto.Nombre.Trim()} ({dto.Email.Trim()})"
            };

            _context.PagoSuscripcion.Add(pago);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "¡Pago registrado! Validaremos tu pago en menos de 24 horas.",
                idPago = pago.Id
            });
        }

        // ---------- Helpers privados ----------

        /// <summary>
        /// "gratis" queda deliberadamente fuera: es un plan sin pago, así que un registro
        /// de pago para Gratis solo ensuciaría la cola de validación del SuperAdmin.
        /// El CTA de Gratis en el landing va a registro, no a este endpoint.
        /// </summary>
        private static bool TryMapPlan(string plan, out PlanEmpresa result)
        {
            result = default;
            return plan?.ToLower().Trim() switch
            {
                "basico" => Assign(PlanEmpresa.Basico, out result),
                "profesional" => Assign(PlanEmpresa.Profesional, out result),
                "premium" => Assign(PlanEmpresa.Premium, out result),
                _ => false
            };
        }

        /// <summary>Ayudante para asignar un parametro "out" dentro de una switch expression.</summary>
        private static bool Assign(PlanEmpresa value, out PlanEmpresa result)
        {
            result = value;
            return true;
        }

        /// <summary>Traduce el codigo de metodo de pago que manda la landing a un nombre legible.</summary>
        private static string MapMetodoPago(string metodo)
        {
            return metodo?.ToLower().Trim() switch
            {
                "binance" => "Binance Pay",
                "pago_movil" => "Pago Móvil",
                "transferencia" => "Transferencia Bancaria",
                "otro" => "Otro",
                _ => metodo ?? "No especificado"
            };
        }

        /// <summary>
        /// Precio mensual en USD. Debe coincidir con la sección #planes del landing.
        /// </summary>
        private static decimal GetMontoPlan(PlanEmpresa plan)
        {
            return plan switch
            {
                PlanEmpresa.Gratis => 0m,
                PlanEmpresa.Basico => 4.99m,
                PlanEmpresa.Profesional => 9.99m,
                PlanEmpresa.Premium => 19.99m,
                _ => 0m
            };
        }
        /// <summary>Cuerpo de RegistrarPago, con validaciones por atributos.</summary>
        public class CreatePagoSuscripcionDto
        {
            [Required]
            public PlanEmpresa PlanSolicitado { get; set; }

            [Required]
            [StringLength(100)]
            public string NumeroReferencia { get; set; } = null!;

            [Required]
            public DateTime FechaPago { get; set; }

            [Required]
            [StringLength(50)]
            public string MetodoPago { get; set; } = null!;

            [Required]
            [Range(0.01, double.MaxValue)]
            public decimal Monto { get; set; }

            public string? Notas { get; set; }
        }
    }

}
