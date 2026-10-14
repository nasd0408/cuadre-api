using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Controllers
{
    /// <summary>
    /// Corazon de la app: las citas (servicios prestados o agendados) y su
    /// maquina de estados. Cada transicion registra movimientos de dinero en
    /// CuentasCliente para que el saldo del cliente cuadre siempre:
    ///
    ///   Pendiente (agendada) --marcar-asistida--> PagoPendiente (+Cargo)
    ///   PagoPendiente --cobrar--------------------> Cerrada (+Abono)
    ///   Pendiente/PagoPendiente --marcar-pagada---> Pagada (+Cargo si faltaba, +Abono)
    ///   Pagada --cerrar---------------------------> Cerrada
    ///   Cerrada --reabrir-------------------------> Pagada (+Cargo de correccion)
    ///   cualquiera salvo Cerrada --DELETE---------> Cancelada
    ///
    /// Atajos: POST pagada (se crea ya cobrada y cerrada) y POST pendiente (se
    /// crea atendida pero sin pagar, "a deber").
    /// </summary>
    [ApiController]
    [Route("api/citas")]
    [Authorize(Roles = "Admin,Barbero,SuperAdmin")]
    public class CitasController : ControllerBase
    {
        private const string MensajeSolapamiento =
            "El empleado ya tiene una cita que coincide con ese horario.";
        private readonly AppDbContext _context;

        /// <summary>Recibe el DbContext por DI.</summary>
        public CitasController(AppDbContext context)
        {
            _context = context;
        }

        // =========================================================
        // GET: api/citas
        // Lista paginada con filtros
        // =========================================================
        /// <summary>
        /// Lista paginada con filtros opcionales (empleado, cliente, estado, rango de
        /// fechas) y orden. Patron "consulta dinamica": se parte de un IQueryable y se
        /// le van encadenando Where solo si el filtro viene; nada toca la BD hasta
        /// CountAsync/ToListAsync. [FromQuery] con un DTO bindea varios parametros de
        /// la query string a sus propiedades.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<CitasPagedResponseDto>> Get([FromQuery] CitasFiltrosDto filtros)
        {
            var query = _context.Citas
                .Include(c => c.Cliente)
                .Include(c => c.Empleado)
                .Include(c => c.CitaServicios)
                    .ThenInclude(cs => cs.Servicio)
                .AsQueryable();

            if (filtros.EmpleadoId.HasValue)
                query = query.Where(c => c.EmpleadoId == filtros.EmpleadoId);

            if (filtros.ClienteId.HasValue)
                query = query.Where(c => c.ClienteId == filtros.ClienteId);

            if (filtros.Estado.HasValue)
                query = query.Where(c => (int)c.Estado == filtros.Estado);

            if (filtros.FechaDesde.HasValue)
                query = query.Where(c => c.Fecha >= filtros.FechaDesde.Value);

            if (filtros.FechaHasta.HasValue)
                query = query.Where(c => c.Fecha <= filtros.FechaHasta.Value.AddDays(1));

            var totalItems = await query.CountAsync();

            query = filtros.OrderBy?.ToLower() switch
            {
                "fecha_asc" => query.OrderBy(c => c.Fecha),
                "total_desc" => query.OrderByDescending(c => c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad)),
                _ => query.OrderByDescending(c => c.Fecha)
            };

            var pageSize = Math.Clamp(filtros.PageSize, 1, 100);
            var page = Math.Max(filtros.Page, 1);
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            var citas = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = citas.Select(c => MapToCitaResponse(c)).ToList();

            return Ok(new CitasPagedResponseDto
            {
                Items = items,
                TotalItems = totalItems,
                TotalPages = totalPages,
                CurrentPage = page,
                PageSize = pageSize
            });
        }

        // =========================================================
        // GET: api/citas/hoy
        // =========================================================
        /// <summary>
        /// Citas del dia "hoy" segun la zona horaria del telefono: la app manda su
        /// fecha local y su desfase en minutos, y se convierte a un rango UTC
        /// [hoy, manana). Las fechas se guardan en UTC en la BD.
        /// </summary>
        [HttpGet("hoy")]
        public async Task<ActionResult<List<CitaResponseDto>>> GetHoy(
            [FromQuery] int? empleadoId,
            [FromQuery] string? fecha = null,
            [FromQuery] int? tzOffsetMinutes = null)
        {
            // "Hoy" según el dispositivo del usuario (ver DashboardController).
            // Sin parámetros (app vieja) usa el día UTC del servidor.
            var offset = tzOffsetMinutes ?? 0;
            var hoyLocal = DateTime.TryParse(fecha, out var fechaParsed)
                ? fechaParsed.Date
                : DateTime.UtcNow.AddMinutes(-offset).Date;

            var hoy = DateTime.SpecifyKind(hoyLocal.AddMinutes(offset), DateTimeKind.Unspecified);
            var manana = hoy.AddDays(1);

            var query = _context.Citas
                .Include(c => c.Cliente)
                .Include(c => c.Empleado)
                .Include(c => c.CitaServicios)
                    .ThenInclude(cs => cs.Servicio)
                .Where(c => c.Fecha >= hoy && c.Fecha < manana);

            if (empleadoId.HasValue)
                query = query.Where(c => c.EmpleadoId == empleadoId);

            var citas = await query.OrderByDescending(c => c.Fecha).ToListAsync();

            return Ok(citas.Select(c => MapToCitaResponse(c)).ToList());
        }

        // =========================================================
        // GET: api/citas/pendientes
        // =========================================================
        /// <summary>Citas atendidas pero sin pagar (estado PagoPendiente), mas antiguas primero.</summary>
        [HttpGet("pendientes")]
        public async Task<ActionResult<List<CitaResponseDto>>> GetPendientes()
        {
            var citas = await _context.Citas
                .Include(c => c.Cliente)
                .Include(c => c.Empleado)
                .Include(c => c.CitaServicios)
                    .ThenInclude(cs => cs.Servicio)
                .Where(c => c.Estado == EstadoCita.PagoPendiente)
                .OrderBy(c => c.Fecha)
                .ToListAsync();

            return Ok(citas.Select(c => MapToCitaResponse(c)).ToList());
        }

        // =========================================================
        // GET: api/citas/{id}
        // =========================================================
        /// <summary>
        /// Una cita con cliente, empleado y servicios. Include/ThenInclude cargan las
        /// relaciones (JOINs); sin ellos las propiedades de navegacion vendrian null.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<CitaResponseDto>> GetById(int id)
        {
            var cita = await _context.Citas
                .Include(c => c.Cliente)
                .Include(c => c.Empleado)
                .Include(c => c.CitaServicios)
                    .ThenInclude(cs => cs.Servicio)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cita == null)
                return NotFound();

            return Ok(MapToCitaResponse(cita));
        }

        // =========================================================
        // GET: api/citas/{id}/pagos
        // Movimientos de cuenta asociados a esta cita
        // =========================================================
        /// <summary>Movimientos de dinero (cargos y abonos) asociados a una cita.</summary>
        [HttpGet("{id}/pagos")]
        public async Task<ActionResult<List<CuentaCliente>>> GetPagos(int id)
        {
            var cita = await _context.Citas.FindAsync(id);
            if (cita == null)
                return NotFound();

            var pagos = await _context.CuentasCliente
                .Where(cc => cc.CitaId == id)
                .OrderBy(cc => cc.FechaCreacion)
                .ToListAsync();

            return Ok(pagos);
        }

        // =========================================================
        // POST: api/citas/pagada
        // FLUJO FELIZ (90%)
        // =========================================================
        /// <summary>
        /// Flujo principal: el cliente se corto y pago en el momento. Crea la cita,
        /// su Cargo y su Abono, la deja Cerrada y actualiza la fecha de ultimo corte.
        /// Todo dentro de una TRANSACCION: o se guardan todas las escrituras o
        /// ninguna (si algo falla, el "using" hace rollback al salir sin Commit).
        /// </summary>
        [HttpPost("pagada")]
        public async Task<IActionResult> CreatePagada([FromBody] CreateCitaPagadaDto dto)
        {
            // Transaccion porque este flujo hace TRES escrituras separadas: la cita
            // (hace falta su Id para las claves foraneas), el Cargo, y el Abono con
            // el cierre. Sin ella, un fallo en la ultima dejaba la cita marcada como
            // pagada, con su Cargo y sin Abono: el cliente aparecia debiendo dinero
            // que acababa de pagar, y en la pantalla de deudores.
            using var transaccion = await _context.Database.BeginTransactionAsync();

            var cita = await CrearCitaInterna(dto, EstadoCita.Pagada);

            var total = cita.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad);

            _context.CuentasCliente.Add(new CuentaCliente
            {
                ClienteId = cita.ClienteId,
                CitaId = cita.Id,
                Monto = total,
                Tipo = TipoMovimiento.Abono,
                Observaciones = dto.ObservacionesPago ?? "Pago inmediato"
            });

            cita.Estado = EstadoCita.Cerrada;

            var cliente = await _context.Clientes.FindAsync(cita.ClienteId);
            if (cliente != null)
                cliente.FechaUltimoCorte = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await transaccion.CommitAsync();

            var citaCompleta = await _context.Citas
                .Include(c => c.Cliente)
                .Include(c => c.Empleado)
                .Include(c => c.CitaServicios)
                    .ThenInclude(cs => cs.Servicio)
                .FirstAsync(c => c.Id == cita.Id);

            return Ok(MapToCitaResponse(citaCompleta));
        }

        // =========================================================
        // POST: api/citas/pendiente
        // =========================================================
        /// <summary>
        /// El cliente se atendio pero queda debiendo: crea la cita en PagoPendiente
        /// con su Cargo (dentro de una transaccion).
        /// </summary>
        [HttpPost("pendiente")]
        public async Task<IActionResult> CreatePendiente([FromBody] CreateCitaBaseDto dto)
        {
            // CrearCitaInterna guarda dos veces (la cita, y luego su Cargo y los
            // recordatorios). Que la cita quede sin su Cargo es una deuda que nunca
            // se cobra.
            using var transaccion = await _context.Database.BeginTransactionAsync();

            var cita = await CrearCitaInterna(dto, EstadoCita.PagoPendiente);
            await _context.SaveChangesAsync();
            await transaccion.CommitAsync();

            var citaCompleta = await _context.Citas
                .Include(c => c.Cliente)
                .Include(c => c.Empleado)
                .Include(c => c.CitaServicios)
                    .ThenInclude(cs => cs.Servicio)
                .FirstAsync(c => c.Id == cita.Id);

            return Ok(MapToCitaResponse(citaCompleta));
        }

        // =========================================================
        // POST: api/citas/agendar
        // =========================================================
        /// <summary>
        /// Agenda una cita futura (estado Pendiente, sin movimientos de dinero).
        /// Rechaza fechas pasadas y devuelve 409 Conflict si el empleado ya tiene
        /// otra cita que se solapa en ese horario.
        /// </summary>
        [HttpPost("agendar")]
        public async Task<IActionResult> AgendarCita([FromBody] CreateCitaBaseDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (dto.Fecha <= DateTime.UtcNow)
                return BadRequest("La fecha debe ser futura para agendar una cita.");

            await using var transaccion = await TransaccionAgenda.Abrir(_context);
            await ValidarPertenenciaAsync(dto.ClienteId, dto.EmpleadoId);
            if (!await _context.Empleados.AnyAsync(e => e.Id == dto.EmpleadoId && e.EmpresaId == _context.CurrentEmpresaId))
                return NotFound();

            if (!await DentroDelHorario(dto.EmpleadoId, dto.Fecha, dto.Servicios))
                return Conflict("La cita queda fuera del horario laboral o coincide con una ausencia del profesional.");

            if (await HaySolapamientoAsync(
                    dto.EmpleadoId,
                    dto.Fecha,
                    dto.Servicios))
                return Conflict(MensajeSolapamiento);

            var cita = await CrearCitaInterna(dto, EstadoCita.Pendiente);

            await _context.SaveChangesAsync();
            await transaccion.CommitAsync();

            var citaCompleta = await _context.Citas
                .Include(c => c.Cliente)
                .Include(c => c.Empleado)
                .Include(c => c.CitaServicios)
                    .ThenInclude(cs => cs.Servicio)
                .FirstAsync(c => c.Id == cita.Id);

            return Ok(MapToCitaResponse(citaCompleta));
        }

        // =========================================================
        // POST: api/citas/{id}/marcar-asistida
        // =========================================================
        /// <summary>El cliente de una cita agendada vino: pasa a PagoPendiente y genera el Cargo.</summary>
        [HttpPost("{id}/marcar-asistida")]
        public async Task<IActionResult> MarcarAsistida(int id)
        {
            var cita = await _context.Citas
                .Include(c => c.Cliente)
                .Include(c => c.Empleado)
                .Include(c => c.CitaServicios)
                    .ThenInclude(cs => cs.Servicio)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cita == null)
                return NotFound();

            if (cita.Estado != EstadoCita.Pendiente)
                return BadRequest("Solo se pueden marcar como asistida las citas pendientes.");

            var total = cita.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad);

            _context.CuentasCliente.Add(new CuentaCliente
            {
                ClienteId = cita.ClienteId,
                CitaId = cita.Id,
                Tipo = TipoMovimiento.Cargo,
                Monto = total,
                Observaciones = $"Cita #{cita.Id}"
            });

            cita.Estado = EstadoCita.PagoPendiente;

            await _context.SaveChangesAsync();

            return Ok(MapToCitaResponse(cita));
        }

        // =========================================================
        // POST: api/citas/{id}/marcar-pagada
        // =========================================================
        /// <summary>
        /// Marca como pagada una cita pendiente o con pago pendiente: agrega el Cargo
        /// si aun no existia (cita agendada) y el Abono del pago. Queda en Pagada.
        /// </summary>
        [HttpPost("{id}/marcar-pagada")]
        public async Task<IActionResult> MarcarPagada(int id, [FromBody] CobrarCitaDto? dto)
        {
            var cita = await _context.Citas
                .Include(c => c.Cliente)
                .Include(c => c.Empleado)
                .Include(c => c.CitaServicios)
                    .ThenInclude(cs => cs.Servicio)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cita == null)
                return NotFound();

            if (cita.Estado != EstadoCita.Pendiente && cita.Estado != EstadoCita.PagoPendiente)
                return BadRequest("Solo se pueden marcar como pagada las citas pendientes o con pago pendiente.");

            var total = cita.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad);

            if (cita.Estado == EstadoCita.Pendiente)
            {
                _context.CuentasCliente.Add(new CuentaCliente
                {
                    ClienteId = cita.ClienteId,
                    CitaId = cita.Id,
                    Tipo = TipoMovimiento.Cargo,
                    Monto = total,
                    Observaciones = $"Cita #{cita.Id}"
                });
            }

            _context.CuentasCliente.Add(new CuentaCliente
            {
                ClienteId = cita.ClienteId,
                CitaId = cita.Id,
                Monto = total,
                Tipo = TipoMovimiento.Abono,
                Observaciones = dto?.Observaciones ?? $"Pago de cita #{cita.Id}"
            });

            cita.Estado = EstadoCita.Pagada;

            await _context.SaveChangesAsync();

            return Ok(MapToCitaResponse(cita));
        }

        // =========================================================
        // POST: api/citas/{id}/cerrar
        // =========================================================
        /// <summary>Cierra una cita Pagada (queda definitiva y cuenta para nomina) y actualiza el ultimo corte del cliente.</summary>
        [HttpPost("{id}/cerrar")]
        public async Task<IActionResult> CerrarCita(int id)
        {
            var cita = await _context.Citas
                .Include(c => c.Cliente)
                .Include(c => c.Empleado)
                .Include(c => c.CitaServicios)
                    .ThenInclude(cs => cs.Servicio)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cita == null)
                return NotFound();

            if (cita.Estado != EstadoCita.Pagada)
                return BadRequest("Solo se pueden cerrar las citas pagadas.");

            cita.Estado = EstadoCita.Cerrada;

            var cliente = await _context.Clientes.FindAsync(cita.ClienteId);
            if (cliente != null)
                cliente.FechaUltimoCorte = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(MapToCitaResponse(cita));
        }

        // =========================================================
        // POST: api/citas/{id}/reabrir
        // Cerrada → Pagada (para correcciones)
        // =========================================================
        /// <summary>
        /// Deshace el cierre para corregir algo: Cerrada -> Pagada con un Cargo de
        /// reapertura. Bloqueado si la comision ya se pago al empleado, porque
        /// cambiaria una nomina ya liquidada.
        /// </summary>
        [HttpPost("{id}/reabrir")]
        public async Task<IActionResult> ReabrirCita(int id, [FromBody] ReabrirCitaDto dto)
        {
            var cita = await _context.Citas
                .Include(c => c.Cliente)
                .Include(c => c.Empleado)
                .Include(c => c.CitaServicios)
                    .ThenInclude(cs => cs.Servicio)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cita == null)
                return NotFound();

            if (cita.Estado != EstadoCita.Cerrada)
                return BadRequest("Solo se pueden reabrir citas cerradas.");

            if (cita.PagadoAlEmpleado)
                return BadRequest("No se puede reabrir una cita cuya comisión ya fue pagada al empleado.");

            var total = cita.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad);

            cita.Estado = EstadoCita.Pagada;

            // Registrar cargo por reapertura (el cliente vuelve a deber)
            _context.CuentasCliente.Add(new CuentaCliente
            {
                ClienteId = cita.ClienteId,
                CitaId = cita.Id,
                Tipo = TipoMovimiento.Cargo,
                Monto = total,
                Observaciones = $"[Reapertura] {dto.Motivo ?? "Corrección manual"}"
            });

            await _context.SaveChangesAsync();

            return Ok(MapToCitaResponse(cita));
        }

        // =========================================================
        // POST: api/citas/{id}/cobrar
        // =========================================================
        /// <summary>Cobra una cita en PagoPendiente: registra el Abono y la cierra.</summary>
        [HttpPost("{id}/cobrar")]
        public async Task<IActionResult> Cobrar(int id, [FromBody] CobrarCitaDto? dto)
        {
            var cita = await _context.Citas
                .Include(c => c.Cliente)
                .Include(c => c.Empleado)
                .Include(c => c.CitaServicios)
                    .ThenInclude(cs => cs.Servicio)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cita == null)
                return NotFound();

            if (cita.Estado != EstadoCita.PagoPendiente)
                return BadRequest("Esta cita no está pendiente de pago.");

            var total = cita.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad);

            _context.CuentasCliente.Add(new CuentaCliente
            {
                ClienteId = cita.ClienteId,
                CitaId = cita.Id,
                Monto = total,
                Tipo = TipoMovimiento.Abono,
                Observaciones = dto?.Observaciones ?? $"Pago de cita #{cita.Id}"
            });

            cita.Estado = EstadoCita.Cerrada;

            var cliente = await _context.Clientes.FindAsync(cita.ClienteId);
            if (cliente != null)
                cliente.FechaUltimoCorte = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(MapToCitaResponse(cita));
        }

        // =========================================================
        // PUT: api/citas/{id}
        // Ahora permite editar citas cerradas (se excluyen solo canceladas)
        // =========================================================
        /// <summary>
        /// Edita cliente, empleado, fecha y servicios de una cita (no canceladas).
        /// Los precios se toman de la BD, nunca del cliente. Si la cita ya estaba
        /// cerrada y cambia el total, registra un movimiento de ajuste por la
        /// diferencia en vez de modificar los movimientos anteriores.
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCita(int id, [FromBody] UpdateCitaDto dto)
        {
            await using var transaccion = await TransaccionAgenda.Abrir(_context);
            var cita = await _context.Citas
                .Include(c => c.CitaServicios)
                .FirstOrDefaultAsync(c => c.Id == id && c.EmpresaId == _context.CurrentEmpresaId);

            if (cita == null)
                return NotFound();

            // Solo las canceladas son intocables
            if (cita.Estado == EstadoCita.Cancelada)
                return BadRequest("No se puede modificar una cita cancelada.");

            if (dto.Servicios == null || !dto.Servicios.Any())
                return BadRequest("La cita debe tener al menos un servicio.");

            if (dto.Servicios.Any(s => s.Cantidad <= 0))
                return BadRequest("Cantidad inválida.");

            var servicioIds = dto.Servicios.Select(s => s.ServicioId).Distinct().ToList();

            var serviciosDb = await _context.Servicios
                .Where(s => servicioIds.Contains(s.Id) && s.Activo)
                .ToListAsync();

            if (serviciosDb.Count != servicioIds.Count)
                return BadRequest("Uno o más servicios no existen o no están activos.");

            await ValidarPertenenciaAsync(dto.ClienteId, dto.EmpleadoId);
            if (!await _context.Empleados.AnyAsync(e => e.Id == dto.EmpleadoId && e.EmpresaId == _context.CurrentEmpresaId))
                return NotFound();

            var duracionesOriginales = cita.CitaServicios.GroupBy(cs => cs.ServicioId)
                .ToDictionary(g => g.Key, g => g.First().DuracionMinutos);
            if (cita.Estado == EstadoCita.Pendiente && dto.Fecha > DateTime.UtcNow &&
                !await DentroDelHorario(dto.EmpleadoId, dto.Fecha, dto.Servicios, duracionesOriginales))
                return Conflict("La cita queda fuera del horario laboral o coincide con una ausencia del profesional.");

            if (cita.Estado == EstadoCita.Pendiente &&
                dto.Fecha > DateTime.UtcNow &&
                await HaySolapamientoAsync(
                    dto.EmpleadoId,
                    dto.Fecha,
                    dto.Servicios,
                    cita.Id, duracionesOriginales))
                return Conflict(MensajeSolapamiento);

            // Si la cita estaba cerrada y se están cambiando los servicios,
            // recalcular y ajustar los movimientos de cuenta
            bool esCerrada = cita.Estado == EstadoCita.Cerrada;
            decimal totalAnterior = cita.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad);

            // Misma validacion que al crear: editar tambien permitia apuntar la
            // cita a un cliente de otra empresa.
            await ValidarPertenenciaAsync(dto.ClienteId, dto.EmpleadoId);

            cita.ClienteId = dto.ClienteId;
            cita.EmpleadoId = dto.EmpleadoId;
            cita.Fecha = dto.Fecha;

            cita.CitaServicios.Clear();

            foreach (var item in dto.Servicios)
            {
                var servicio = serviciosDb.First(s => s.Id == item.ServicioId);
                cita.CitaServicios.Add(new CitaServicio
                {
                    ServicioId = servicio.Id,
                    Precio = servicio.Precio,
                    DuracionMinutos = duracionesOriginales.GetValueOrDefault(servicio.Id) ?? servicio.DuracionMinutos,
                    Cantidad = item.Cantidad
                });
            }

            decimal totalNuevo = cita.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad);

            // Si había diferencia de monto en una cita cerrada, registrar ajuste
            if (esCerrada && totalNuevo != totalAnterior)
            {
                var diferencia = totalNuevo - totalAnterior;
                _context.CuentasCliente.Add(new CuentaCliente
                {
                    ClienteId = cita.ClienteId,
                    CitaId = cita.Id,
                    Tipo = diferencia > 0 ? TipoMovimiento.Cargo : TipoMovimiento.Abono,
                    Monto = Math.Abs(diferencia),
                    Observaciones = $"[Ajuste] Edición de cita #{cita.Id} cerrada"
                });
            }

            await _context.SaveChangesAsync();

            await transaccion.CommitAsync();

            var citaCompleta = await _context.Citas
                .Include(c => c.Cliente)
                .Include(c => c.Empleado)
                .Include(c => c.CitaServicios)
                    .ThenInclude(cs => cs.Servicio)
                .FirstAsync(c => c.Id == cita.Id);

            return Ok(MapToCitaResponse(citaCompleta));
        }

        // =========================================================
        // DELETE: api/citas/{id}
        // =========================================================
        /// <summary>
        /// DELETE no borra la fila: la marca como Cancelada (se conserva para
        /// historial). Las cerradas no se pueden cancelar.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> CancelarCita(int id)
        {
            var cita = await _context.Citas.FindAsync(id);

            if (cita == null)
                return NotFound();

            if (cita.Estado == EstadoCita.Cerrada)
                return BadRequest("No se puede cancelar una cita cerrada.");

            cita.Estado = EstadoCita.Cancelada;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // =========================================================
        // MÉTODO PRIVADO: Mapear a DTO
        // =========================================================
        /// <summary>Convierte la entidad Cita (con sus relaciones cargadas) al DTO de respuesta.</summary>
        private static CitaResponseDto MapToCitaResponse(Cita cita)
        {
            var estadoNombres = new Dictionary<EstadoCita, string>
            {
                { EstadoCita.Pendiente, "Pendiente" },
                { EstadoCita.PagoPendiente, "Por Cobrar" },
                { EstadoCita.Pagada, "Pagada" },
                { EstadoCita.Cerrada, "Cerrada" },
                { EstadoCita.Cancelada, "Cancelada" }
            };

            return new CitaResponseDto
            {
                Id = cita.Id,
                Fecha = cita.Fecha,
                Estado = (int)cita.Estado,
                EstadoNombre = estadoNombres.GetValueOrDefault(cita.Estado, "Desconocido"),
                PagadoAlEmpleado = cita.PagadoAlEmpleado,
                ClienteId = cita.ClienteId,
                ClienteNombre = cita.Cliente?.Nombre ?? "Sin cliente",
                ClienteTelefono = cita.Cliente?.Telefono,
                EmpleadoId = cita.EmpleadoId,
                EmpleadoNombre = cita.Empleado?.Nombre ?? "Sin asignar",
                Servicios = cita.CitaServicios?.Select(cs => new CitaServicioResponseDto
                {
                    ServicioId = cs.ServicioId,
                    ServicioNombre = cs.Servicio?.Nombre ?? "Servicio",
                    Precio = cs.Precio,
                    Cantidad = cs.Cantidad,
                    DuracionMinutos = cs.DuracionMinutos ?? cs.Servicio?.DuracionMinutos ?? 30
                }).ToList() ?? new List<CitaServicioResponseDto>(),
                Total = cita.CitaServicios?.Sum(cs => cs.Precio * cs.Cantidad) ?? 0
            };
        }

        /// <summary>
        /// Comprueba disponibilidad usando la duración real de los servicios.
        /// Dos citas que se tocan justo en el límite (fin == inicio) sí son válidas.
        /// Canceladas, cobradas y cerradas no ocupan agenda futura.
        /// </summary>
        private async Task<bool> HaySolapamientoAsync(
            int empleadoId,
            DateTime inicio,
            IReadOnlyCollection<ServicioCantidadDto> serviciosSolicitados,
            int? citaIdExcluir = null,
            IReadOnlyDictionary<int, int?>? originales = null)
        {
            if (serviciosSolicitados.Count == 0)
                return false;

            var ids = serviciosSolicitados
                .Select(s => s.ServicioId)
                .Distinct()
                .ToList();

            var duraciones = await _context.Servicios
                .Where(s => ids.Contains(s.Id) && s.Activo)
                .Select(s => new { s.Id, s.DuracionMinutos })
                .ToDictionaryAsync(s => s.Id, s => s.DuracionMinutos);

            // La validación normal devolverá el error de servicio inválido.
            if (duraciones.Count != ids.Count)
                return false;

            foreach (var id in ids)
                if (originales?.GetValueOrDefault(id) is int minutosOriginales)
                    duraciones[id] = minutosOriginales;

            var minutos = serviciosSolicitados.Sum(
                item => duraciones[item.ServicioId] * item.Cantidad);
            var fin = inicio.AddMinutes(Math.Max(minutos, 1));

            var query = _context.Citas
                .Include(c => c.CitaServicios)
                    .ThenInclude(cs => cs.Servicio)
                .Where(c =>
                    c.EmpleadoId == empleadoId &&
                    c.Estado == EstadoCita.Pendiente &&
                    c.Fecha < fin);

            if (citaIdExcluir.HasValue)
                query = query.Where(c => c.Id != citaIdExcluir.Value);

            var candidatas = await query.ToListAsync();

            return candidatas.Any(cita =>
            {
                var minutosExistentes = cita.CitaServicios.Sum(
                    cs => (cs.DuracionMinutos ?? cs.Servicio.DuracionMinutos) * cs.Cantidad);
                var finExistente = cita.Fecha.AddMinutes(
                    Math.Max(minutosExistentes, 1));

                return cita.Fecha < fin && finExistente > inicio;
            });
        }

        private async Task<bool> DentroDelHorario(int empleadoId, DateTime inicio, IReadOnlyCollection<ServicioCantidadDto>? servicios, IReadOnlyDictionary<int, int?>? originales = null)
        {
            if (servicios is null || servicios.Count == 0) return true;
            var ids = servicios.Select(s => s.ServicioId).Distinct().ToList();
            var duraciones = await _context.Servicios.Where(s => ids.Contains(s.Id) && s.Activo)
                .ToDictionaryAsync(s => s.Id, s => s.DuracionMinutos);
            if (duraciones.Count != ids.Count || servicios.Count == 0 || servicios.Any(s => s.Cantidad <= 0)) return true;
            foreach (var id in ids)
                if (originales?.GetValueOrDefault(id) is int minutosOriginales)
                    duraciones[id] = minutosOriginales;
            var minutos = servicios.Sum(s => (long)duraciones[s.ServicioId] * s.Cantidad);
            if (minutos > 1440) return false;
            return await new BarberShop.Services.HorarioService(_context).Permite(empleadoId, inicio, (int)minutos);
        }

        /// <summary>
        /// Comprueba que el cliente y el empleado existan Y sean de la empresa de
        /// la sesion. El filtro global de tenant hace el trabajo: un id de otra
        /// empresa no se encuentra.
        /// </summary>
        /// <remarks>Lanza InvalidOperationException, que ManejadorGlobalDeExcepciones convierte en 400.</remarks>
        private async Task ValidarPertenenciaAsync(int clienteId, int empleadoId)
        {
            if (!await _context.Clientes.AnyAsync(c => c.Id == clienteId))
                throw new InvalidOperationException("Cliente inválido o fuera de la empresa.");

            if (!await _context.Empleados.AnyAsync(e => e.Id == empleadoId))
                throw new InvalidOperationException("Empleado inválido o fuera de la empresa.");
        }

        // =========================================================
        // MÉTODO PRIVADO: Crear cita interna
        // =========================================================
        /// <summary>
        /// Logica comun de creacion: valida servicios (activos y de la empresa),
        /// cliente y empleado; congela el precio de cada servicio en CitaServicio
        /// (si manana cambia el precio, la cita vieja conserva el que se cobro);
        /// guarda la cita, crea el Cargo si ya fue atendida, y descarta los
        /// recordatorios activos del cliente porque ya volvio.
        /// </summary>
        private async Task<Cita> CrearCitaInterna(CreateCitaBaseDto dto, EstadoCita estadoInicial)
        {
            if (dto.Servicios == null || !dto.Servicios.Any())
                throw new InvalidOperationException("La cita debe tener al menos un servicio.");
            if (dto.Servicios.Any(s => s.Cantidad <= 0))
                throw new InvalidOperationException("Cantidad inválida.");

            var servicioIds = dto.Servicios
                .Select(s => s.ServicioId)
                .Distinct()
                .ToList();

            var serviciosDb = await _context.Servicios
                .Where(s => servicioIds.Contains(s.Id) && s.Activo)
                .ToListAsync();

            if (serviciosDb.Count != servicioIds.Count)
                throw new InvalidOperationException("Servicio inválido o fuera de la empresa.");

            // Cliente y empleado se validan igual que los servicios.
            //
            // Antes se copiaban del DTO sin comprobar nada. Como la clave foranea
            // apunta a la tabla entera y no al tenant, un id de OTRA empresa
            // pasaba el INSERT sin error: quedaba una cita de la empresa A
            // apuntando a un cliente de la B, y MapToCitaResponse devolvia su
            // nombre y su telefono. Fuga de datos con solo probar numeros.
            //
            // Las consultas van por el filtro global, asi que un id ajeno
            // simplemente no aparece.
            await ValidarPertenenciaAsync(dto.ClienteId, dto.EmpleadoId);

            var cita = new Cita
            {
                ClienteId = dto.ClienteId,
                EmpleadoId = dto.EmpleadoId,
                Fecha = dto.Fecha,
                Estado = estadoInicial
            };

            foreach (var item in dto.Servicios)
            {
                var servicio = serviciosDb.First(s => s.Id == item.ServicioId);
                cita.CitaServicios.Add(new CitaServicio
                {
                    ServicioId = servicio.Id,
                    Precio = servicio.Precio,
                    DuracionMinutos = servicio.DuracionMinutos,
                    Cantidad = item.Cantidad
                });
            }

            _context.Citas.Add(cita);
            await _context.SaveChangesAsync();

            if (estadoInicial != EstadoCita.Pendiente)
            {
                var total = cita.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad);
                _context.CuentasCliente.Add(new CuentaCliente
                {
                    ClienteId = cita.ClienteId,
                    CitaId = cita.Id,
                    Tipo = TipoMovimiento.Cargo,
                    Monto = total,
                    Observaciones = $"Cita #{cita.Id}"
                });
            }

            var recordatoriosActivos = await _context.Recordatorios
                .Where(r =>
                    r.ClienteId == dto.ClienteId &&
                    r.Estado != EstadoRecordatorio.Descartado)
                .ToListAsync();

            foreach (var recordatorio in recordatoriosActivos)
            {
                recordatorio.Estado = EstadoRecordatorio.Descartado;
                recordatorio.FechaGestion = DateTime.UtcNow;
                recordatorio.Nota = $"Cliente vino, cita #{cita.Id} generada";
            }

            await _context.SaveChangesAsync();

            return cita;
        }
    }

    /// <summary>Cuerpo opcional al cobrar: una observacion para el movimiento.</summary>
    public class CobrarCitaDto
    {
        public string? Observaciones { get; set; }
    }

    /// <summary>Cuerpo al reabrir: motivo de la correccion.</summary>
    public class ReabrirCitaDto
    {
        public string? Motivo { get; set; }
    }
}
