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
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Panel del dueno de la plataforma (lo consume la app BarberShopAdmin).
/// Con rol SuperAdmin el filtro de tenant no aplica, asi que ve y edita
/// datos de TODAS las empresas: metricas globales, empresas, suscripciones,
/// planes y configuracion, y endpoints para probar los correos.
/// </summary>
[ApiController]
[Route("api/superadmin")]
[Authorize(Roles = "SuperAdmin")]
public class SuperAdminController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<Usuario> _userManager;
    private readonly GmailEmailSender _emailSender;


    /// <summary>Recibe BD, UserManager y el emisor de correos por DI.</summary>
    public SuperAdminController(AppDbContext context, UserManager<Usuario> userManager, GmailEmailSender emailSender)
    {
        _context = context;
        _userManager = userManager;
        _userManager = userManager;
        _emailSender = emailSender;
    }

    // =========================================================
    // GET: api/superadmin/resumen
    // Dashboard del SuperAdmin
    // =========================================================
    /// <summary>Metricas globales: empresas por estado, ingresos del mes (HistorialSuscripciones), empresas por vencer en 7 dias y morosas.</summary>
    [HttpGet("resumen")]
    public async Task<ActionResult<ResumenSuperAdminDto>> GetResumen()
    {
        var empresas = await _context.Empresas.ToListAsync();
        var hoy = DateTime.Today;
        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);

        // Estadísticas de todas las empresas en cuatro consultas, no cuatro por
        // empresa.
        var empresasConStats = await MapEmpresasResumen(empresas);

        // Ingresos del mes (de HistorialSuscripcion)
        var ingresosMes = await _context.HistorialSuscripciones
            .Where(h => h.Fecha >= inicioMes && h.Monto.HasValue)
            .SumAsync(h => h.Monto ?? 0);

        var porVencer = empresasConStats
            .Where(e => e.DiasRestantes > 0 && e.DiasRestantes <= 7 && e.Estado == (int)EstadoSuscripcion.Activo)
            .OrderBy(e => e.DiasRestantes)
            .ToList();

        var morosas = empresasConStats
            .Where(e => e.Estado == (int)EstadoSuscripcion.Moroso)
            .OrderBy(e => e.FechaVencimiento)
            .ToList();

        return Ok(new ResumenSuperAdminDto
        {
            TotalEmpresas = empresas.Count,
            EmpresasActivas = empresas.Count(e => e.Estado == EstadoSuscripcion.Activo),
            EmpresasMorosas = empresas.Count(e => e.Estado == EstadoSuscripcion.Moroso),
            EmpresasSuspendidas = empresas.Count(e => e.Estado == EstadoSuscripcion.Suspendido),
            EmpresasPorVencer7Dias = porVencer.Count,
            IngresosMes = ingresosMes,
            EmpresasPorVencer = porVencer,
            EmpresasMorosasLista = morosas
        });
    }

    // =========================================================
    // GET: api/superadmin/empresas
    // Lista de todas las empresas
    // =========================================================
    /// <summary>Lista de empresas (opcionalmente filtradas por estado) con conteos de uso.</summary>
    [HttpGet("empresas")]
    public async Task<ActionResult<List<EmpresaResumenDto>>> GetEmpresas([FromQuery] int? estado)
    {
        var query = _context.Empresas.AsQueryable();

        if (estado.HasValue)
            query = query.Where(e => (int)e.Estado == estado);

        var empresas = await query.OrderBy(e => e.Nombre).ToListAsync();

        return Ok(await MapEmpresasResumen(empresas));
    }

    // =========================================================
    // GET: api/superadmin/empresas/{id}
    // Detalle de una empresa
    // =========================================================
    /// <summary>Detalle de una empresa: datos, usuarios, uso, plan e historial de suscripcion.</summary>
    [HttpGet("empresas/{id}")]
    public async Task<ActionResult<EmpresaDetalleAdminDto>> GetEmpresa(int id)
    {
        var empresa = await _context.Empresas.FindAsync(id);
        if (empresa == null) return NotFound("Empresa no encontrada");

        var resumen = (await MapEmpresasResumen(new List<Empresa> { empresa })).Single();

        // Buscar admin usando UserManager
        var usersDeEmpresa = await _context.Users
            .Where(u => u.EmpresaId == id)
            .ToListAsync();

        string? adminEmail = null;
        string? adminNombre = null;

        foreach (var user in usersDeEmpresa)
        {
            if (await _userManager.IsInRoleAsync(user, "Admin"))
            {
                adminEmail = user.Email;
                adminNombre = user.NombreCompleto;
                break;
            }
        }
        var config = await _context.EmpresaConfigs
            .Include(c => c.Empresa)
            .FirstOrDefaultAsync(c => c.EmpresaId == id);
        // Historial de pagos
        var historial = await _context.HistorialSuscripciones
            .Where(h => h.EmpresaId == id)
            .OrderByDescending(h => h.Fecha)
            .Take(20)
            .Select(h => new HistorialSuscripcionDto
            {
                Id = h.Id,
                Fecha = h.Fecha,
                Accion = h.Accion,
                Detalle = h.Detalle,
                DiasAgregados = h.DiasAgregados,
                MetodoPago = h.MetodoPago,
                Monto = h.Monto
            })
            .ToListAsync();

        return Ok(new EmpresaDetalleAdminDto
        {
            Id = resumen.Id,
            Nombre = resumen.Nombre,
            Telefono = resumen.Telefono,
            Direccion = resumen.Direccion,
            FechaCreacion = resumen.FechaCreacion,
            Estado = resumen.Estado,
            EstadoNombre = resumen.EstadoNombre,
            FechaVencimiento = resumen.FechaVencimiento,
            FechaBloqueo = resumen.FechaBloqueo,
            DiasRestantes = resumen.DiasRestantes,
            TotalEmpleados = resumen.TotalEmpleados,
            TotalClientes = resumen.TotalClientes,
            CitasUltimos30Dias = resumen.CitasUltimos30Dias,
            AdminEmail = adminEmail,
            AdminNombre = adminNombre,
            HistorialPagos = historial,
            Config = config != null ? MapConfigDto(config) : null
        });
    }

    // =========================================================
    // POST: api/superadmin/extender
    // Extender suscripción
    // =========================================================
    /// <summary>Suma dias a la suscripcion de una empresa (pago manual o cortesia) y lo deja en el historial.</summary>
    [HttpPost("extender")]
    public async Task<IActionResult> ExtenderSuscripcion([FromBody] ExtenderSuscripcionDto dto)
    {
        var empresa = await _context.Empresas.FindAsync(dto.EmpresaId);
        if (empresa == null) return NotFound("Empresa no encontrada");

        // Misma regla que la aprobación de pagos: antes había dos cálculos
        // distintos (uno con Today y otro con UtcNow) para lo mismo.
        empresa.FechaVencimiento = CalculoSuscripcion.CalcularNuevoVencimiento(
            empresa.FechaVencimiento, DateTime.UtcNow, dto.Dias);
        empresa.Estado = EstadoSuscripcion.Activo;
        empresa.FechaBloqueo = null;

        // Registrar en historial
        _context.HistorialSuscripciones.Add(new HistorialSuscripcion
        {
            EmpresaId = dto.EmpresaId,
            Accion = "Extension",
            Detalle = $"Suscripción extendida por {dto.Dias} días",
            DiasAgregados = dto.Dias,
            MetodoPago = dto.MetodoPago,
            Monto = dto.Monto,
            Referencia = dto.Referencia
        });

        await _context.SaveChangesAsync();

        return Ok(new
        {
            Message = $"Suscripción extendida hasta {empresa.FechaVencimiento:dd/MM/yyyy}",
            NuevaFechaVencimiento = empresa.FechaVencimiento
        });
    }

    // =========================================================
    // POST: api/superadmin/cambiar-estado
    // Cambiar estado de suscripción
    // =========================================================
    /// <summary>Cambia a mano el estado (Activo/Moroso/Suspendido). Suspender es la unica forma de bloquear una cuenta.</summary>
    [HttpPost("cambiar-estado")]
    public async Task<IActionResult> CambiarEstado([FromBody] CambiarEstadoSuscripcionDto dto)
    {
        var empresa = await _context.Empresas.FindAsync(dto.EmpresaId);
        if (empresa == null) return NotFound("Empresa no encontrada");

        var estadoAnterior = empresa.Estado;
        empresa.Estado = (EstadoSuscripcion)dto.NuevoEstado;

        if (dto.NuevoEstado == (int)EstadoSuscripcion.Suspendido)
        {
            empresa.FechaBloqueo = DateTime.UtcNow;
        }
        else if (dto.NuevoEstado == (int)EstadoSuscripcion.Activo)
        {
            empresa.FechaBloqueo = null;
        }

        var estadoNombres = new[] { "Activo", "Moroso", "Suspendido" };

        _context.HistorialSuscripciones.Add(new HistorialSuscripcion
        {
            EmpresaId = dto.EmpresaId,
            Accion = "CambioEstado",
            Detalle = $"Estado cambiado de {estadoNombres[(int)estadoAnterior]} a {estadoNombres[dto.NuevoEstado]}. {dto.Motivo}"
        });

        await _context.SaveChangesAsync();

        return Ok(new { Message = "Estado actualizado correctamente" });
    }

    // =========================================================
    // POST: api/superadmin/crear-empresa-trial
    // Crear empresa con trial
    // =========================================================
    /// <summary>Crea una empresa con plan Basico y N dias de prueba (sin usuario; herramienta interna).</summary>
    [HttpPost("crear-empresa-trial")]
    public async Task<IActionResult> CrearEmpresaTrial([FromBody] CrearEmpresaTrialDto dto)
    {
        var empresa = new Empresa
        {
            Nombre = dto.NombreEmpresa,
            Telefono = dto.Telefono ?? string.Empty,
            Estado = EstadoSuscripcion.Activo,
            FechaVencimiento = DateTime.Today.AddDays(dto.DiasTrial)
        };

        _context.Empresas.Add(empresa);
        await _context.SaveChangesAsync();
        var configTrial = PlanDefaults.CrearConfig(empresa.Id, PlanEmpresa.Basico);
        configTrial.FechaVencimiento = DateTime.Today.AddDays(dto.DiasTrial);
        _context.EmpresaConfigs.Add(configTrial);

        _context.HistorialSuscripciones.Add(new HistorialSuscripcion
        {
            EmpresaId = empresa.Id,
            Accion = "Activacion",
            Detalle = $"Trial de {dto.DiasTrial} días activado",
            DiasAgregados = dto.DiasTrial
        });

        await _context.SaveChangesAsync();

        return Ok(new { EmpresaId = empresa.Id, Message = "Empresa creada con trial" });
    }

    // =========================================================
    // Helper: Mapear empresa a resumen
    // =========================================================
    /// <summary>
    /// Cuenta de una vez lo que antes se pedia empresa por empresa.
    ///
    /// MapEmpresaResumen hacia CUATRO consultas -empleados, clientes, citas y
    /// config- y se llamaba dentro de un foreach. Con 100 empresas eran mas de
    /// 400 viajes a la base de datos para responder a un solo endpoint. Ahora son
    /// cuatro, sin importar cuantas haya.
    /// </summary>
    private async Task<List<EmpresaResumenDto>> MapEmpresasResumen(List<Empresa> empresas)
    {
        if (empresas.Count == 0) return new List<EmpresaResumenDto>();

        var hace30Dias = DateTime.Today.AddDays(-30);
        var ids = empresas.Select(e => e.Id).ToList();

        // SuperAdmin salta el filtro global, asi que estas consultas ven todas
        // las empresas; el Contains las acota a las de esta pagina.
        var empleados = (await _context.Empleados
            .AsNoTracking()
            .Where(e => e.Activo && e.EmpresaId != null && ids.Contains(e.EmpresaId.Value))
            .GroupBy(e => e.EmpresaId!.Value)
            .Select(g => new { EmpresaId = g.Key, N = g.Count() })
            .ToListAsync()).ToDictionary(x => x.EmpresaId, x => x.N);

        var clientes = (await _context.Clientes
            .AsNoTracking()
            .Where(c => c.Activo && c.EmpresaId != null && ids.Contains(c.EmpresaId.Value))
            .GroupBy(c => c.EmpresaId!.Value)
            .Select(g => new { EmpresaId = g.Key, N = g.Count() })
            .ToListAsync()).ToDictionary(x => x.EmpresaId, x => x.N);

        var citas = (await _context.Citas
            .AsNoTracking()
            .Where(c => c.Fecha >= hace30Dias && c.EmpresaId != null && ids.Contains(c.EmpresaId.Value))
            .GroupBy(c => c.EmpresaId!.Value)
            .Select(g => new { EmpresaId = g.Key, N = g.Count() })
            .ToListAsync()).ToDictionary(x => x.EmpresaId, x => x.N);

        var configs = (await _context.EmpresaConfigs
            .AsNoTracking()
            // EmpresaConfig no hereda de BaseEntity: su EmpresaId no es nullable.
            .Where(c => ids.Contains(c.EmpresaId))
            .ToListAsync()).ToDictionary(c => c.EmpresaId, c => c);

        return empresas
            .Select(e => MapEmpresaResumen(
                e,
                empleados.GetValueOrDefault(e.Id),
                clientes.GetValueOrDefault(e.Id),
                citas.GetValueOrDefault(e.Id),
                configs.GetValueOrDefault(e.Id)))
            .ToList();
    }

    /// <summary>Arma el resumen de UNA empresa a partir de los conteos ya calculados en lote.</summary>
    private EmpresaResumenDto MapEmpresaResumen(
        Empresa empresa,
        int totalEmpleados,
        int totalClientes,
        int citasUltimos30,
        EmpresaConfig? config)
    {
        var hoy = DateTime.Today;

        var diasRestantes = empresa.FechaVencimiento.HasValue
            ? (int)(empresa.FechaVencimiento.Value - hoy).TotalDays
            : 0;

        var estadoNombres = new Dictionary<EstadoSuscripcion, string>
        {
            { EstadoSuscripcion.Activo, "Activo" },
            { EstadoSuscripcion.Moroso, "Moroso" },
            { EstadoSuscripcion.Suspendido, "Suspendido" }
        };

        return new EmpresaResumenDto
        {
            Id = empresa.Id,
            Nombre = empresa.Nombre,
            Telefono = empresa.Telefono,
            Direccion = empresa.Direccion,
            FechaCreacion = empresa.FechaCreacion,
            Estado = (int)empresa.Estado,
            EstadoNombre = estadoNombres.GetValueOrDefault(empresa.Estado, "Desconocido"),
            FechaVencimiento = empresa.FechaVencimiento,
            FechaBloqueo = empresa.FechaBloqueo,
            DiasRestantes = Math.Max(diasRestantes, 0),
            TotalEmpleados = totalEmpleados,
            TotalClientes = totalClientes,
            CitasUltimos30Dias = citasUltimos30,
            Plan = config != null ? (int)config.Plan : null,
            PlanNombre = config != null ? PlanValidationService.GetPlanNombre(config.Plan) : "Sin plan"
        };
    }
    // ============================================
    // TEST EMAIL ENDPOINTS
    // ============================================

    /// <summary>
    /// Test: Expiration warning email (7, 3, or 1 days before)
    /// </summary>
    [HttpPost("test-email/expiration-warning")]
    public async Task<IActionResult> TestExpirationWarning([FromBody] TestEmailDto dto)
    {
        var user = CreateTestUser(dto);
        var emailService = new SubscriptionEmailService(_emailSender);

        await emailService.SendExpirationWarningEmailAsync(
            user,
            dto.EmpresaNombre ?? "Barbería Test",
            dto.DiasRestantes ?? 7
        );

        return Ok(new { message = $"Expiration warning email sent to {dto.Email}", diasRestantes = dto.DiasRestantes ?? 7 });
    }

    /// <summary>
    /// Test: Subscription expired email (grace period started)
    /// </summary>
    [HttpPost("test-email/expired")]
    public async Task<IActionResult> TestExpired([FromBody] TestEmailDto dto)
    {
        var user = CreateTestUser(dto);
        var emailService = new SubscriptionEmailService(_emailSender);

        await emailService.SendExpiredEmailAsync(
            user,
            dto.EmpresaNombre ?? "Barbería Test",
            dto.DiasRestantes ?? 5
        );

        return Ok(new { message = $"Expired email sent to {dto.Email}", diasDeGracia = dto.DiasRestantes ?? 5 });
    }

    /// <summary>
    /// Test: Urgent reminder email (3 or 1 days before suspension)
    /// </summary>
    [HttpPost("test-email/urgent-reminder")]
    public async Task<IActionResult> TestUrgentReminder([FromBody] TestEmailDto dto)
    {
        var user = CreateTestUser(dto);
        var emailService = new SubscriptionEmailService(_emailSender);

        await emailService.SendUrgentReminderEmailAsync(
            user,
            dto.EmpresaNombre ?? "Barbería Test",
            dto.DiasRestantes ?? 1
        );

        return Ok(new { message = $"Urgent reminder email sent to {dto.Email}", diasRestantes = dto.DiasRestantes ?? 1 });
    }

    /// <summary>
    /// Test: Account suspended email
    /// </summary>
    [HttpPost("test-email/suspended")]
    public async Task<IActionResult> TestSuspended([FromBody] TestEmailDto dto)
    {
        var user = CreateTestUser(dto);
        var emailService = new SubscriptionEmailService(_emailSender);

        await emailService.SendSuspendedEmailAsync(
            user,
            dto.EmpresaNombre ?? "Barbería Test"
        );

        return Ok(new { message = $"Suspended email sent to {dto.Email}" });
    }

    /// <summary>
    /// Test: Renewal confirmation email
    /// </summary>
    [HttpPost("test-email/renewal-confirmation")]
    public async Task<IActionResult> TestRenewalConfirmation([FromBody] TestEmailDto dto)
    {
        var user = CreateTestUser(dto);
        var emailService = new SubscriptionEmailService(_emailSender);

        await emailService.SendRenewalConfirmationEmailAsync(
            user,
            dto.EmpresaNombre ?? "Barbería Test",
            DateTime.UtcNow.AddDays(30)
        );

        return Ok(new { message = $"Renewal confirmation email sent to {dto.Email}" });
    }

    /// <summary>
    /// Test: Welcome email (after email confirmation)
    /// </summary>
    [HttpPost("test-email/welcome")]
    public async Task<IActionResult> TestWelcome([FromBody] TestEmailDto dto)
    {
        var user = CreateTestUser(dto);

        await _emailSender.SendWelcomeEmailAsync(
            user,
            dto.Email,
            dto.EmpresaNombre ?? "Barbería Test"
        );

        return Ok(new { message = $"Welcome email sent to {dto.Email}" });
    }

    /// <summary>
    /// Test: Email confirmation code
    /// </summary>
    [HttpPost("test-email/confirmation-code")]
    public async Task<IActionResult> TestConfirmationCode([FromBody] TestEmailDto dto)
    {
        var user = CreateTestUser(dto);

        await _emailSender.SendEmailConfirmationCodeAsync(
            user,
            dto.Email,
            "123456"
        );

        return Ok(new { message = $"Confirmation code email sent to {dto.Email}", code = "123456" });
    }

    /// <summary>
    /// Test: Password reset code
    /// </summary>
    [HttpPost("test-email/password-reset")]
    public async Task<IActionResult> TestPasswordReset([FromBody] TestEmailDto dto)
    {
        var user = CreateTestUser(dto);

        await _emailSender.SendPasswordResetCodeAsync(
            user,
            dto.Email,
            "654321"
        );

        return Ok(new { message = $"Password reset email sent to {dto.Email}", code = "654321" });
    }

    /// <summary>
    /// Send ALL test emails to see the complete flow
    /// </summary>
    /// <remarks>Los endpoints test-email/* envian cada plantilla a la direccion indicada para revisarlas sin esperar al evento real.</remarks>
    [HttpPost("test-email/all")]
    public async Task<IActionResult> TestAllEmails([FromBody] TestEmailDto dto)
    {
        var user = CreateTestUser(dto);
        var emailService = new SubscriptionEmailService(_emailSender);
        var empresaNombre = dto.EmpresaNombre ?? "Barbería Test";
        var results = new List<string>();

        try
        {
            await _emailSender.SendEmailConfirmationCodeAsync(user, dto.Email, "123456");
            results.Add("✅ Confirmation code");

            await _emailSender.SendWelcomeEmailAsync(user, dto.Email, empresaNombre);
            results.Add("✅ Welcome");

            await emailService.SendExpirationWarningEmailAsync(user, empresaNombre, 7);
            results.Add("✅ Expiration warning (7 days)");

            await emailService.SendExpirationWarningEmailAsync(user, empresaNombre, 1);
            results.Add("✅ Expiration warning (1 day)");

            await emailService.SendExpiredEmailAsync(user, empresaNombre, 5);
            results.Add("✅ Expired (grace period)");

            await emailService.SendUrgentReminderEmailAsync(user, empresaNombre, 3);
            results.Add("✅ Urgent reminder (3 days)");

            await emailService.SendUrgentReminderEmailAsync(user, empresaNombre, 1);
            results.Add("✅ Urgent reminder (1 day)");

            await emailService.SendSuspendedEmailAsync(user, empresaNombre);
            results.Add("✅ Suspended");

            await emailService.SendRenewalConfirmationEmailAsync(user, empresaNombre, DateTime.UtcNow.AddDays(30));
            results.Add("✅ Renewal confirmation");

            await _emailSender.SendPasswordResetCodeAsync(user, dto.Email, "654321");
            results.Add("✅ Password reset");

            return Ok(new
            {
                message = $"All {results.Count} test emails sent to {dto.Email}",
                emails = results
            });
        }
        catch (Exception ex)
        {
            results.Add($"❌ Error: {ex.Message}");
            return BadRequest(new
            {
                message = "Some emails failed",
                emails = results,
                error = ex.Message
            });
        }
    }

    // =========================================================
    // GET: api/superadmin/empresas/{id}/config
    // Ver configuración actual de la empresa
    // =========================================================
    /// <summary>Limites y modulos actuales de una empresa.</summary>
    [HttpGet("empresas/{id}/config")]
    public async Task<ActionResult<EmpresaConfigDto>> GetEmpresaConfig(int id)
    {
        var config = await _context.EmpresaConfigs
            .Include(c => c.Empresa)
            .FirstOrDefaultAsync(c => c.EmpresaId == id);

        if (config == null)
            return NotFound("Esta empresa no tiene configuración. Use el endpoint de asignar plan.");

        return Ok(MapConfigDto(config));
    }

    // =========================================================
    // POST: api/superadmin/empresas/{id}/asignar-plan
    // Asigna un plan con sus defaults (sobreescribe todo)
    // =========================================================
    /// <summary>Cambia el plan de una empresa aplicando sus valores por defecto (PlanDefaults.AplicarA).</summary>
    [HttpPost("empresas/{id}/asignar-plan")]
    public async Task<ActionResult<EmpresaConfigDto>> AsignarPlan(int id, [FromBody] AsignarPlanDto dto)
    {
        var empresa = await _context.Empresas.FindAsync(id);
        if (empresa == null) return NotFound("Empresa no encontrada");

        if (!System.Enum.IsDefined(typeof(PlanEmpresa), dto.Plan))
            return BadRequest("Plan no válido. Use: 0 = Basico, 1 = Profesional, 2 = Premium, 3 = Gratis");

        var plan = (PlanEmpresa)dto.Plan;
        var configExistente = await _context.EmpresaConfigs
            .FirstOrDefaultAsync(c => c.EmpresaId == id);

        if (configExistente != null)
        {
            // Actualizar con defaults del nuevo plan
            PlanDefaults.AplicarA(configExistente, plan);
        }
        else
        {
            var config = PlanDefaults.CrearConfig(id, plan);
            _context.EmpresaConfigs.Add(config);
        }

        if (plan == PlanEmpresa.Gratis && empresa.Estado != EstadoSuscripcion.Suspendido)
        {
            empresa.Estado = EstadoSuscripcion.Activo;
            empresa.FechaVencimiento = null;
            empresa.FechaBloqueo = null;
        }

        // Historial
        _context.HistorialSuscripciones.Add(new HistorialSuscripcion
        {
            EmpresaId = id,
            Accion = "CambioPlan",
            Detalle = $"Plan asignado: {PlanValidationService.GetPlanNombre(plan)}"
        });

        await _context.SaveChangesAsync();

        var configFinal = await _context.EmpresaConfigs
            .Include(c => c.Empresa)
            .FirstAsync(c => c.EmpresaId == id);

        return Ok(MapConfigDto(configFinal));
    }

    // =========================================================
    // PATCH: api/superadmin/empresas/{id}/config
    // Ajuste granular: solo cambia los campos que envíes
    // =========================================================
    /// <summary>
    /// Ajuste fino: cambia solo los campos que vengan en el cuerpo (los null se
    /// ignoran), p. ej. dar 2 empleados extra sin cambiar de plan. Registra cada
    /// cambio en el historial.
    /// </summary>
    [HttpPatch("empresas/{id}/config")]
    public async Task<ActionResult<EmpresaConfigDto>> PatchConfig(int id, [FromBody] PatchConfigDto dto)
    {
        var config = await _context.EmpresaConfigs
            .Include(c => c.Empresa)
            .FirstOrDefaultAsync(c => c.EmpresaId == id);

        if (config == null)
            return NotFound("Esta empresa no tiene configuración. Asigne un plan primero.");

        var cambios = new List<string>();

        // Límites numéricos
        if (dto.MaxEmpleados.HasValue)
        {
            cambios.Add($"MaxEmpleados: {config.MaxEmpleados} → {dto.MaxEmpleados.Value}");
            config.MaxEmpleados = dto.MaxEmpleados.Value;
        }
        if (dto.MaxServicios.HasValue)
        {
            cambios.Add($"MaxServicios: {config.MaxServicios} → {dto.MaxServicios.Value}");
            config.MaxServicios = dto.MaxServicios.Value;
        }
        if (dto.MaxCitasOffline.HasValue)
        {
            cambios.Add($"MaxCitasOffline: {config.MaxCitasOffline} → {dto.MaxCitasOffline.Value}");
            config.MaxCitasOffline = dto.MaxCitasOffline.Value;
        }
        if (dto.RetencionHistorialMeses.HasValue)
        {
            cambios.Add($"RetencionHistorialMeses: {config.RetencionHistorialMeses} → {dto.RetencionHistorialMeses.Value}");
            config.RetencionHistorialMeses = dto.RetencionHistorialMeses.Value;
        }

        // Módulos
        if (dto.ModuloPagosEmpleados.HasValue)
        {
            cambios.Add($"ModuloPagosEmpleados: {config.ModuloPagosEmpleados} → {dto.ModuloPagosEmpleados.Value}");
            config.ModuloPagosEmpleados = dto.ModuloPagosEmpleados.Value;
        }
        if (dto.ModuloReportes.HasValue)
        {
            cambios.Add($"ModuloReportes: {config.ModuloReportes} → {dto.ModuloReportes.Value}");
            config.ModuloReportes = dto.ModuloReportes.Value;
        }
        if (dto.ModuloRecordatorios.HasValue)
        {
            cambios.Add($"ModuloRecordatorios: {config.ModuloRecordatorios} → {dto.ModuloRecordatorios.Value}");
            config.ModuloRecordatorios = dto.ModuloRecordatorios.Value;
        }
        if (dto.ModuloCalendario.HasValue)
        {
            cambios.Add($"ModuloCalendario: {config.ModuloCalendario} → {dto.ModuloCalendario.Value}");
            config.ModuloCalendario = dto.ModuloCalendario.Value;
        }

        // Monetización
        if (dto.MostrarAnuncios.HasValue)
        {
            cambios.Add($"MostrarAnuncios: {config.MostrarAnuncios} → {dto.MostrarAnuncios.Value}");
            config.MostrarAnuncios = dto.MostrarAnuncios.Value;
        }
        if (dto.AccesoWeb.HasValue)
        {
            cambios.Add($"AccesoWeb: {config.AccesoWeb} → {dto.AccesoWeb.Value}");
            config.AccesoWeb = dto.AccesoWeb.Value;
        }

        // Vencimiento
        if (dto.FechaVencimiento.HasValue)
        {
            cambios.Add($"FechaVencimiento: {config.FechaVencimiento?.ToString("dd/MM/yyyy") ?? "sin límite"} → {dto.FechaVencimiento.Value:dd/MM/yyyy}");
            config.FechaVencimiento = dto.FechaVencimiento.Value;
        }
        if (dto.QuitarVencimiento == true)
        {
            cambios.Add("FechaVencimiento removida");
            config.FechaVencimiento = null;
        }

        if (cambios.Count == 0)
            return BadRequest("No se envió ningún campo para actualizar.");

        config.FechaActualizacion = DateTime.UtcNow;

        // Historial
        _context.HistorialSuscripciones.Add(new HistorialSuscripcion
        {
            EmpresaId = id,
            Accion = "AjusteConfig",
            Detalle = string.Join(" | ", cambios)
        });

        await _context.SaveChangesAsync();

        return Ok(MapConfigDto(config));
    }

    // =========================================================
    // GET: api/superadmin/planes/defaults
    // Ver los valores por defecto de cada plan
    // =========================================================
    /// <summary>Los valores por defecto de cada plan, para mostrarlos en el panel.</summary>
    [HttpGet("planes/defaults")]
    public ActionResult<Dictionary<string, EmpresaConfigDto>> GetPlanDefaults()
    {
        var result = new Dictionary<string, EmpresaConfigDto>();

        foreach (PlanEmpresa plan in System.Enum.GetValues(typeof(PlanEmpresa)))
        {
            var config = PlanDefaults.CrearConfig(0, plan);
            result[plan.ToString()] = MapConfigDto(config);
        }

        return Ok(result);
    }

    // =========================================================
    // Helper: Mapear EmpresaConfig a DTO
    // =========================================================
    /// <summary>Convierte EmpresaConfig a DTO.</summary>
    private static EmpresaConfigDto MapConfigDto(EmpresaConfig config)
    {
        return new EmpresaConfigDto
        {
            Id = config.Id,
            EmpresaId = config.EmpresaId,
            EmpresaNombre = config.Empresa?.Nombre,
            Plan = (int)config.Plan,
            PlanNombre = PlanValidationService.GetPlanNombre(config.Plan),
            MaxEmpleados = config.MaxEmpleados,
            MaxServicios = config.MaxServicios,
            MaxCitasOffline = config.MaxCitasOffline,
            RetencionHistorialMeses = config.RetencionHistorialMeses,
            ModuloPagosEmpleados = config.ModuloPagosEmpleados,
            ModuloReportes = config.ModuloReportes,
            ModuloRecordatorios = config.ModuloRecordatorios,
            ModuloCalendario = config.ModuloCalendario,
            MostrarAnuncios = config.MostrarAnuncios,
            AccesoWeb = config.AccesoWeb,
            FechaVencimiento = config.FechaVencimiento,
            FechaCreacion = config.FechaCreacion,
            FechaActualizacion = config.FechaActualizacion
        };
    }

    // ============================================
    // HELPERS
    // ============================================

    /// <summary>Usuario falso en memoria (no se guarda) para rellenar las plantillas de correo de prueba.</summary>
    private Usuario CreateTestUser(TestEmailDto dto)
    {
        return new Usuario
        {
            Email = dto.Email,
            NombreCompleto = dto.NombreUsuario ?? "Usuario Test",
            UserName = dto.Email
        };
    }
}

/// <summary>Cuerpo de crear-empresa-trial.</summary>
public class CrearEmpresaTrialDto
{
    public string NombreEmpresa { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public int DiasTrial { get; set; } = 14;
}

/// <summary>Cuerpo de los endpoints de prueba de correo.</summary>
public class TestEmailDto
{
    public string Email { get; set; } = string.Empty;
    public string? NombreUsuario { get; set; }
    public string? EmpresaNombre { get; set; }
    public int? DiasRestantes { get; set; }
}
