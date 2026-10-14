using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Interfaces;
using BarberShop.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Controllers
{
    /// <summary>
    /// Configuracion de mensajeria por WhatsApp (nivel 1): codigo de pais, app
    /// destino y plantillas de texto que la app movil usa para abrir WhatsApp con
    /// el mensaje ya escrito. El backend solo guarda la configuracion; no envia nada.
    /// </summary>
    [ApiController]
    [Route("api/mensajeria")]
    [Authorize(Roles = "Admin")]
    public class MensajeriaController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IUserContext _userContext;

        /// <summary>Recibe BD y contexto del usuario por DI.</summary>
        public MensajeriaController(AppDbContext context, IUserContext userContext)
        {
            _context = context;
            _userContext = userContext;
        }

        // GET: api/mensajeria/config
        // Devuelve la config del negocio; si no existe, la crea con defaults.
        /// <summary>
        /// Devuelve la configuracion; si la empresa aun no tiene, la crea con los
        /// valores por defecto ("get or create"). ActionResult&lt;T&gt; permite devolver
        /// o bien un T (200) o bien un error como Unauthorized().
        /// </summary>
        [HttpGet("config")]
        public async Task<ActionResult<ConfiguracionMensajeriaDto>> GetConfig()
        {
            var empresaId = _userContext.EmpresaId;
            if (!empresaId.HasValue) return Unauthorized();

            // El tenant filter de BaseEntity ya limita a la empresa actual
            var config = await _context.ConfiguracionesMensajeria.FirstOrDefaultAsync();

            if (config == null)
            {
                config = new ConfiguracionMensajeria();  // EmpresaId lo setea SaveChanges
                _context.ConfiguracionesMensajeria.Add(config);
                await _context.SaveChangesAsync();
            }

            return Ok(ToDto(config));
        }

        // PUT: api/mensajeria/config
        /// <summary>
        /// Actualiza (o crea) la configuracion. ModelState.IsValid revisa los
        /// atributos de validacion del DTO ([Required], [StringLength]...).
        /// </summary>
        [HttpPut("config")]
        public async Task<ActionResult<ConfiguracionMensajeriaDto>> UpdateConfig(
            [FromBody] UpdateConfiguracionMensajeriaDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var empresaId = _userContext.EmpresaId;
            if (!empresaId.HasValue) return Unauthorized();

            var config = await _context.ConfiguracionesMensajeria.FirstOrDefaultAsync();
            if (config == null)
            {
                config = new ConfiguracionMensajeria();
                _context.ConfiguracionesMensajeria.Add(config);
            }

            config.CodigoPais = dto.CodigoPais.Trim().Replace("+", "");
            config.AppDestino = dto.AppDestino;
            config.PlantillaRecordatorio = dto.PlantillaRecordatorio;
            config.PlantillaDeudor = dto.PlantillaDeudor;
            config.PlantillaConfirmacion = dto.PlantillaConfirmacion;
            config.FechaActualizacion = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(ToDto(config));
        }

        /// <summary>
        /// Convierte la entidad en DTO. Nunca se devuelve la entidad de BD tal cual:
        /// el DTO decide exactamente que campos viajan al cliente.
        /// </summary>
        private static ConfiguracionMensajeriaDto ToDto(ConfiguracionMensajeria c) => new()
        {
            CodigoPais = c.CodigoPais,
            AppDestino = c.AppDestino,
            PlantillaRecordatorio = c.PlantillaRecordatorio,
            PlantillaDeudor = c.PlantillaDeudor,
            PlantillaConfirmacion = c.PlantillaConfirmacion,
        };
    }
}
