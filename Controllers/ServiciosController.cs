using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Interfaces;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Controllers
{
    /// <summary>
    /// CRUD del catalogo de servicios del negocio (corte, barba, unas...) con
    /// precio, duracion e intervalo de recordatorio. Solo Admin/SuperAdmin.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class ServiciosController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IUserContext _userContext;
        private readonly PlanValidationService _planService;

        /// <summary>Recibe BD, contexto del usuario y validador de planes por DI.</summary>
        public ServiciosController(AppDbContext context, IUserContext userContext, PlanValidationService planService)
        {
            _context = context;
            _userContext = userContext;
            _planService = planService;
        }

        // GET: api/Servicios
        /// <summary>Lista los servicios activos de la empresa ordenados por categoria y nombre.</summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Servicio>>> GetServicios()
        {
            return await _context.Servicios
                .Where(s => s.Activo)
                .OrderBy(s => s.Categoria)
                .ThenBy(s => s.Nombre)
                .ToListAsync();
        }

        // GET: api/Servicios/5
        /// <summary>
        /// Un servicio por id. FindAsync busca por clave primaria (y respeta el filtro
        /// de tenant): si es de otra empresa devuelve null -> 404. "{id:int}" es una
        /// restriccion de ruta: solo matchea si el segmento es un entero.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<Servicio>> GetServicio(int id)
        {
            var servicio = await _context.Servicios.FindAsync(id);

            if (servicio == null)
                return NotFound();

            return servicio;
        }

        // POST: api/Servicios
        /// <summary>
        /// Crea un servicio si el plan lo permite. Devuelve 201 Created con cabecera
        /// Location apuntando a GetServicio (CreatedAtAction), la convencion REST.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<Servicio>> CreateServicio([FromBody] CreateServicioDto createDto)
        {
            // 1. Validación del DTO (Annotations)
            if (!ModelState.IsValid)
                return BadRequest(ModelState);
            // ── Validar límite de plan ──
            var empresaId = _userContext.EmpresaId;
            if (!empresaId.HasValue) return Unauthorized();

            var (permitido, error) = await _planService.PuedeCrearServicioAsync(empresaId.Value);
            if (!permitido)
                return BadRequest(new { message = error, code = "PLAN_LIMIT" });
            // 2. Mapeo Manual (Dto -> Entity)
            // Pasamos los datos limpios a la entidad real
            var servicio = new Servicio
            {
                Nombre = createDto.Nombre,
                Descripcion = createDto.Descripcion,
                Precio = createDto.Precio,
                DuracionMinutos = createDto.DuracionMinutos,
                IntervaloDias = createDto.IntervaloDias,
                Categoria = createDto.Categoria,
                ColorHex = createDto.ColorHex,
                EnPromocion = createDto.EnPromocion,
                ImagenUrl = createDto.ImagenUrl,

            };

            // 3. Guardado
            _context.Servicios.Add(servicio);

            // Aquí es donde tu DbContext intercepta y pone el EmpresaId y FechaCreacion
            await _context.SaveChangesAsync();

            // 4. Retorno
            // Devolvemos el objeto completo (con ID generado) al cliente
            return CreatedAtAction(nameof(GetServicio), new { id = servicio.Id }, servicio);
        }

        /// <summary>
        /// Actualiza un servicio existente. EF detecta que propiedades cambiaron
        /// ("change tracking") y el UPDATE solo incluye esas columnas.
        /// </summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateServicio(int id, [FromBody] UpdateServicioDto updateDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // 1. Buscamos la entidad existente

            var existente = await _context.Servicios.FindAsync(id);

            if (existente == null)
                return NotFound("El servicio no existe o no pertenece a tu empresa.");

            // 2. Mapeo Manual (DTO -> Entidad existente)
            // Solo actualizamos lo que permitimos cambiar
            existente.Nombre = updateDto.Nombre;
            existente.Descripcion = updateDto.Descripcion;
            existente.Precio = updateDto.Precio;
            existente.DuracionMinutos = updateDto.DuracionMinutos;
            existente.IntervaloDias = updateDto.IntervaloDias;
            existente.Categoria = updateDto.Categoria;
            existente.ColorHex = updateDto.ColorHex;
            existente.EnPromocion = updateDto.EnPromocion;
            existente.ImagenUrl = updateDto.ImagenUrl;
            existente.Activo = updateDto.Activo;


            try
            {
                // 3. Guardar cambios
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // Manejo de concurrencia (opcional pero recomendado)
                if (!ServicioExists(id)) return NotFound();
                else throw;
            }

            return NoContent(); // 204 No Content es el estándar para Updates exitosos
        }

        // Método auxiliar para el catch de concurrencia
        /// <summary>true si el servicio sigue existiendo (se usa al capturar un conflicto de concurrencia).</summary>
        private bool ServicioExists(int id)
        {
            return (_context.Servicios?.Any(e => e.Id == id)).GetValueOrDefault();
        }

        /// <summary>
        /// Borra un servicio. Si ya se uso en alguna cita hace "soft delete"
        /// (Activo = false) para no romper el historial ni la clave foranea; si nunca
        /// se uso, lo borra fisicamente.
        /// </summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteServicio(int id)
        {
            var servicio = await _context.Servicios.FindAsync(id);
            if (servicio == null)
                return NotFound();

            // Hard delete only if never used in citas (FK safe)
            var usado = await _context.CitasServicios
                .AnyAsync(cs => cs.ServicioId == id);

            if (usado)
            {
                // Soft delete — safe because the row stays
                servicio.Activo = false;
                await _context.SaveChangesAsync();
                return NoContent();
            }

            // Never used — can hard delete
            _context.Servicios.Remove(servicio);
            await _context.SaveChangesAsync();
            return NoContent();
        }
        // GET: api/Servicios/activos
        /// <summary>
        /// Igual que GetServicios pero con [AllowAnonymous]. OJO: sin JWT no hay
        /// EmpresaId, asi que el filtro de tenant compara EmpresaId == null y la lista
        /// sale VACIA. Para una pagina publica de reservas habria que recibir la
        /// empresa (p. ej. por slug en la URL) y usar IgnoreQueryFilters().
        /// </summary>
        [HttpGet("activos")]
        [AllowAnonymous] // útil para agenda pública o móvil
        public async Task<ActionResult<IEnumerable<Servicio>>> GetServiciosActivos()
        {
            return await _context.Servicios
                .Where(s => s.Activo)
                .OrderBy(s => s.Categoria)
                .ThenBy(s => s.Nombre)
                .ToListAsync();
        }
    }
}
