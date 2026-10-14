using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Interfaces;
using BarberShop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Pantalla de Ajustes: perfil del usuario, contrasena, datos de la empresa,
/// info del plan y la suscripcion, y eliminacion de cuenta (requisito de
/// Google Play: el usuario debe poder borrar su cuenta desde la app y la web).
/// </summary>
[ApiController]
[Route("api/ajustes")]
[Authorize]
public class AjustesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IUserContext _userContext;
    private readonly UserManager<BarberShop.Models.Usuario> _userManager;
    private readonly PlanValidationService _planService;
    private readonly ILogger<AjustesController> _logger;

    /// <summary>Recibe BD, UserManager de Identity, contexto del usuario y dependencias por DI.</summary>
    public AjustesController(
        AppDbContext context,
        IUserContext userContext,
        UserManager<BarberShop.Models.Usuario> userManager,
        PlanValidationService planService,
        ILogger<AjustesController> logger)
    {
        _context = context;
        _userContext = userContext;
        _userManager = userManager;
        _planService = planService;
        _logger = logger;
    }

    // =========================================================
    // GET: api/ajustes/plan
    // Obtener el plan del usuario actual
    // =========================================================
    /// <summary>Limites y modulos del plan actual para que la app muestre u oculte funciones.</summary>
    [HttpGet("plan")]
    [Authorize(Roles = "Admin,Barbero")]
    public async Task<IActionResult> GetPlanInfo()
    {
        var empresaId = _userContext.EmpresaId;
        if (!empresaId.HasValue) return NotFound();

        var planInfo = await _planService.GetPlanInfoAsync(empresaId.Value);
        if (planInfo == null) return NotFound("Sin configuración de plan.");

        return Ok(planInfo);
    }
    // =========================================================
    // GET: api/ajustes/perfil
    // Obtener perfil del usuario actual
    // =========================================================
    /// <summary>Nombre, email y datos basicos del usuario logueado.</summary>
    [HttpGet("perfil")]
    public async Task<ActionResult<AjustesUsuarioDto>> GetPerfil()
    {
        var userId = _userContext.UsuarioId;
        var user = await _userManager.FindByIdAsync(userId);

        if (user == null) return NotFound();

        return Ok(new AjustesUsuarioDto
        {
            NombreCompleto = user.NombreCompleto,
            Email = user.Email ?? "",
            Telefono = user.PhoneNumber
        });
    }

    // =========================================================
    // PUT: api/ajustes/perfil
    // Actualizar perfil del usuario
    // =========================================================
    /// <summary>Actualiza los datos del perfil a traves de UserManager (la API de Identity para usuarios).</summary>
    [HttpPut("perfil")]
    public async Task<IActionResult> UpdatePerfil([FromBody] AjustesUsuarioDto dto)
    {
        var userId = _userContext.UsuarioId;
        var user = await _userManager.FindByIdAsync(userId);

        if (user == null) return NotFound();

        user.NombreCompleto = dto.NombreCompleto;
        user.PhoneNumber = dto.Telefono;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return BadRequest(result.Errors.FirstOrDefault()?.Description);

        return Ok(new { Message = "Perfil actualizado" });
    }

    // =========================================================
    // POST: api/ajustes/cambiar-password
    // Cambiar contraseña
    // =========================================================
    /// <summary>
    /// Cambia la contrasena verificando la actual. Identity nunca guarda la
    /// contrasena: guarda un hash (PBKDF2 con sal), y ChangePasswordAsync compara
    /// hashes.
    /// </summary>
    [HttpPost("cambiar-password")]
    public async Task<IActionResult> CambiarPassword([FromBody] CambiarPasswordDto dto)
    {
        var userId = _userContext.UsuarioId;
        var user = await _userManager.FindByIdAsync(userId);

        if (user == null) return NotFound();

        var result = await _userManager.ChangePasswordAsync(user, dto.PasswordActual, dto.PasswordNuevo);

        if (!result.Succeeded)
            return BadRequest(result.Errors.FirstOrDefault()?.Description ?? "Error al cambiar contraseña");

        return Ok(new { Message = "Contraseña actualizada" });
    }

    // =========================================================
    // GET: api/ajustes/empresa
    // Obtener datos de la empresa (Admin only)
    // =========================================================
    /// <summary>Datos de la empresa (nombre, telefono, direccion, logo). Solo Admin.</summary>
    [HttpGet("empresa")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AjustesEmpresaDto>> GetEmpresa()
    {
        var empresaId = _userContext.EmpresaId;
        if (!empresaId.HasValue) return NotFound();

        var empresa = await _context.Empresas.FindAsync(empresaId);
        if (empresa == null) return NotFound();

        return Ok(new AjustesEmpresaDto
        {
            Nombre = empresa.Nombre,
            Telefono = empresa.Telefono,
            Direccion = empresa.Direccion,
            LogoUrl = empresa.LogoUrl
        });
    }

    // =========================================================
    // PUT: api/ajustes/empresa
    // Actualizar datos de la empresa (Admin only)
    // =========================================================
    /// <summary>Actualiza los datos de la empresa. Solo Admin.</summary>
    [HttpPut("empresa")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateEmpresa([FromBody] AjustesEmpresaDto dto)
    {
        var empresaId = _userContext.EmpresaId;
        if (!empresaId.HasValue) return NotFound();

        var empresa = await _context.Empresas.FindAsync(empresaId);
        if (empresa == null) return NotFound();

        empresa.Nombre = dto.Nombre;
        // El DTO los declara opcionales pero la columna es NOT NULL: si el
        // usuario borraba el telefono en Ajustes, se intentaba guardar NULL y el
        // endpoint respondia 500. Vacio es lo que ya guardan casi la mitad de las
        // empresas.
        empresa.Telefono = dto.Telefono ?? string.Empty;
        empresa.Direccion = dto.Direccion ?? string.Empty;

        await _context.SaveChangesAsync();

        return Ok(new { Message = "Empresa actualizada" });
    }

    // =========================================================
    // GET: api/ajustes/suscripcion
    // Info de suscripción de la empresa
    // =========================================================
    /// <summary>Plan, estado, fechas de vencimiento e historial de pagos de la suscripcion.</summary>
    [HttpGet("suscripcion")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<InfoSuscripcionDto>> GetSuscripcion()
    {
        var empresaId = _userContext.EmpresaId;
        if (!empresaId.HasValue) return NotFound();

        var empresa = await _context.Empresas.FindAsync(empresaId);
        if (empresa == null) return NotFound();

        var diasRestantes = empresa.FechaVencimiento.HasValue
            ? (int)(empresa.FechaVencimiento.Value - DateTime.Today).TotalDays
            : 0;

        var estadoNombres = new Dictionary<EstadoSuscripcion, string>
        {
            { EstadoSuscripcion.Activo, "Activo" },
            { EstadoSuscripcion.Moroso, "Moroso" },
            { EstadoSuscripcion.Suspendido, "Suspendido" }
        };

        string mensaje;
        bool requierePago = false;

        if (empresa.Estado == EstadoSuscripcion.Suspendido)
        {
            mensaje = "Tu cuenta está suspendida. Contacta al administrador para reactivarla.";
            requierePago = true;
        }
        else if (empresa.Estado == EstadoSuscripcion.Moroso)
        {
            mensaje = "Tu suscripción ha vencido. Renueva para seguir usando la app.";
            requierePago = true;
        }
        else if (diasRestantes <= 7)
        {
            mensaje = $"Tu suscripción vence en {diasRestantes} días. ¡Renueva pronto!";
            requierePago = diasRestantes <= 3;
        }
        else
        {
            mensaje = $"Suscripción activa. {diasRestantes} días restantes.";
        }

        return Ok(new InfoSuscripcionDto
        {
            Estado = (int)empresa.Estado,
            EstadoNombre = estadoNombres.GetValueOrDefault(empresa.Estado, "Desconocido"),
            FechaVencimiento = empresa.FechaVencimiento,
            DiasRestantes = Math.Max(diasRestantes, 0),
            RequierePago = requierePago,
            Mensaje = mensaje
        });
    }
    // =========================================================
    // DELETE: api/ajustes/eliminar-cuenta
    // Eliminar cuenta del usuario y todos sus datos (Play Store compliance)
    // =========================================================
    /// <summary>
    /// Borra al usuario logueado. Si es el ULTIMO usuario de la empresa, borra
    /// tambien toda la empresa: hay que eliminar las tablas hijas antes que las
    /// padres (orden "por niveles") para no violar claves foraneas. Todo en una
    /// transaccion: si algo falla no queda una cuenta a medio borrar.
    /// </summary>
    [HttpDelete("eliminar-cuenta")]
    public async Task<IActionResult> EliminarCuenta()
    {
        var userId = _userContext.UsuarioId;
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound("Usuario no encontrado.");

        var empresaId = user.EmpresaId;

        // ── Verificar si es el último usuario de la empresa ──
        var totalUsuarios = await _context.Users
            .CountAsync(u => u.EmpresaId == empresaId);

        var esUltimoUsuario = totalUsuarios <= 1;

        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // ══════════════════════════════════════════════════
            // 1) Siempre: eliminar datos Identity del usuario
            // ══════════════════════════════════════════════════

            // Códigos de verificación
            var codigos = await _context.CodigosVerificacion
                .Where(c => c.UserId == userId)
                .ToListAsync();
            _context.CodigosVerificacion.RemoveRange(codigos);

            // Tablas Identity (roles, claims, logins, tokens)
            _context.UserRoles.RemoveRange(
                await _context.UserRoles.Where(ur => ur.UserId == userId).ToListAsync());
            _context.UserClaims.RemoveRange(
                await _context.UserClaims.Where(uc => uc.UserId == userId).ToListAsync());
            _context.UserLogins.RemoveRange(
                await _context.UserLogins.Where(ul => ul.UserId == userId).ToListAsync());
            _context.UserTokens.RemoveRange(
                await _context.UserTokens.Where(ut => ut.UserId == userId).ToListAsync());

            // ══════════════════════════════════════════════════
            // 2) Si es el último usuario → borrar TODA la empresa
            // ══════════════════════════════════════════════════
            if (esUltimoUsuario)
            {
                // ── Nivel 4: tablas más profundas ──

                var abonosPago = await _context.AbonosPago
                    .Where(a => a.EmpresaId == empresaId)
                    .ToListAsync();
                _context.AbonosPago.RemoveRange(abonosPago);

                var citaServicios = await _context.CitasServicios
                    .Where(cs => cs.EmpresaId == empresaId)
                    .ToListAsync();
                _context.CitasServicios.RemoveRange(citaServicios);

                // ── Nivel 3 ──

                var cuentasCliente = await _context.CuentasCliente
                    .Where(cc => cc.EmpresaId == empresaId)
                    .ToListAsync();
                _context.CuentasCliente.RemoveRange(cuentasCliente);

                var recordatorios = await _context.Recordatorios
                    .Where(r => r.EmpresaId == empresaId)
                    .ToListAsync();
                _context.Recordatorios.RemoveRange(recordatorios);

                var citas = await _context.Citas
                    .Where(c => c.EmpresaId == empresaId)
                    .ToListAsync();
                _context.Citas.RemoveRange(citas);

                var pagosNomina = await _context.PagosNomina
                    .Where(p => p.EmpresaId == empresaId)
                    .ToListAsync();
                _context.PagosNomina.RemoveRange(pagosNomina);

                // ── Nivel 2 ──

                var clientes = await _context.Clientes
                    .Where(c => c.EmpresaId == empresaId)
                    .ToListAsync();
                _context.Clientes.RemoveRange(clientes);

                var empleados = await _context.Empleados
                    .Where(e => e.EmpresaId == empresaId)
                    .ToListAsync();
                _context.Empleados.RemoveRange(empleados);

                var servicios = await _context.Servicios
                    .Where(s => s.EmpresaId == empresaId)
                    .ToListAsync();
                _context.Servicios.RemoveRange(servicios);

                var pagosSuscripcion = await _context.PagoSuscripcion
                    .Where(p => p.EmpresaId == empresaId)
                    .ToListAsync();
                _context.PagoSuscripcion.RemoveRange(pagosSuscripcion);

                // ── Nivel 1 ──

                var config = await _context.EmpresaConfigs
                    .FirstOrDefaultAsync(ec => ec.EmpresaId == empresaId);
                if (config != null)
                    _context.EmpresaConfigs.Remove(config);

                var historial = await _context.HistorialSuscripciones
                    .Where(h => h.EmpresaId == empresaId)
                    .ToListAsync();
                _context.HistorialSuscripciones.RemoveRange(historial);

                // ── Otros usuarios de la empresa (si los hubiera) ──
                var otrosUsuarios = await _context.Users
                    .Where(u => u.EmpresaId == empresaId && u.Id != userId)
                    .ToListAsync();

                foreach (var otro in otrosUsuarios)
                {
                    _context.CodigosVerificacion.RemoveRange(
                        await _context.CodigosVerificacion.Where(c => c.UserId == otro.Id).ToListAsync());
                    _context.UserRoles.RemoveRange(
                        await _context.UserRoles.Where(ur => ur.UserId == otro.Id).ToListAsync());
                    _context.UserClaims.RemoveRange(
                        await _context.UserClaims.Where(uc => uc.UserId == otro.Id).ToListAsync());
                    _context.UserLogins.RemoveRange(
                        await _context.UserLogins.Where(ul => ul.UserId == otro.Id).ToListAsync());
                    _context.UserTokens.RemoveRange(
                        await _context.UserTokens.Where(ut => ut.UserId == otro.Id).ToListAsync());
                    _context.Users.Remove(otro);
                }

                // ── Eliminar la empresa ──
                var empresa = await _context.Empresas.FindAsync(empresaId);
                if (empresa != null)
                    _context.Empresas.Remove(empresa);
            }

            // ══════════════════════════════════════════════════
            // 3) Eliminar el usuario solicitante
            //    (directamente en el context, NO con UserManager)
            // ══════════════════════════════════════════════════
            _context.Users.Remove(user);

            // ── Un solo SaveChanges para todo ──
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                Message = "Cuenta y datos eliminados correctamente.",
                EmpresaEliminada = esUltimoUsuario
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            // Es la operacion mas destructiva del sistema: si falla y no queda
            // traza, no hay forma de saber si los datos del usuario se borraron a
            // medias ni de responderle cuando pregunte.
            _logger.LogError(ex,
                "Fallo al eliminar la cuenta del usuario {UserId} (empresa {EmpresaId})",
                _userContext.UsuarioId, _userContext.EmpresaId);
            return StatusCode(500, "Error interno al eliminar la cuenta.");
        }
    }
    // =========================================================
    // POST: api/ajustes/eliminar-cuenta-web
    // Eliminar cuenta desde la web (sin JWT, usa email+password)
    // Play Store compliance: users must be able to delete
    // their account without reinstalling the app
    // =========================================================
    /// <summary>
    /// Misma eliminacion pero desde la pagina web publica (delete-account.html),
    /// sin JWT: se autentica con email y contrasena en el cuerpo. [AllowAnonymous]
    /// anula el [Authorize] del controlador para este endpoint.
    /// </summary>
    [HttpPost("eliminar-cuenta-web")]
    [AllowAnonymous]
    public async Task<IActionResult> EliminarCuentaWeb([FromBody] EliminarCuentaWebDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { message = "Email y contraseña son obligatorios." });

        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null)
            return NotFound(new { message = "No se encontró una cuenta con ese correo." });

        // Quien se registro con Google no tiene contrasena, asi que CheckPasswordAsync
        // siempre fallaria y veria "credenciales incorrectas" sin entender por que.
        // Se le indica el camino que si funciona en su caso.
        if (!await _userManager.HasPasswordAsync(user))
        {
            return BadRequest(new
            {
                message = "Esta cuenta inicia sesión con Google y no tiene contraseña. " +
                          "Elimínala desde la app en Configuración → Eliminar cuenta, " +
                          "o escríbenos a cuadreapp@gmail.com y lo hacemos por ti.",
                code = "CUENTA_SIN_PASSWORD"
            });
        }

        var passwordValid = await _userManager.CheckPasswordAsync(user, dto.Password);
        if (!passwordValid)
            return Unauthorized(new { message = "Credenciales incorrectas." });

        var empresaId = user.EmpresaId;
        var userId = user.Id;

        var totalUsuarios = await _context.Users
            .CountAsync(u => u.EmpresaId == empresaId);

        var esUltimoUsuario = totalUsuarios <= 1;

        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Datos Identity del usuario
            var codigos = await _context.CodigosVerificacion
                .Where(c => c.UserId == userId).ToListAsync();
            _context.CodigosVerificacion.RemoveRange(codigos);

            _context.UserRoles.RemoveRange(
                await _context.UserRoles.Where(ur => ur.UserId == userId).ToListAsync());
            _context.UserClaims.RemoveRange(
                await _context.UserClaims.Where(uc => uc.UserId == userId).ToListAsync());
            _context.UserLogins.RemoveRange(
                await _context.UserLogins.Where(ul => ul.UserId == userId).ToListAsync());
            _context.UserTokens.RemoveRange(
                await _context.UserTokens.Where(ut => ut.UserId == userId).ToListAsync());

            if (esUltimoUsuario)
            {
                // Nivel 4
                _context.AbonosPago.RemoveRange(
                    await _context.AbonosPago.IgnoreQueryFilters()
                        .Where(a => a.EmpresaId == empresaId).ToListAsync());
                _context.CitasServicios.RemoveRange(
                    await _context.CitasServicios.IgnoreQueryFilters()
                        .Where(cs => cs.EmpresaId == empresaId).ToListAsync());

                // Nivel 3
                _context.CuentasCliente.RemoveRange(
                    await _context.CuentasCliente.IgnoreQueryFilters()
                        .Where(cc => cc.EmpresaId == empresaId).ToListAsync());
                _context.Recordatorios.RemoveRange(
                    await _context.Recordatorios.IgnoreQueryFilters()
                        .Where(r => r.EmpresaId == empresaId).ToListAsync());
                _context.Citas.RemoveRange(
                    await _context.Citas.IgnoreQueryFilters()
                        .Where(c => c.EmpresaId == empresaId).ToListAsync());
                _context.PagosNomina.RemoveRange(
                    await _context.PagosNomina.IgnoreQueryFilters()
                        .Where(p => p.EmpresaId == empresaId).ToListAsync());

                // Nivel 2
                _context.Clientes.RemoveRange(
                    await _context.Clientes.IgnoreQueryFilters()
                        .Where(c => c.EmpresaId == empresaId).ToListAsync());
                _context.Empleados.RemoveRange(
                    await _context.Empleados.IgnoreQueryFilters()
                        .Where(e => e.EmpresaId == empresaId).ToListAsync());
                _context.Servicios.RemoveRange(
                    await _context.Servicios.IgnoreQueryFilters()
                        .Where(s => s.EmpresaId == empresaId).ToListAsync());
                _context.PagoSuscripcion.RemoveRange(
                    await _context.PagoSuscripcion.IgnoreQueryFilters()
                        .Where(p => p.EmpresaId == empresaId).ToListAsync());

                // Nivel 1
                var config = await _context.EmpresaConfigs
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(ec => ec.EmpresaId == empresaId);
                if (config != null) _context.EmpresaConfigs.Remove(config);

                _context.HistorialSuscripciones.RemoveRange(
                    await _context.HistorialSuscripciones.IgnoreQueryFilters()
                        .Where(h => h.EmpresaId == empresaId).ToListAsync());

                // Otros usuarios
                var otrosUsuarios = await _context.Users
                    .Where(u => u.EmpresaId == empresaId && u.Id != userId)
                    .ToListAsync();

                foreach (var otro in otrosUsuarios)
                {
                    _context.CodigosVerificacion.RemoveRange(
                        await _context.CodigosVerificacion.Where(c => c.UserId == otro.Id).ToListAsync());
                    _context.UserRoles.RemoveRange(
                        await _context.UserRoles.Where(ur => ur.UserId == otro.Id).ToListAsync());
                    _context.UserClaims.RemoveRange(
                        await _context.UserClaims.Where(uc => uc.UserId == otro.Id).ToListAsync());
                    _context.UserLogins.RemoveRange(
                        await _context.UserLogins.Where(ul => ul.UserId == otro.Id).ToListAsync());
                    _context.UserTokens.RemoveRange(
                        await _context.UserTokens.Where(ut => ut.UserId == otro.Id).ToListAsync());
                    _context.Users.Remove(otro);
                }

                var empresa = await _context.Empresas.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(e => e.Id == empresaId);
                if (empresa != null) _context.Empresas.Remove(empresa);
            }

            _context.Users.Remove(user);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                message = "Cuenta y datos eliminados correctamente.",
                empresaEliminada = esUltimoUsuario
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            // Ruta web (anonima): aqui no hay sesion, asi que el correo es lo
            // unico que permite localizar despues al usuario afectado.
            _logger.LogError(ex,
                "Fallo al eliminar la cuenta por la via web para {Email}", dto?.Email);
            return StatusCode(500, new { message = "Error interno al eliminar la cuenta." });
        }
    }


}