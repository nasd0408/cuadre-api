using System.ComponentModel.DataAnnotations;
using BarberShop.Controllers;
using BarberShop.Dtos;
using BarberShop.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Tests;

public class PerfilPublicoTests
{
    [Fact]
    public async Task Publico_solo_muestra_perfil_y_sucursales_publicados_de_la_empresa()
    {
        using var db = Ayudas.CrearContexto(new UserContextFalso());
        db.PerfilesPublicos.Add(new PerfilPublicoEmpresa
            { EmpresaId = 1, Nombre = "Salón", Categoria = "Belleza" });
        db.Sucursales.AddRange(
            new Sucursal { EmpresaId = 1, Nombre = "Visible", Publicada = true },
            new Sucursal { EmpresaId = 1, Nombre = "Borrador" },
            new Sucursal { EmpresaId = 2, Nombre = "Otra empresa", Publicada = true });
        await db.SaveChangesAsync();
        var api = new EmpresasPublicasController(db);
        Assert.IsType<NotFoundResult>(await api.Get(1));
        var perfil = await db.PerfilesPublicos.IgnoreQueryFilters().SingleAsync();
        perfil.Publicado = true;
        await db.SaveChangesAsync();
        var respuesta = Assert.IsType<EmpresaPublicaDto>(Assert.IsType<OkObjectResult>(await api.Get(1)).Value);
        Assert.Equal("Visible", Assert.Single(respuesta.Sucursales).Nombre);
        Assert.Null(respuesta.Perfil.TelefonoPublico);
        perfil.Publicado = false;
        await db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await api.Get(1));
    }

    [Fact]
    public async Task Admin_no_puede_editar_sucursal_ajena_ni_listarla()
    {
        using var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = 1 });
        var ajena = new Sucursal { EmpresaId = 2, Nombre = "Original" };
        db.Sucursales.Add(ajena);
        await db.SaveChangesAsync();
        var api = new SucursalesController(db);
        Assert.IsType<NotFoundResult>(await api.Update(ajena.Id, new SucursalDto { Nombre = "Intruso" }));
        Assert.Empty(Assert.IsType<List<SucursalResponseDto>>(Assert.IsType<OkObjectResult>(await api.Get()).Value));
        Assert.Equal("Original", ajena.Nombre);
    }

    [Fact]
    public async Task Perfil_no_copia_contacto_interno_y_se_actualiza_sin_duplicarse()
    {
        using var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = 1 });
        db.Empresas.Add(new Empresa { Id = 1, Nombre = "Interno", Telefono = "123456789", Direccion = "Privada" });
        await db.SaveChangesAsync();
        var api = new PerfilPublicoController(db);
        var dto = new PerfilPublicoDto { Nombre = "Público", Categoria = "Belleza", Instagram = "@salon" };
        await api.Put(dto);
        dto.Descripcion = "Descripción actualizada";
        await api.Put(dto);
        var perfil = await db.PerfilesPublicos.SingleAsync();
        Assert.False(perfil.Publicado);
        Assert.Null(perfil.TelefonoPublico);
        Assert.Equal("salon", perfil.Instagram);
        Assert.Equal(dto.Descripcion, perfil.Descripcion);
        Assert.Equal("123456789", (await db.Empresas.SingleAsync()).Telefono);
    }

    [Fact]
    public async Task Administracion_sin_empresa_es_rechazada()
    {
        using var db = Ayudas.CrearContexto(new UserContextFalso());
        Assert.IsType<ForbidResult>(await new PerfilPublicoController(db).Get());
        Assert.IsType<ForbidResult>(await new SucursalesController(db).Create(new SucursalDto { Nombre = "Local" }));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(0d, 0d, true)]
    [InlineData(90d, -180d, true)]
    [InlineData(91d, 0d, false)]
    [InlineData(0d, 181d, false)]
    [InlineData(28d, null, false)]
    [InlineData(null, -16d, false)]
    [InlineData(double.NaN, 0d, false)]
    public void Coordenadas_son_opcionales_pero_deben_ser_un_par_valido(double? lat, double? lon, bool valido)
    {
        var dto = new SucursalDto { Nombre = "Local", Latitud = lat, Longitud = lon };
        Assert.Equal(valido, Validator.TryValidateObject(dto, new ValidationContext(dto), [], true));
    }
}
