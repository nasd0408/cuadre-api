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
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace BarberShop.Controllers
{
    /// <summary>
    /// Autenticacion: registro de negocios, login (email/contrasena y Google),
    /// confirmacion de email, recuperacion de contrasena, refresh/logout y alta
    /// de empleados con cuenta propia.
    ///
    /// Como funciona la sesion (JWT + refresh token):
    ///   1. Login correcto -> se devuelven DOS tokens:
    ///      - JWT (access token, 12 h): firmado con JWT:Secret, lleva "claims"
    ///        (id de usuario, EmpresaId, roles). El servidor NO lo guarda: lo
    ///        valida con la firma en cada peticion (sin estado / "stateless").
    ///      - Refresh token (aleatorio, larga duracion): SI se guarda en BD, pero
    ///        solo su hash SHA-256, igual que una contrasena.
    ///   2. La app manda el JWT en la cabecera "Authorization: Bearer ...".
    ///   3. Cuando el JWT caduca, la app llama a /refresh con el refresh token y
    ///      recibe un par nuevo; el viejo se revoca ("rotacion").
    ///   4. Logout revoca el refresh token.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<Usuario> _userManager;
        private readonly IConfiguration _configuration;
        private readonly IUserContext _userContext;
        private readonly AppDbContext _context;
        private readonly GmailEmailSender _emailSender;
        private readonly ICodigoVerificacionService _codigoService;
        private readonly GoogleTokenValidator _googleValidator;
        private readonly ILogger<AuthController> _logger;

        /// <summary>
        /// Recibe por DI: UserManager (API de Identity para crear usuarios, validar
        /// contrasenas y roles), configuracion, BD, correo, codigos, Google y logger.
        /// </summary>
        public AuthController(
            UserManager<Usuario> userManager,
            IConfiguration configuration,
            IUserContext userContext,
            AppDbContext context,
            GmailEmailSender emailSender,           // FIX #4: inject directly, no hard cast needed
            ICodigoVerificacionService codigoService,
            GoogleTokenValidator googleValidator,
            ILogger<AuthController> logger)
        {
            _userManager = userManager;
            _configuration = configuration;
            _userContext = userContext;
            _context = context;
            _emailSender = emailSender;
            _codigoService = codigoService;
            _googleValidator = googleValidator;
            _logger = logger;
        }

        // =====================================================
        // REGISTRO DE NEGOCIO (Owner/Admin)
        // =====================================================

        /// <summary>
        /// Registra una nueva barbería y su administrador.
        /// Envía código de confirmación por email.
        /// </summary>
        /// <remarks>
        /// Crea Empresa + EmpresaConfig (plan Gratis) + Usuario con rol Admin dentro
        /// de una transaccion, y manda el codigo de 6 digitos por correo. El usuario
        /// no puede entrar hasta confirmar (RequireConfirmedEmail en Program.cs).
        /// [EnableRateLimiting("auth")] aplica la politica de 10 peticiones/minuto por IP.
        /// </remarks>
        [HttpPost("register")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> RegisterBusiness([FromBody] RegisterBusinessDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // FIX #2: Use IgnoreQueryFilters() so the tenant filter (null EmpresaId on unauthenticated
            // requests) doesn't cause the duplicate check to silently return zero rows.
            if (await _context.Empresas.IgnoreQueryFilters()
                    .AnyAsync(e => e.Nombre == dto.NombreBarberia))
                return BadRequest(new { message = "Ya existe un negocio con ese nombre" });

            if (await _userManager.FindByEmailAsync(dto.Email) != null)
                return BadRequest(new { message = "El correo ya está registrado" });

            // FIX #1: Remove ExecutionStrategy wrapper — using it together with a manually opened
            // transaction throws on PostgreSQL/Npgsql. Use the transaction directly instead.
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1. Crear empresa
                var nuevaEmpresa = new Empresa
                {
                    Nombre = dto.NombreBarberia,
                    Telefono = dto.PhoneNumber?.Trim() ?? string.Empty,
                    Direccion = dto.Direccion?.Trim() ?? string.Empty,
                    Estado = BarberShop.Enum.EstadoSuscripcion.Activo,
                    // Sin trial: el plan Gratis no vence, así que no hay fechas de
                    // vencimiento ni de bloqueo que gestionar.
                    FechaVencimiento = null,
                    FechaBloqueo = null
                };

                _context.Empresas.Add(nuevaEmpresa);
                await _context.SaveChangesAsync();

                // 1.5 Crear configuración con plan Gratis (monetizado con anuncios)
                var config = PlanDefaults.CrearConfig(nuevaEmpresa.Id, PlanEmpresa.Gratis);
                _context.EmpresaConfigs.Add(config);

                // 1.6 Crear empleado (el dueño)
                var empleado = new Empleado
                {
                    Nombre = dto.NombreCompleto,
                    PorcentajeComision = 100,
                    Periodicidad = PeriodicidadPago.Quincenal,
                    EmpresaId = nuevaEmpresa.Id
                };
                _context.Empleados.Add(empleado);
                await _context.SaveChangesAsync();

                // 2. Crear usuario
                var nuevoUsuario = new Usuario
                {
                    UserName = dto.Email,
                    Email = dto.Email,
                    NombreCompleto = dto.NombreCompleto,
                    PhoneNumber = dto.PhoneNumber?.Trim(),
                    EmpresaId = nuevaEmpresa.Id,
                    EmailConfirmed = false
                };

                var result = await _userManager.CreateAsync(nuevoUsuario, dto.Password);

                if (!result.Succeeded)
                    throw new Exception("Error al crear usuario: " +
                        string.Join(", ", result.Errors.Select(e => e.Description)));

                await _userManager.AddToRoleAsync(nuevoUsuario, "Admin");

                // 3. Código email
                var codigo = await _codigoService.GenerarCodigoAsync(
                    nuevoUsuario.Id,
                    TipoCodigoVerificacion.ConfirmacionEmail);

                // El commit va ANTES del email, igual que en el registro con Google.
                //
                // Antes se enviaba dentro de la transaccion: HttpClient no tenia
                // timeout configurado (100 s por defecto), asi que un Brevo lento
                // mantenia abierta una transaccion de Postgres ocupando una
                // conexion del pool. Y si Brevo fallaba, el rollback tiraba un
                // registro perfectamente valido.
                //
                // Es seguro moverlo porque existe /auth/resend-confirmation y la
                // app ya lo ofrece en la pantalla de confirmacion: si el correo no
                // llega, el usuario puede pedir otro sin quedarse atrapado.
                await transaction.CommitAsync();

                try
                {
                    await _emailSender.SendEmailConfirmationCodeAsync(
                        nuevoUsuario, dto.Email, codigo);
                }
                catch (Exception emailEx)
                {
                    _logger.LogWarning(emailEx,
                        "No se pudo enviar el código de confirmación a {UserId}; " +
                        "el usuario puede pedirlo de nuevo",
                        nuevoUsuario.Id);
                }

                return Ok(new
                {
                    message = "Registro exitoso. Revisa tu correo para confirmar tu cuenta.",
                    email = dto.Email,
                    requiresEmailConfirmation = true
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error en registro de negocio");
                return StatusCode(500, new { message = "Error interno al procesar el registro" });
            }
        }

        // =====================================================
        // INICIO DE SESIÓN CON GOOGLE
        // =====================================================

        private const string GOOGLE_PROVIDER = "Google";

        /// <summary>
        /// Paso 1: valida el ID token de Google e inicia sesión.
        ///
        /// Tres caminos posibles:
        ///  - Ya existe un login de Google vinculado → entra.
        ///  - Existe un usuario con ese email → se vincula Google y entra.
        ///  - No existe → responde requiresRegistration para que el cliente pida
        ///    los datos del negocio y llame a google/register.
        /// </summary>
        /// <remarks>
        /// La identidad se toma SOLO del token verificado por GoogleTokenValidator,
        /// nunca de datos que mande la app. El vinculo se guarda en AspNetUserLogins
        /// (proveedor "Google" + "sub", el id estable del usuario en Google).
        /// </remarks>
        [HttpPost("google")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleAuthDto dto)
        {
            if (!_googleValidator.EstaConfigurado)
                return StatusCode(503, new { message = "El inicio de sesión con Google no está disponible." });

            var google = await _googleValidator.ValidarAsync(dto.IdToken);
            if (google == null)
                return Unauthorized(new { message = "No pudimos validar tu cuenta de Google. Intenta de nuevo." });

            // 1) Login de Google ya vinculado
            var user = await _userManager.FindByLoginAsync(GOOGLE_PROVIDER, google.Sub);

            // 2) Mismo email registrado con contraseña: se vincula automáticamente.
            //    Solo si Google confirma que el correo está verificado; de lo contrario
            //    alguien podría reclamar una cuenta ajena creando un Google con ese email.
            if (user == null && google.EmailVerificado)
            {
                user = await _userManager.FindByEmailAsync(google.Email);

                if (user != null)
                {
                    var link = await _userManager.AddLoginAsync(
                        user,
                        new UserLoginInfo(GOOGLE_PROVIDER, google.Sub, GOOGLE_PROVIDER));

                    if (!link.Succeeded)
                    {
                        _logger.LogError("No se pudo vincular Google al usuario {UserId}: {Errores}",
                            user.Id, string.Join(", ", link.Errors.Select(e => e.Description)));
                        return StatusCode(500, new { message = "No pudimos vincular tu cuenta de Google." });
                    }

                    // Entrar con Google prueba que el correo es suyo: si quedó a medias
                    // en la confirmación por email, se da por confirmado aquí.
                    if (!user.EmailConfirmed)
                    {
                        user.EmailConfirmed = true;
                        await _userManager.UpdateAsync(user);
                    }

                    _logger.LogInformation("Google vinculado al usuario existente {UserId}", user.Id);
                }
            }

            // 3) Usuario nuevo: falta el negocio, que Google no puede aportar.
            if (user == null)
            {
                return Ok(new
                {
                    requiresRegistration = true,
                    email = google.Email,
                    nombreCompleto = google.Nombre ?? ""
                });
            }

            if (await _userManager.IsLockedOutAsync(user))
                return Unauthorized(new { message = "Demasiados intentos fallidos. Intenta de nuevo en unos minutos." });

            return Ok(await BuildLoginResponseAsync(user));
        }

        /// <summary>
        /// Paso 2: crea el negocio y la cuenta para un usuario nuevo de Google.
        /// </summary>
        /// <remarks>
        /// Vuelve a validar el token de Google (no se fia del paso 1) y crea empresa,
        /// config (plan Gratis) y usuario, con EmailConfirmed = lo que diga Google
        /// (email_verified), y devuelve directamente el login.
        /// </remarks>
        [HttpPost("google/register")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> GoogleRegister([FromBody] GoogleRegisterDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (!_googleValidator.EstaConfigurado)
                return StatusCode(503, new { message = "El inicio de sesión con Google no está disponible." });

            var google = await _googleValidator.ValidarAsync(dto.IdToken);
            if (google == null)
                return Unauthorized(new { message = "No pudimos validar tu cuenta de Google. Intenta de nuevo." });

            if (await _userManager.FindByLoginAsync(GOOGLE_PROVIDER, google.Sub) != null ||
                await _userManager.FindByEmailAsync(google.Email) != null)
            {
                return BadRequest(new { message = "Esta cuenta ya está registrada. Inicia sesión." });
            }

            // Mismo IgnoreQueryFilters que en el registro normal: sin usuario autenticado
            // el filtro de tenant dejaría la comprobación de duplicados en cero filas.
            if (await _context.Empresas.IgnoreQueryFilters()
                    .AnyAsync(e => e.Nombre == dto.NombreBarberia))
                return BadRequest(new { message = "Ya existe un negocio con ese nombre" });

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var nuevaEmpresa = new Empresa
                {
                    Nombre = dto.NombreBarberia,
                    Telefono = dto.PhoneNumber?.Trim() ?? string.Empty,
                    Direccion = dto.Direccion?.Trim() ?? string.Empty,
                    Estado = EstadoSuscripcion.Activo,
                    FechaVencimiento = null,
                    FechaBloqueo = null
                };

                _context.Empresas.Add(nuevaEmpresa);
                await _context.SaveChangesAsync();

                _context.EmpresaConfigs.Add(
                    PlanDefaults.CrearConfig(nuevaEmpresa.Id, PlanEmpresa.Gratis));

                var nombreCompleto = !string.IsNullOrWhiteSpace(google.Nombre)
                    ? google.Nombre!
                    : google.Email;

                _context.Empleados.Add(new Empleado
                {
                    Nombre = nombreCompleto,
                    PorcentajeComision = 100,
                    Periodicidad = PeriodicidadPago.Quincenal,
                    EmpresaId = nuevaEmpresa.Id
                });
                await _context.SaveChangesAsync();

                var nuevoUsuario = new Usuario
                {
                    UserName = google.Email,
                    Email = google.Email,
                    NombreCompleto = nombreCompleto,
                    PhoneNumber = dto.PhoneNumber?.Trim(),
                    EmpresaId = nuevaEmpresa.Id,
                    // Google ya verificó el correo: no tiene sentido pedir un código.
                    // Esto además esquiva el ~30% de abandono en la confirmación por email.
                    EmailConfirmed = google.EmailVerificado
                };

                // Sin contraseña: la cuenta se usa solo con Google. Si más adelante
                // quiere una, puede pedirla por "olvidé mi contraseña".
                var result = await _userManager.CreateAsync(nuevoUsuario);

                if (!result.Succeeded)
                    throw new Exception("Error al crear usuario: " +
                        string.Join(", ", result.Errors.Select(e => e.Description)));

                var link = await _userManager.AddLoginAsync(
                    nuevoUsuario,
                    new UserLoginInfo(GOOGLE_PROVIDER, google.Sub, GOOGLE_PROVIDER));

                if (!link.Succeeded)
                    throw new Exception("Error al vincular Google: " +
                        string.Join(", ", link.Errors.Select(e => e.Description)));

                await _userManager.AddToRoleAsync(nuevoUsuario, "Admin");

                // El refresh token forma parte del alta: si no se puede construir
                // la sesión, se revierte también el usuario y el negocio.
                var auth = await BuildLoginResponseAsync(nuevoUsuario);
                await transaction.CommitAsync();

                // El email ocurre después del commit y es best-effort. Un fallo del
                // proveedor no debe responder 500 cuando la cuenta ya quedó creada.
                try
                {
                    await _emailSender.SendWelcomeEmailAsync(
                        nuevoUsuario, google.Email, nuevaEmpresa.Nombre);
                }
                catch (Exception emailEx)
                {
                    _logger.LogWarning(emailEx,
                        "No se pudo enviar bienvenida de Google al usuario {UserId}",
                        nuevoUsuario.Id);
                }

                return Ok(auth);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error en registro con Google");
                return StatusCode(500, new { message = "Error interno al procesar el registro" });
            }
        }

        // =====================================================
        // CONFIRMAR EMAIL
        // =====================================================

        /// <summary>
        /// Confirma el email del usuario con el código de 6 dígitos
        /// </summary>
        /// <remarks>Si el codigo es valido marca EmailConfirmed, envia la bienvenida y devuelve directamente el login (JWT) para no pedir la contrasena otra vez.</remarks>
        [HttpPost("confirm-email")]
        [EnableRateLimiting("codes")]
        public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);

            if (user == null)
                return BadRequest(new { message = "Usuario no encontrado" });

            if (user.EmailConfirmed)
                return BadRequest(new { message = "El email ya está confirmado" });

            // Validar código
            var codigoValido = await _codigoService.ValidarCodigoAsync(
                user.Id,
                dto.Codigo,
                TipoCodigoVerificacion.ConfirmacionEmail);

            if (!codigoValido)
                return BadRequest(new { message = "Código inválido o expirado" });

            // Confirmar email
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);

            // Cargar empresa para el email de bienvenida
            await _context.Entry(user).Reference(u => u.Empresa).LoadAsync();

            // Enviar email de bienvenida
            await _emailSender.SendWelcomeEmailAsync(
                user,
                user.Email!,
                user.Empresa?.Nombre ?? "Tu negocio");

            // Auto-login: el usuario probó ser dueño del correo con el código,
            // devolvemos el token para entrar sin re-loguearse (menos fricción).
            return Ok(await BuildLoginResponseAsync(user));
        }

        /// <summary>
        /// Reenvía el código de confirmación de email
        /// </summary>
        /// <remarks>Genera un codigo nuevo (el anterior deja de valer). Responde igual exista o no el email, para no revelar que correos estan registrados.</remarks>
        [HttpPost("resend-confirmation")]
        [EnableRateLimiting("codes")]
        public async Task<IActionResult> ResendConfirmationCode([FromBody] ResendCodeDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);

            if (user == null)
            {
                // No revelamos si el usuario existe o no
                return Ok(new { message = "Si el correo está registrado, recibirás un nuevo código." });
            }

            if (user.EmailConfirmed)
                return BadRequest(new { message = "El email ya está confirmado" });

            // Generar nuevo código
            var codigo = await _codigoService.GenerarCodigoAsync(
                user.Id,
                TipoCodigoVerificacion.ConfirmacionEmail);

            await _emailSender.SendEmailConfirmationCodeAsync(user, dto.Email, codigo);

            return Ok(new { message = "Si el correo está registrado, recibirás un nuevo código." });
        }

        // =====================================================
        // LOGIN
        // =====================================================

        /// <summary>
        /// Inicia sesión y retorna JWT token
        /// </summary>
        /// <remarks>
        /// Comprueba bloqueo por intentos fallidos (lockout: 5 fallos = 5 min),
        /// contrasena y email confirmado. Si todo va bien construye la respuesta con
        /// JWT + refresh token (BuildLoginResponseAsync).
        /// </remarks>
        [HttpPost("login")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Login([FromBody] LoginDto model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);

            if (user == null)
                return Unauthorized(new { message = "Credenciales inválidas" });

            // Verificar que el email esté confirmado
            if (!user.EmailConfirmed)
            {
                return Unauthorized(new
                {
                    message = "Debes confirmar tu email antes de iniciar sesión",
                    requiresEmailConfirmation = true,
                    email = model.Email
                });
            }

            // Bloqueo temporal tras varios intentos fallidos
            if (await _userManager.IsLockedOutAsync(user))
                return Unauthorized(new { message = "Demasiados intentos fallidos. Intenta de nuevo en unos minutos." });

            // Verificar contraseña
            if (!await _userManager.CheckPasswordAsync(user, model.Password))
            {
                await _userManager.AccessFailedAsync(user);
                return Unauthorized(new { message = "Credenciales inválidas" });
            }

            await _userManager.ResetAccessFailedCountAsync(user);

            // Cargar empresa
            await _context.Entry(user).Reference(u => u.Empresa).LoadAsync();

            // Verificar estado de la empresa
            /*if (user.Empresa != null && user.Empresa.Estado == BarberShop.Enum.EstadoSuscripcion.Suspendido)
            {
                return Unauthorized(new
                {
                    message = "Tu suscripción ha expirado. Contacta a soporte para reactivarla.",
                    subscriptionExpired = true
                });
            }
            */

            return Ok(await BuildLoginResponseAsync(user));
        }

        // =====================================================
        // REFRESH TOKEN
        // =====================================================

        /// <summary>
        /// Intercambia un refresh token válido por un nuevo JWT + refresh token (rotación).
        /// Evita que el usuario tenga que volver a iniciar sesión cada 12 horas.
        /// </summary>
        /// <remarks>Busca el refresh token por su hash; si esta activo lo revoca y emite un par nuevo. Un token robado solo sirve una vez.</remarks>
        [HttpPost("refresh")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto dto)
        {
            var hash = HashToken(dto.RefreshToken);

            var stored = await _context.RefreshTokens
                .Include(r => r.Usuario)
                .FirstOrDefaultAsync(r => r.TokenHash == hash);

            if (stored == null || !stored.EstaActivo || stored.Usuario == null)
                return Unauthorized(new { message = "Sesión expirada. Inicia sesión de nuevo." });

            // Rotación: el token usado se revoca y se emite uno nuevo
            stored.FechaRevocacion = DateTime.UtcNow;

            return Ok(await BuildLoginResponseAsync(stored.Usuario));
        }

        /// <summary>
        /// Revoca el refresh token al cerrar sesión.
        /// </summary>
        /// <remarks>El JWT sigue valido hasta que caduque (no se puede "des-firmar"); por eso su vida es corta y lo que se revoca es el refresh token.</remarks>
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshRequestDto dto)
        {
            var hash = HashToken(dto.RefreshToken);

            var stored = await _context.RefreshTokens
                .FirstOrDefaultAsync(r => r.TokenHash == hash && r.FechaRevocacion == null);

            if (stored != null)
            {
                stored.FechaRevocacion = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return Ok(new { message = "Sesión cerrada" });
        }

        // =====================================================
        // FORGOT PASSWORD
        // =====================================================

        /// <summary>
        /// Envía código de recuperación de contraseña por email
        /// </summary>
        /// <remarks>Responde lo mismo exista o no la cuenta (evita enumerar usuarios).</remarks>
        [HttpPost("forgot-password")]
        [EnableRateLimiting("codes")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);

            // Siempre retornamos el mismo mensaje por seguridad
            var successMessage = new { message = "Si el correo está registrado, recibirás instrucciones para restablecer tu contraseña." };

            if (user == null || !user.EmailConfirmed)
                return Ok(successMessage);

            // Generar código
            var codigo = await _codigoService.GenerarCodigoAsync(
                user.Id,
                TipoCodigoVerificacion.ResetPassword);

            await _emailSender.SendPasswordResetCodeAsync(user, model.Email, codigo);

            return Ok(successMessage);
        }

        // =====================================================
        // RESET PASSWORD
        // =====================================================

        /// <summary>
        /// Restablece la contraseña usando el código de verificación
        /// </summary>
        /// <remarks>Valida el codigo y usa el token interno de Identity (GeneratePasswordResetToken + ResetPassword) para cambiar la contrasena.</remarks>
        [HttpPost("reset-password")]
        [EnableRateLimiting("codes")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);

            if (user == null)
                return BadRequest(new { message = "Solicitud inválida" });

            // Validar código
            var codigoValido = await _codigoService.ValidarCodigoAsync(
                user.Id,
                model.Codigo,
                TipoCodigoVerificacion.ResetPassword);

            if (!codigoValido)
                return BadRequest(new { message = "Código inválido o expirado" });

            // Resetear contraseña usando Identity
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);

            if (!result.Succeeded)
                return BadRequest(new { message = "Error al cambiar contraseña", errors = result.Errors });

            return Ok(new { message = "Contraseña actualizada exitosamente. Ya puedes iniciar sesión." });
        }

        // =====================================================
        // REGISTRO DE EMPLEADO (Solo Admin)
        // =====================================================

        /// <summary>
        /// Registra un nuevo empleado/barbero (requiere rol Admin)
        /// </summary>
        /// <remarks>
        /// El Admin crea la cuenta de un barbero: usuario con rol "Barbero" ligado a
        /// un Empleado existente o uno nuevo de la misma empresa, y le envia el
        /// codigo de confirmacion.
        /// OJO (posible bug): cuando crea un Empleado nuevo NO llama a
        /// PlanValidationService.PuedeCrearEmpleadoAsync, asi que por esta via se
        /// puede superar MaxEmpleados del plan (EmpleadosController.Create si lo valida).
        /// </remarks>
        [Authorize(Roles = "Admin")]
        [HttpPost("register-employee")]
        public async Task<IActionResult> RegisterEmployee([FromBody] RegisterEmployeeDto model)
        {
            var empresaId = _userContext.EmpresaId;
            if (empresaId == null)
                return Unauthorized(new { message = "Empresa no detectada" });

            var userExists = await _userManager.FindByEmailAsync(model.Email);
            if (userExists != null)
                return BadRequest(new { message = "El usuario ya existe con ese email" });

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                int finalEmpleadoId;

                if (model.EmpleadoId.HasValue)
                {
                    var empleadoExistente = await _context.Empleados.FindAsync(model.EmpleadoId.Value);

                    if (empleadoExistente == null || empleadoExistente.EmpresaId != empresaId)
                        return BadRequest(new { message = "El EmpleadoId no existe o no pertenece a tu empresa" });

                    finalEmpleadoId = model.EmpleadoId.Value;
                }
                else
                {
                    if (model.PorcentajeComision == null)
                        return BadRequest(new { message = "Debes enviar PorcentajeComision y Periodicidad para crear el perfil de empleado" });

                    var nuevoEmpleado = new Empleado
                    {
                        Nombre = model.NombreCompleto,
                        PorcentajeComision = model.PorcentajeComision.Value,
                        Periodicidad = model.Periodicidad,
                        EmpresaId = empresaId.Value
                    };

                    _context.Empleados.Add(nuevoEmpleado);
                    await _context.SaveChangesAsync();

                    finalEmpleadoId = nuevoEmpleado.Id;
                }

                var user = new Usuario
                {
                    Email = model.Email,
                    SecurityStamp = Guid.NewGuid().ToString(),
                    UserName = model.Email,
                    NombreCompleto = model.NombreCompleto,
                    EmpresaId = empresaId.Value,
                    EmpleadoId = finalEmpleadoId,
                    EmailConfirmed = false
                };

                var result = await _userManager.CreateAsync(user, model.Password);

                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = "Error al crear usuario", errors = result.Errors });
                }

                await _userManager.AddToRoleAsync(user, "Barbero");

                // Enviar código de confirmación al empleado
                var codigo = await _codigoService.GenerarCodigoAsync(
                    user.Id,
                    TipoCodigoVerificacion.ConfirmacionEmail);

                await _emailSender.SendEmailConfirmationCodeAsync(user, model.Email, codigo);

                await transaction.CommitAsync();

                return Ok(new
                {
                    message = "Empleado registrado. Se envió un código de confirmación a su correo.",
                    empleadoId = finalEmpleadoId,
                    email = user.Email
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error al registrar empleado");
                // Sin ex.Message: los errores de Npgsql llevan nombres de tabla,
                // columna y restriccion, y este endpoint es publico. La causa ya
                // quedo en el log de arriba.
                return StatusCode(500, new { message = "No pudimos registrar al empleado. Inténtalo de nuevo." });
            }
        }

        // =====================================================
        // HELPERS
        // =====================================================

        private const int DIAS_REFRESH_TOKEN = 60;

        /// <summary>
        /// Construye la respuesta de login/refresh: JWT + refresh token nuevo.
        /// Guarda los cambios pendientes del contexto (incluida la rotación).
        /// </summary>
        private async Task<LoginResponseDto> BuildLoginResponseAsync(Usuario user)
        {
            await _context.Entry(user).Reference(u => u.Empresa).LoadAsync();

            var userRoles = await _userManager.GetRolesAsync(user);
            var authClaims = new List<Claim>
            {
                new(ClaimTypes.Name, user.Email!),
                new(ClaimTypes.NameIdentifier, user.Id),
                new("EmpresaId", user.EmpresaId.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };

            foreach (var role in userRoles)
            {
                authClaims.Add(new Claim(ClaimTypes.Role, role));
            }

            var token = CreateToken(authClaims);

            // Nuevo refresh token; limpiar los vencidos/revocados del usuario
            var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            _context.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                TokenHash = HashToken(rawRefreshToken),
                FechaExpiracion = DateTime.UtcNow.AddDays(DIAS_REFRESH_TOKEN)
            });

            var ahora = DateTime.UtcNow;
            var tokensViejos = await _context.RefreshTokens
                .Where(r => r.UserId == user.Id &&
                       (r.FechaRevocacion != null || r.FechaExpiracion < ahora))
                .ToListAsync();
            _context.RefreshTokens.RemoveRange(tokensViejos);

            await _context.SaveChangesAsync();

            return new LoginResponseDto
            {
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                RefreshToken = rawRefreshToken,
                Expiration = token.ValidTo,
                UserName = user.NombreCompleto ?? user.UserName ?? "",
                UserId = user.Id,
                UserEmail = user.Email!,
                Roles = userRoles,
                Empresa = user.Empresa?.Nombre ?? "Sin Empresa",
                EmpresaId = user.EmpresaId
            };
        }

        /// <summary>
        /// SHA-256 del refresh token en hexadecimal. En BD solo se guarda el hash: si
        /// alguien roba la base, no puede usar los tokens. Basta un hash rapido (y no
        /// uno lento tipo PBKDF2) porque el token ya es aleatorio de 64 bytes.
        /// </summary>
        private static string HashToken(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }

        /// <summary>
        /// Crea y firma el JWT con HMAC-SHA256 usando JWT:Secret (clave simetrica: la
        /// misma clave firma y verifica, ver AddJwtBearer en Program.cs). Emisor y
        /// audiencia deben coincidir con los que valida el servidor. Caduca en 12 h.
        /// </summary>
        private JwtSecurityToken CreateToken(List<Claim> authClaims)
        {
            var authSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_configuration["JWT:Secret"]!));

            return new JwtSecurityToken(
                issuer: _configuration["JWT:ValidIssuer"],
                audience: _configuration["JWT:ValidAudience"],
                // FIX #3: Use DateTime.UtcNow to avoid timezone issues in containerized environments
                expires: DateTime.UtcNow.AddHours(12),
                claims: authClaims,
                signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
            );
        }
    }
}
