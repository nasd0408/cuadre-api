using BarberShop.Data;
using BarberShop.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace BarberShop.Services
{
    /// <summary>
    /// Codigos de 6 digitos de un solo uso (confirmar email y recuperar
    /// contrasena). Se guardan en la tabla CodigosVerificacion.
    /// </summary>
    public interface ICodigoVerificacionService
    {
        /// <summary>Crea un codigo nuevo (invalidando los anteriores) y lo devuelve para enviarlo por correo.</summary>
        Task<string> GenerarCodigoAsync(string userId, TipoCodigoVerificacion tipo);
        /// <summary>true si el codigo es el vigente del usuario; lo marca como usado.</summary>
        Task<bool> ValidarCodigoAsync(string userId, string codigo, TipoCodigoVerificacion tipo);
        /// <summary>Marca como usados todos los codigos pendientes de ese tipo.</summary>
        Task InvalidarCodigosAnterioresAsync(string userId, TipoCodigoVerificacion tipo);
    }

    /// <summary>Implementacion con EF Core de ICodigoVerificacionService.</summary>
    public class CodigoVerificacionService : ICodigoVerificacionService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<CodigoVerificacionService> _logger;

        // Tiempo de expiración en minutos
        private const int MINUTOS_EXPIRACION_EMAIL = 60;      // 1 hora para confirmar email
        private const int MINUTOS_EXPIRACION_PASSWORD = 15;   // 15 min para reset password

        // Máximo de intentos de validación antes de invalidar el código
        private const int MAX_INTENTOS = 5;

        /// <summary>Recibe el DbContext y el logger por inyeccion de dependencias.</summary>
        public CodigoVerificacionService(AppDbContext context, ILogger<CodigoVerificacionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Genera un codigo aleatorio de 6 digitos. Se usa RandomNumberGenerator
        /// (criptografico) y no Random, porque Random es predecible si se conoce
        /// la semilla y un atacante podria adivinar codigos.
        /// </summary>
        public async Task<string> GenerarCodigoAsync(string userId, TipoCodigoVerificacion tipo)
        {
            // Invalidar códigos anteriores del mismo tipo
            await InvalidarCodigosAnterioresAsync(userId, tipo);

            // Generar código de 6 dígitos con RNG criptográfico
            var codigo = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

            var minutosExpiracion = tipo == TipoCodigoVerificacion.ConfirmacionEmail
                ? MINUTOS_EXPIRACION_EMAIL
                : MINUTOS_EXPIRACION_PASSWORD;

            var codigoVerificacion = new CodigoVerificacion
            {
                UserId = userId,
                Codigo = codigo,
                Tipo = tipo,
                FechaCreacion = DateTime.UtcNow,
                FechaExpiracion = DateTime.UtcNow.AddMinutes(minutosExpiracion),
                Usado = false
            };

            _context.CodigosVerificacion.Add(codigoVerificacion);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Código de verificación generado para usuario {UserId}, tipo {Tipo}, expira en {Minutos} minutos",
                userId, tipo, minutosExpiracion);

            return codigo;
        }

        /// <summary>
        /// Busca el codigo vigente (no usado, no expirado, el mas reciente) y lo
        /// compara. Cada fallo suma un intento; al 5o fallo el codigo se quema,
        /// asi no se pueden probar las 900.000 combinaciones por fuerza bruta.
        /// </summary>
        public async Task<bool> ValidarCodigoAsync(string userId, string codigo, TipoCodigoVerificacion tipo)
        {
            // Buscar el código vigente del usuario y comparar en memoria,
            // para poder contar intentos fallidos contra el código real.
            var codigoVigente = await _context.CodigosVerificacion
                .Where(c => c.UserId == userId
                         && c.Tipo == tipo
                         && !c.Usado
                         && c.FechaExpiracion > DateTime.UtcNow)
                .OrderByDescending(c => c.FechaCreacion)
                .FirstOrDefaultAsync();

            if (codigoVigente == null || codigoVigente.Codigo != codigo)
            {
                if (codigoVigente != null)
                {
                    codigoVigente.IntentosFallidos++;
                    if (codigoVigente.IntentosFallidos >= MAX_INTENTOS)
                    {
                        codigoVigente.Usado = true;
                        _logger.LogWarning(
                            "Código invalidado por exceso de intentos para usuario {UserId}, tipo {Tipo}",
                            userId, tipo);
                    }
                    await _context.SaveChangesAsync();
                }

                _logger.LogWarning(
                    "Código inválido o expirado para usuario {UserId}, tipo {Tipo}",
                    userId, tipo);
                return false;
            }

            var codigoVerificacion = codigoVigente;

            // Marcar como usado
            codigoVerificacion.Usado = true;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Código validado exitosamente para usuario {UserId}, tipo {Tipo}",
                userId, tipo);

            return true;
        }

        /// <summary>
        /// Carga los codigos pendientes, los marca como usados y guarda. Asi solo
        /// el ultimo codigo enviado por correo es valido.
        /// </summary>
        public async Task InvalidarCodigosAnterioresAsync(string userId, TipoCodigoVerificacion tipo)
        {
            var codigosAnteriores = await _context.CodigosVerificacion
                .Where(c => c.UserId == userId && c.Tipo == tipo && !c.Usado)
                .ToListAsync();

            foreach (var codigo in codigosAnteriores)
            {
                codigo.Usado = true;
            }

            if (codigosAnteriores.Any())
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation(
                    "Invalidados {Count} códigos anteriores para usuario {UserId}, tipo {Tipo}",
                    codigosAnteriores.Count, userId, tipo);
            }
        }
    }
}