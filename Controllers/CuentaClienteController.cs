using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Controllers
{
    /// <summary>
    /// "Libreta de fiado" de cada cliente: movimientos de Cargo (el cliente debe)
    /// y Abono (el cliente paga). El saldo no se guarda en ningun lado, se calcula
    /// sumando movimientos (patron "libro contable": solo se agregan filas, nunca
    /// se edita un total, asi queda historial de todo).
    ///
    /// [ApiController] activa validacion automatica del modelo y binding desde el
    /// body; [Route] fija la URL base; [Authorize(Roles=...)] exige JWT con uno de
    /// esos roles (si no, 401/403 sin llegar a ejecutar el metodo).
    /// </summary>
    [ApiController]
    [Route("api/cuentas-cliente")]
    [Authorize(Roles = "Admin,Barbero,SuperAdmin")]
    public class CuentaClienteController : ControllerBase
    {
        private readonly AppDbContext _context;

        /// <summary>El framework crea un controlador NUEVO por peticion e inyecta el DbContext.</summary>
        public CuentaClienteController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // GET: api/cuentas-cliente/cliente/{clienteId}
        // Historial completo
        // =========================================================
        /// <summary>
        /// Todos los movimientos de un cliente en orden cronologico. {clienteId} en la
        /// ruta se "bindea" automaticamente al parametro del mismo nombre.
        /// </summary>
        [HttpGet("cliente/{clienteId}")]
        public async Task<IActionResult> GetMovimientos(int clienteId)
        {
            var movimientos = await _context.CuentasCliente
                .Where(c => c.ClienteId == clienteId)
                .OrderBy(c => c.FechaCreacion)
                .ToListAsync();

            return Ok(movimientos);
        }

        // =========================================================
        // GET: api/cuentas-cliente/cliente/{clienteId}/saldo
        // =========================================================
        /// <summary>
        /// Saldo del cliente: suma abonos y resta cargos en memoria. Ojo con el signo:
        /// aqui un saldo NEGATIVO significa que el cliente debe (al reves que
        /// ConsultasClientela, donde la deuda es Cargos - Abonos y sale positiva).
        /// </summary>
        [HttpGet("cliente/{clienteId}/saldo")]
        public async Task<IActionResult> GetSaldo(int clienteId)
        {
            var movimientos = await _context.CuentasCliente
                .Where(c => c.ClienteId == clienteId)
                .ToListAsync();

            var saldo = movimientos.Sum(m =>
                m.Tipo == TipoMovimiento.Abono ? m.Monto : -m.Monto);

            return Ok(new
            {
                ClienteId = clienteId,
                Saldo = saldo
            });
        }

        // =========================================================
        // POST: api/cuentas-cliente/abono
        // =========================================================
        /// <summary>
        /// Registra un pago del cliente. Valida que cliente y cita pertenezcan a la
        /// empresa (gracias al filtro de tenant, AnyAsync solo "ve" los de la empresa).
        /// [FromBody] indica que el DTO se deserializa del JSON del cuerpo.
        /// </summary>
        [HttpPost("abono")]
        public async Task<IActionResult> CrearAbono([FromBody] CrearAbonoDto dto)
        {
            if (dto.Monto <= 0)
                return BadRequest("El monto debe ser mayor a 0.");

            // ClienteId y CitaId venian del cuerpo y se persistian sin comprobar
            // nada: permitia inyectar movimientos de dinero contra un cliente de
            // otra empresa. Las consultas van por el filtro global de tenant.
            if (!await _context.Clientes.AnyAsync(c => c.Id == dto.ClienteId))
                return BadRequest("Cliente inválido o fuera de la empresa.");

            if (dto.CitaId.HasValue &&
                !await _context.Citas.AnyAsync(c => c.Id == dto.CitaId.Value))
                return BadRequest("Cita inválida o fuera de la empresa.");

            var abono = new CuentaCliente
            {
                ClienteId = dto.ClienteId,
                CitaId = dto.CitaId,
                Monto = dto.Monto,
                Tipo = TipoMovimiento.Abono,
                Observaciones = dto.Observaciones
            };

            _context.CuentasCliente.Add(abono);
            await _context.SaveChangesAsync();

            return Ok(abono);
        }

        // =========================================================
        // POST: api/cuentas-cliente/cargo
        // =========================================================
        /// <summary>Registra una deuda del cliente (un servicio "a deber"). Mismas validaciones que CrearAbono.</summary>
        [HttpPost("cargo")]
        public async Task<IActionResult> CrearCargo([FromBody] CrearCargoDto dto)
        {
            if (dto.Monto <= 0)
                return BadRequest("El monto debe ser mayor a 0.");

            // ClienteId y CitaId venian del cuerpo y se persistian sin comprobar
            // nada: permitia inyectar movimientos de dinero contra un cliente de
            // otra empresa. Las consultas van por el filtro global de tenant.
            if (!await _context.Clientes.AnyAsync(c => c.Id == dto.ClienteId))
                return BadRequest("Cliente inválido o fuera de la empresa.");

            if (dto.CitaId.HasValue &&
                !await _context.Citas.AnyAsync(c => c.Id == dto.CitaId.Value))
                return BadRequest("Cita inválida o fuera de la empresa.");

            var cargo = new CuentaCliente
            {
                ClienteId = dto.ClienteId,
                CitaId = dto.CitaId,
                Monto = dto.Monto,
                Tipo = TipoMovimiento.Cargo,
                Observaciones = dto.Observaciones
            };

            _context.CuentasCliente.Add(cargo);
            await _context.SaveChangesAsync();

            return Ok(cargo);
        }
    }
}
