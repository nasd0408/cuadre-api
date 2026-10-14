using BarberShop.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace BarberShop.Tests;

/// <summary>
/// El manejador global es lo unico que separa "500 con cuerpo vacio y sin traza"
/// de una respuesta que la app puede interpretar. Si se rompe, se vuelve a la
/// ceguera anterior sin que nadie se entere, asi que va cubierto.
/// </summary>
public class ManejadorExcepcionesTests
{
    [Fact]
    public async Task Timeout_de_bloqueo_devuelve_conflicto_sin_detalles_internos()
    {
        var (estado, cuerpo) = await Ejecutar(new Npgsql.PostgresException("detalle interno", "ERROR", "ERROR", "55P03"));
        Assert.Equal(409, estado);
        Assert.Equal("Agenda ocupada", cuerpo.GetProperty("title").GetString());
        Assert.DoesNotContain("detalle interno", cuerpo.ToString());
    }
    private static async Task<(int estado, JsonElement cuerpo)> Ejecutar(Exception aLanzar)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Method = "POST";
        contexto.Request.Path = "/api/citas/1/cobrar";
        contexto.Response.Body = new MemoryStream();

        var manejador = new ManejadorGlobalDeExcepciones(
            _ => throw aLanzar,
            NullLogger<ManejadorGlobalDeExcepciones>.Instance);

        await manejador.InvokeAsync(contexto);

        contexto.Response.Body.Seek(0, SeekOrigin.Begin);
        var texto = await new StreamReader(contexto.Response.Body).ReadToEndAsync();
        return (contexto.Response.StatusCode, JsonDocument.Parse(texto).RootElement);
    }

    [Fact]
    public async Task ConflictoDeConcurrencia_Devuelve409_ConMensajeAccionable()
    {
        // Es el caso del doble toque en "Cobrar": la app necesita distinguirlo de
        // un error de servidor para poder decirle al usuario que recargue.
        var (estado, cuerpo) = await Ejecutar(new DbUpdateConcurrencyException("choque"));

        Assert.Equal(StatusCodes.Status409Conflict, estado);
        Assert.Contains("modificó", cuerpo.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ErrorInesperado_Devuelve500_SIN_filtrarElMensajeInterno()
    {
        // ex.Message de Npgsql lleva nombres de tabla, columna y restriccion.
        const string secreto = "relation \"CuentasCliente\" violates constraint";
        var (estado, cuerpo) = await Ejecutar(new Exception(secreto));

        Assert.Equal(StatusCodes.Status500InternalServerError, estado);
        Assert.DoesNotContain("CuentasCliente", cuerpo.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task TodaRespuesta_LlevaTraceId_ParaPoderCruzarlaConElLog()
    {
        var (_, cuerpo) = await Ejecutar(new Exception("lo que sea"));

        Assert.False(string.IsNullOrWhiteSpace(cuerpo.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task ArgumentoInvalido_Devuelve400_YSiEnsenaElMotivo()
    {
        // CitasController lanza InvalidOperationException con un DTO sin servicios;
        // antes salia como 500 y el usuario no sabia que corregir.
        var (estado, cuerpo) = await Ejecutar(
            new InvalidOperationException("La cita debe tener al menos un servicio."));

        Assert.Equal(StatusCodes.Status400BadRequest, estado);
        Assert.Contains("al menos un servicio", cuerpo.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task RespuestaYaIniciada_NoRevienta_AlIntentarReescribirla()
    {
        var contexto = new DefaultHttpContext();
        contexto.Response.Body = new MemoryStream();
        await contexto.Response.WriteAsync("ya enviado");

        var manejador = new ManejadorGlobalDeExcepciones(
            _ => throw new Exception("tarde"),
            NullLogger<ManejadorGlobalDeExcepciones>.Instance);

        // No debe lanzar: cambiar el estado con la respuesta empezada seria un
        // InvalidOperationException que taparia el error original.
        await manejador.InvokeAsync(contexto);
    }
}
