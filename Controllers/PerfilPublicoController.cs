using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Controllers;

[ApiController]
[Route("api/empresa/perfil-publico")]
[Authorize(Roles = "Admin")]
public class PerfilPublicoController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct = default)
    {
        if (db.CurrentEmpresaId is not int empresaId) return Forbid();
        var perfil = await db.PerfilesPublicos.AsNoTracking()
            .SingleOrDefaultAsync(p => p.EmpresaId == empresaId, ct);
        return perfil is null ? NotFound() : Ok(Map(perfil));
    }

    [HttpPut]
    public async Task<IActionResult> Put(PerfilPublicoDto dto, CancellationToken ct = default)
    {
        if (db.CurrentEmpresaId is not int empresaId) return Forbid();
        if (!await db.Empresas.AnyAsync(e => e.Id == empresaId, ct)) return NotFound();
        await using var transaction = await TransaccionAgenda.Abrir(db, ct);
        var perfil = await db.PerfilesPublicos.SingleOrDefaultAsync(p => p.EmpresaId == empresaId, ct);
        if (perfil is null)
        {
            perfil = new PerfilPublicoEmpresa { EmpresaId = empresaId };
            db.PerfilesPublicos.Add(perfil);
        }
        perfil.Nombre = dto.Nombre.Trim();
        perfil.Descripcion = dto.Descripcion?.Trim();
        perfil.Categoria = dto.Categoria.Trim();
        perfil.TelefonoPublico = dto.TelefonoPublico?.Trim();
        perfil.Instagram = dto.Instagram?.TrimStart('@');
        perfil.Facebook = dto.Facebook?.TrimStart('@');
        perfil.TikTok = dto.TikTok?.TrimStart('@');
        perfil.Publicado = dto.Publicado;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(Map(perfil));
    }

    internal static PerfilPublicoDto Map(PerfilPublicoEmpresa p) => new()
    {
        Nombre = p.Nombre, Descripcion = p.Descripcion, Categoria = p.Categoria,
        TelefonoPublico = p.TelefonoPublico, Instagram = p.Instagram,
        Facebook = p.Facebook, TikTok = p.TikTok, Publicado = p.Publicado
    };
}

[ApiController]
[Route("api/empresa/sucursales")]
[Authorize(Roles = "Admin")]
public class SucursalesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct = default)
    {
        if (db.CurrentEmpresaId is not int empresaId) return Forbid();
        var filas = await db.Sucursales.AsNoTracking().Where(s => s.EmpresaId == empresaId)
            .OrderBy(s => s.Id).ToListAsync(ct);
        return Ok(filas.Select(Map).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create(SucursalDto dto, CancellationToken ct = default)
    {
        if (db.CurrentEmpresaId is not int empresaId) return Forbid();
        if (!await db.Empresas.AnyAsync(e => e.Id == empresaId, ct)) return NotFound();
        var sucursal = new Sucursal { EmpresaId = empresaId };
        Apply(sucursal, dto);
        db.Sucursales.Add(sucursal);
        await db.SaveChangesAsync(ct);
        return StatusCode(StatusCodes.Status201Created, Map(sucursal));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SucursalDto dto, CancellationToken ct = default)
    {
        if (db.CurrentEmpresaId is not int empresaId) return Forbid();
        await using var transaction = await TransaccionAgenda.Abrir(db, ct);
        var sucursal = await db.Sucursales.SingleOrDefaultAsync(s => s.Id == id && s.EmpresaId == empresaId, ct);
        if (sucursal is null) return NotFound();
        Apply(sucursal, dto);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(Map(sucursal));
    }

    private static void Apply(Sucursal s, SucursalDto dto)
    {
        s.Nombre = dto.Nombre.Trim();
        s.DireccionPublica = dto.DireccionPublica?.Trim();
        s.Latitud = dto.Latitud;
        s.Longitud = dto.Longitud;
        s.Publicada = dto.Publicada;
    }

    internal static SucursalResponseDto Map(Sucursal s) =>
        new(s.Id, s.Nombre, s.DireccionPublica, s.Latitud, s.Longitud, s.Publicada);
}

[ApiController]
[Route("api/publico/empresas")]
[AllowAnonymous]
public class EmpresasPublicasController(AppDbContext db) : ControllerBase
{
    [HttpGet("{empresaId:int}")]
    public async Task<IActionResult> Get(int empresaId, CancellationToken ct = default)
    {
        // Única lectura transversal: publicación y empresa se filtran explícitamente.
        var perfil = await db.PerfilesPublicos.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(p => p.EmpresaId == empresaId && p.Publicado, ct);
        if (perfil is null) return NotFound();
        var sucursales = await db.Sucursales.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.EmpresaId == empresaId && s.Publicada).OrderBy(s => s.Id).ToListAsync(ct);
        return Ok(new EmpresaPublicaDto(empresaId, PerfilPublicoController.Map(perfil),
            sucursales.Select(SucursalesController.Map).ToList(), perfil.ReservasActivas ? perfil.TokenReservas : null));
    }
}
