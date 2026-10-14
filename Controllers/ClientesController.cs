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
    /// CRUD de los clientes del negocio (la gente que se corta el pelo) mas dos
    /// consultas especiales: deudores y recordatorios de "ya le toca volver".
    /// [Authorize] sin roles: basta con estar logueado (Admin o Barbero).
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ClientesController : ControllerBase
    {
        private readonly AppDbContext _context;

        /// <summary>Recibe el DbContext por DI.</summary>
        public ClientesController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/Clientes
        /// <summary>
        /// Lista los clientes activos. Se proyecta con .Select directamente a DTO:
        /// EF genera un SELECT solo con esas columnas en vez de cargar la entidad
        /// entera. El Where(c => c.Activo) despues del Select tambien se traduce a SQL.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ClienteResponseDto>>> GetClientes()
        {
            // PROYECCIÓN: Convertimos de Entidad a DTO aquí mismo
            var clientes = await _context.Clientes

                .Select(c => new ClienteResponseDto
                {
                    Id = c.Id,
                    Nombre = c.Nombre,
                    Telefono = c.Telefono,
                    FechaUltimoCorte = c.FechaUltimoCorte,
                    Activo = c.Activo
                })
                .Where(c => c.Activo)
                .ToListAsync();

            return Ok(clientes);
        }

        // GET: api/Clientes/5
        /// <summary>Un cliente activo por id, proyectado a DTO; 404 si no existe o es de otra empresa.</summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<ClienteResponseDto>> GetCliente(int id)
        {
            // Buscamos proyectando directamente
            var cliente = await _context.Clientes
                .Where(c => c.Id == id && c.Activo)
                .Select(c => new ClienteResponseDto
                {
                    Id = c.Id,
                    Nombre = c.Nombre,
                    Telefono = c.Telefono,
                    FechaUltimoCorte = c.FechaUltimoCorte,
                    Activo = c.Activo
                })
                .FirstOrDefaultAsync();

            if (cliente == null)
                return NotFound();

            return Ok(cliente);
        }

        // POST: api/Clientes
        /// <summary>
        /// Crea un cliente. EmpresaId no viene del cliente HTTP: lo asigna
        /// AppDbContext.SaveChangesAsync a partir del JWT (asi nadie puede crear
        /// clientes en otra empresa). Devuelve 201 con la ubicacion del recurso.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<Cliente>> PostCliente(ClienteCreateDto dto)
        {
            // Validar modelo automáticamente (si usas [ApiController] atributo)
            // Si no usas [ApiController], puedes hacer validación manual:
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var cliente = new Cliente
            {
                Nombre = dto.Nombre,
                Telefono = dto.Telefono, // Puede ser null si no se envía
                FechaUltimoCorte = dto.FechaUltimoCorte
            };

            _context.Clientes.Add(cliente);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetCliente), new { id = cliente.Id }, cliente);
        }

        // PUT: api/Clientes/5
        /// <summary>Actualiza nombre, telefono y fecha del ultimo corte. 204 No Content si va bien.</summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> PutCliente(int id, ClienteUpdateDto dto)
        {
            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cliente == null)
                return NotFound();

            // Actualizamos solo los campos permitidos
            cliente.Nombre = dto.Nombre;
            cliente.Telefono = dto.Telefono;
            cliente.FechaUltimoCorte = dto.FechaUltimoCorte;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/Clientes/5 (Soft delete)
        /// <summary>
        /// Borrado logico: marca Activo = false en vez de DELETE, para no perder sus
        /// citas y movimientos de dinero asociados.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCliente(int id)
        {
            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cliente == null)
                return NotFound();

            // Soft delete
            cliente.Activo = false;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        /// <summary>
        /// Clientes con deuda: toma las citas pendientes de pago, trae los movimientos
        /// de cuenta de esas citas y calcula en memoria Cargos - Abonos por cliente.
        /// Devuelve solo los que deben, ordenados por la deuda mas antigua. Ojo: reusa
        /// el campo FechaUltimoCorte del DTO para enviar la fecha de la primera deuda.
        /// </summary>
        [HttpGet("deudores")]
        public async Task<ActionResult<IEnumerable<ClienteResponseDto>>> GetClientesDeudores()
        {
            var deudores = await _context.Citas
                .Where(c => c.Estado == EstadoCita.PagoPendiente || c.Estado == EstadoCita.Pendiente)
                .Select(c => new
                {
                    c.ClienteId,
                    c.Cliente.Nombre,
                    c.Cliente.Telefono,
                    c.Cliente.Activo,
                    CitaId = c.Id,
                    FechaCita = c.Fecha
                })
                .ToListAsync();

            if (!deudores.Any())
                return Ok(new List<ClienteResponseDto>());

            var citaIds = deudores.Select(d => d.CitaId).ToList();

            var movimientos = await _context.CuentasCliente
                .Where(cc => cc.CitaId.HasValue && citaIds.Contains(cc.CitaId.Value))
                .ToListAsync();

            var resultado = deudores
                .GroupBy(d => new
                {
                    d.ClienteId,
                    d.Nombre,
                    d.Telefono,
                    d.Activo
                })
                .Select(g =>
                {
                    var totalCargo = movimientos
                        .Where(m => m.ClienteId == g.Key.ClienteId && m.Tipo == TipoMovimiento.Cargo)
                        .Sum(m => m.Monto);

                    var totalAbono = movimientos
                        .Where(m => m.ClienteId == g.Key.ClienteId && m.Tipo == TipoMovimiento.Abono)
                        .Sum(m => m.Monto);

                    var deuda = totalCargo - totalAbono;

                    return new
                    {
                        g.Key.ClienteId,
                        g.Key.Nombre,
                        g.Key.Telefono,
                        g.Key.Activo,
                        Deuda = deuda,
                        FechaPrimeraDeuda = g.Min(x => x.FechaCita)
                    };
                })
                .Where(x => x.Deuda > 0)
                .OrderBy(x => x.FechaPrimeraDeuda)
                .Select(x => new ClienteResponseDto
                {
                    Id = x.ClienteId,
                    Nombre = x.Nombre,
                    Telefono = x.Telefono,
                    FechaUltimoCorte = x.FechaPrimeraDeuda,
                    Activo = x.Activo
                })
                .ToList();

            return Ok(resultado);
        }
        /// <summary>
        /// Calcula "al vuelo" (sin guardar nada) que clientes estan vencidos: para
        /// cada par cliente-servicio con intervalo, la ultima visita (GROUP BY + MAX en
        /// SQL); luego en memoria calcula dias de retraso y deja uno por cliente, el
        /// mas vencido. Compara con RecordatoriosController, que si persiste filas.
        /// </summary>
        [HttpGet("recordatorios")]
        public async Task<ActionResult<IEnumerable<ClienteRecordatorioDto>>> GetRecordatorios()
        {
            var hoy = DateTime.UtcNow.Date;

            // 1) Última visita por cliente-servicio
            var baseData = await _context.CitasServicios
                .Where(cs =>
                    cs.Servicio.IntervaloDias.HasValue &&
                    cs.Cita.Estado == EstadoCita.Cerrada &&
                    cs.Cita.Cliente.Activo)
                .Select(cs => new
                {
                    cs.Cita.ClienteId,
                    NombreCliente = cs.Cita.Cliente.Nombre,
                    cs.Cita.Cliente.Telefono,
                    NombreServicio = cs.Servicio.Nombre,
                    IntervaloDias = cs.Servicio.IntervaloDias!.Value,
                    Fecha = cs.Cita.Fecha
                })
                .GroupBy(x => new
                {
                    x.ClienteId,
                    x.NombreCliente,
                    x.Telefono,
                    x.NombreServicio,
                    x.IntervaloDias
                })
                .Select(g => new
                {
                    g.Key.ClienteId,
                    g.Key.NombreCliente,
                    g.Key.Telefono,
                    Servicio = g.Key.NombreServicio,
                    g.Key.IntervaloDias,
                    UltimaVisita = g.Max(x => x.Fecha)
                })
                .ToListAsync();

            // 2) Calcular vencidos
            var vencidos = baseData
                .Select(x =>
                {
                    var diasDesde = (hoy - x.UltimaVisita.Date).Days;
                    var diasVencido = diasDesde - x.IntervaloDias;

                    return new ClienteRecordatorioDto
                    {
                        ClienteId = x.ClienteId,
                        Nombre = x.NombreCliente,
                        Telefono = x.Telefono,
                        Servicio = x.Servicio,
                        IntervaloDias = x.IntervaloDias,
                        UltimaVisita = x.UltimaVisita,
                        DiasDesdeUltimaVisita = diasDesde,
                        DiasVencido = diasVencido
                    };
                })
                .Where(x => x.DiasVencido >= 0);

            // 3) SOLO UNO POR CLIENTE → el más vencido
            var resultado = vencidos
                .GroupBy(x => x.ClienteId)
                .Select(g => g
                    .OrderByDescending(x => x.DiasVencido)
                    .First())
                .OrderByDescending(x => x.DiasVencido)
                .ToList();

            return Ok(resultado);
        }


    }

}
