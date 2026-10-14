// Controllers/SyncController.cs
using BarberShop.Data;
using BarberShop.Enum;
using BarberShop.Interfaces;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Controllers
{
    /// <summary>
    /// Sincronizacion offline: la app guarda citas en el telefono cuando no hay
    /// internet y luego las manda todas juntas aqui. Cada cita trae un OfflineId
    /// generado en el telefono que sirve de "clave de idempotencia": si la app
    /// reintenta el envio, las que ya se guardaron no se duplican (hay un indice
    /// unico (EmpresaId, OfflineId) en la BD).
    /// </summary>
    [ApiController]
    [Route("api/sync")]
    [Authorize(Roles = "Admin,Barbero,SuperAdmin")]
    public class SyncController : ControllerBase
    {
        /// <summary>Tope de cordura: nadie cobra 100 veces la tarifa de catalogo.</summary>
        private const decimal FACTOR_MAXIMO_SOBRE_CATALOGO = 100m;

        private readonly AppDbContext _context;
        private readonly IUserContext _userContext;
        private readonly PlanValidationService _planService;

        /// <summary>Recibe BD, contexto del usuario y validador de planes por DI.</summary>
        public SyncController(AppDbContext context, IUserContext userContext, PlanValidationService planService)
        {
            _context = context;
            _userContext = userContext;
            _planService = planService;
        }

        // =========================================================
        // POST: api/sync/citas
        // Sincroniza un lote de citas offline en una sola llamada
        // =========================================================
        /// <summary>
        /// Procesa el lote: valida offlineIds, descarta los ya sincronizados, aplica
        /// el limite de citas offline del plan y, dentro de una transaccion, crea
        /// cada cita (y el cliente si es nuevo) con sus movimientos de dinero.
        /// Devuelve un resultado POR CITA (ok/error) para que la app sepa cuales
        /// borrar de su cola local y cuales reintentar.
        /// </summary>
        [HttpPost("citas")]
        public async Task<ActionResult<SyncResultDto>> SyncCitas([FromBody] SyncCitasRequestDto request)
        {
            if (request.Citas == null || !request.Citas.Any())
                return BadRequest("Debe enviar al menos una cita.");

            var resultados = new List<SyncCitaResultadoDto>();
            var empresaId = _userContext.EmpresaId;
            if (!empresaId.HasValue) return Unauthorized();

            if (request.Citas.Any(c => string.IsNullOrWhiteSpace(c.OfflineId)))
                return BadRequest("Todas las citas deben incluir offlineId.");

            var offlineIds = request.Citas.Select(c => c.OfflineId.Trim()).ToList();
            if (offlineIds.Distinct().Count() != offlineIds.Count)
                return BadRequest("El lote contiene offlineId duplicados.");

            // Los reintentos ya procesados no consumen nuevamente el límite ni
            // crean citas duplicadas.
            var existentes = await _context.Citas
                .Where(c => c.OfflineId != null && offlineIds.Contains(c.OfflineId))
                .Select(c => new { c.OfflineId, c.Id })
                .ToDictionaryAsync(c => c.OfflineId!, c => c.Id);

            var nuevas = offlineIds.Count(id => !existentes.ContainsKey(id));
            var (permitido, max, error) = await _planService.ValidarCitasOfflineAsync(
                empresaId.Value, nuevas);
            if (!permitido)
                return BadRequest(new { message = error, code = "PLAN_LIMIT", maxCitasOffline = max });

            // Usar una transacción para todo el lote
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                foreach (var citaDto in request.Citas)
                {
                    citaDto.OfflineId = citaDto.OfflineId.Trim();
                    var resultado = new SyncCitaResultadoDto
                    {
                        OfflineId = citaDto.OfflineId
                    };

                    try
                    {
                        if (existentes.TryGetValue(citaDto.OfflineId, out var citaExistenteId))
                        {
                            resultado.Ok = true;
                            resultado.CitaId = citaExistenteId;
                            resultados.Add(resultado);
                            continue;
                        }

                        // 1. Validar empleado y servicios antes de crear datos.
                        var empleadoExiste = await _context.Empleados
                            .AnyAsync(e => e.Id == citaDto.EmpleadoId && e.Activo);

                        if (!empleadoExiste)
                        {
                            resultado.Ok = false;
                            resultado.Error = $"El empleado ya no está disponible.";
                            resultados.Add(resultado);
                            continue;
                        }

                        if (citaDto.Servicios == null || !citaDto.Servicios.Any())
                        {
                            resultado.Ok = false;
                            resultado.Error = "La cita debe tener al menos un servicio.";
                            resultados.Add(resultado);
                            continue;
                        }

                        if (citaDto.Servicios.Any(s => s.Cantidad <= 0))
                        {
                            resultado.Ok = false;
                            resultado.Error = "Cantidad inválida en servicios.";
                            resultados.Add(resultado);
                            continue;
                        }

                        var servicioIds = citaDto.Servicios
                            .Select(s => s.ServicioId)
                            .Distinct()
                            .ToList();

                        var serviciosDb = await _context.Servicios
                            .Where(s => servicioIds.Contains(s.Id) && s.Activo)
                            .ToListAsync();

                        if (serviciosDb.Count != servicioIds.Count)
                        {
                            resultado.Ok = false;
                            resultado.Error = "Uno o más servicios ya no están disponibles.";
                            resultados.Add(resultado);
                            continue;
                        }

                        // 2. Resolver cliente después de validar el resto para no
                        // dejar clientes huérfanos si el registro es inválido.
                        int clienteId;
                        if (citaDto.ClienteId.HasValue)
                        {
                            var clienteExiste = await _context.Clientes
                                .AnyAsync(c => c.Id == citaDto.ClienteId.Value);

                            if (!clienteExiste)
                            {
                                resultado.Ok = false;
                                resultado.Error = "El cliente ya no existe.";
                                resultados.Add(resultado);
                                continue;
                            }

                            clienteId = citaDto.ClienteId.Value;
                        }
                        else if (!string.IsNullOrWhiteSpace(citaDto.ClienteNombre))
                        {
                            var nuevoCliente = new Cliente
                            {
                                Nombre = citaDto.ClienteNombre.Trim(),
                                Telefono = citaDto.ClienteTelefono?.Trim()
                            };

                            _context.Clientes.Add(nuevoCliente);
                            await _context.SaveChangesAsync();
                            clienteId = nuevoCliente.Id;

                            resultado.ClienteCreado = true;
                            resultado.ClienteId = clienteId;
                        }
                        else
                        {
                            resultado.Ok = false;
                            resultado.Error = "Debe enviar clienteId o clienteNombre.";
                            resultados.Add(resultado);
                            continue;
                        }

                        // 3. Determinar estado
                        var esPagada = citaDto.Tipo?.ToLower() == "pagada";
                        var estadoInicial = esPagada
                            ? EstadoCita.Pagada
                            : EstadoCita.PagoPendiente;

                        // 5. Crear cita
                        var cita = new Cita
                        {
                            ClienteId = clienteId,
                            EmpleadoId = citaDto.EmpleadoId,
                            Fecha = citaDto.Fecha,
                            Estado = estadoInicial,
                            OfflineId = citaDto.OfflineId
                        };

                        foreach (var item in citaDto.Servicios)
                        {
                            var servicio = serviciosDb.First(s => s.Id == item.ServicioId);
                            cita.CitaServicios.Add(new CitaServicio
                            {
                                ServicioId = servicio.Id,
                                // El precio lo manda el cliente A PROPOSITO: una cita
                                // offline registra lo que se cobro en ese momento, y
                                // el dueno pudo cambiar la tarifa antes de que hubiera
                                // red. Forzar el precio del catalogo aqui reescribiria
                                // importes historicos y descuadraria comisiones ya
                                // liquidadas. No cambiar esto sin leer el metodo
                                // PrecioAceptable de abajo.
                                //
                                // Builds anteriores no enviaban Precio; en ese caso se
                                // usa el actual por compatibilidad.
                                Precio = PrecioAceptable(item.Precio, servicio.Precio),
                                DuracionMinutos = servicio.DuracionMinutos,
                                Cantidad = item.Cantidad
                            });
                        }

                        _context.Citas.Add(cita);
                        await _context.SaveChangesAsync();

                        var total = cita.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad);

                        // 6. Registrar cargo en cuenta
                        _context.CuentasCliente.Add(new CuentaCliente
                        {
                            ClienteId = clienteId,
                            CitaId = cita.Id,
                            Tipo = TipoMovimiento.Cargo,
                            Monto = total,
                            Observaciones = $"Cita #{cita.Id} (sync offline)"
                        });

                        // 7. Si es pagada → registrar abono y cerrar
                        if (esPagada)
                        {
                            _context.CuentasCliente.Add(new CuentaCliente
                            {
                                ClienteId = clienteId,
                                CitaId = cita.Id,
                                Monto = total,
                                Tipo = TipoMovimiento.Abono,
                                Observaciones = citaDto.ObservacionesPago ?? "Pago inmediato (sync offline)"
                            });

                            cita.Estado = EstadoCita.Cerrada;

                            var cliente = await _context.Clientes.FindAsync(clienteId);
                            if (cliente != null)
                            {
                                cliente.FechaUltimoCorte = DateTime.UtcNow;
                            }
                        }

                        // 8. Descartar recordatorios activos
                        var recordatoriosActivos = await _context.Recordatorios
                            .Where(r =>
                                r.ClienteId == clienteId &&
                                r.Estado != EstadoRecordatorio.Descartado)
                            .ToListAsync();

                        foreach (var recordatorio in recordatoriosActivos)
                        {
                            recordatorio.Estado = EstadoRecordatorio.Descartado;
                            recordatorio.FechaGestion = DateTime.UtcNow;
                            recordatorio.Nota = $"Sync offline, cita #{cita.Id}";
                        }

                        await _context.SaveChangesAsync();

                        resultado.Ok = true;
                        resultado.CitaId = cita.Id;
                    }
                    catch
                    {
                        // Un fallo inesperado después de crear entidades debe
                        // abortar el lote completo. El cliente lo reintentará de
                        // forma segura gracias a OfflineId.
                        throw;
                    }

                    resultados.Add(resultado);
                }

                await transaction.CommitAsync();

                return Ok(new SyncResultDto
                {
                    Total = resultados.Count,
                    Exitosas = resultados.Count(r => r.Ok),
                    Fallidas = resultados.Count(r => !r.Ok),
                    Resultados = resultados
                });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { error = "Error en la sincronización. El lote no fue aplicado." });
            }
        }
        /// <summary>
        /// Acepta el precio que trae la cita offline, pero no cualquiera.
        ///
        /// Un cero (o negativo) significa "build vieja que no lo enviaba": se usa
        /// el del catalogo. Un valor absurdo indica dato corrupto en el telefono,
        /// no una tarifa real, y colarlo descuadraria ingresos, comisiones y deuda
        /// sin forma de detectarlo despues.
        /// </summary>
        private static decimal PrecioAceptable(decimal precioCliente, decimal precioCatalogo)
        {
            if (precioCliente <= 0) return precioCatalogo;

            var tope = precioCatalogo * FACTOR_MAXIMO_SOBRE_CATALOGO;
            return precioCliente > tope ? precioCatalogo : precioCliente;
        }
    }

    /// <summary>Cuerpo de POST api/sync/citas: la lista de citas offline.</summary>
    public class SyncCitasRequestDto
    {
        public List<SyncCitaItemDto> Citas { get; set; } = new();
    }

    /// <summary>Una cita creada offline en el telefono.</summary>
    public class SyncCitaItemDto
    {
        /// <summary>ID local del frontend para correlacionar resultados</summary>
        public string OfflineId { get; set; } = "";

        /// <summary>"pagada" | "pendiente"</summary>
        public string Tipo { get; set; } = "pendiente";

        /// <summary>ID del cliente existente (null si es nuevo)</summary>
        public int? ClienteId { get; set; }

        /// <summary>Nombre del cliente nuevo (se usa si ClienteId es null)</summary>
        public string? ClienteNombre { get; set; }

        /// <summary>Teléfono del cliente nuevo</summary>
        public string? ClienteTelefono { get; set; }

        public int EmpleadoId { get; set; }
        public DateTime Fecha { get; set; }

        public List<SyncCitaServicioDto> Servicios { get; set; } = new();

        public string? ObservacionesPago { get; set; }
    }

    /// <summary>Servicio de una cita offline (con el precio que se cobro).</summary>
    public class SyncCitaServicioDto
    {
        public int ServicioId { get; set; }
        public decimal Precio { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>Respuesta del sync: resultados individuales por cita.</summary>
    public class SyncResultDto
    {
        public int Total { get; set; }
        public int Exitosas { get; set; }
        public int Fallidas { get; set; }
        public List<SyncCitaResultadoDto> Resultados { get; set; } = new();
    }

    /// <summary>Resultado de una cita: su OfflineId, si se guardo, el Id asignado o el error.</summary>
    public class SyncCitaResultadoDto
    {
        public string OfflineId { get; set; } = "";
        public bool Ok { get; set; }
        public int? CitaId { get; set; }
        public string? Error { get; set; }
        public bool ClienteCreado { get; set; }
        public int? ClienteId { get; set; }
    }
}
