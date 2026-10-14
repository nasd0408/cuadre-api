using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.WebSockets;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BarberShop.Data;
using BarberShop.Hubs;
using BarberShop.Interfaces;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace BarberShop.Tests;

// Kestrel y WebSockets reales; solo la persistencia del fixture usa InMemory.
public class AgendaRealtimeTests
{
    [Fact]
    public async Task Reservar_y_cancelar_por_HTTP_sin_cuenta_avisando_solo_a_la_empresa_del_enlace()
    {
        await using var server = await Servidor.Crear();
        using var scope = server.App.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        await using var owner = new AppDbContext(options, new UserContextFalso { EmpresaId = 1 });
        var inicio = await ReservasPublicasTests.Sembrar(owner);
        using var uno = await server.Conectar("uno", 1); using var dos = await server.Conectar("dos", 2);
        await Preparado(uno); await Preparado(dos);
        using var http = new HttpClient { BaseAddress = new Uri(server.Url) };
        var dto = ReservasPublicasTests.Solicitud(inicio);
        var invalido = ReservasPublicasTests.Solicitud(inicio); invalido.Email = "correo inválido";
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/publico/reservas/" + ReservasPublicasTests.TokenNegocio, invalido)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/empresa/reservas-publicas")).StatusCode);
        // Incluso con sesión de otra empresa, el destino lo determina el enlace.
        http.DefaultRequestHeaders.Authorization = new("Bearer", server.Token("dos", 2));
        var result = await http.PostAsJsonAsync("/api/publico/reservas/" + ReservasPublicasTests.TokenNegocio, dto);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        Assert.True(result.Headers.CacheControl?.NoStore);
        var hub = server.App.Services.GetRequiredService<IHubContext<AgendaHub>>();
        await hub.Clients.All.SendAsync("FinPrueba");
        Assert.Equal("AgendaChanged", (await Leer(uno)).GetProperty("target").GetString());
        Assert.Equal("FinPrueba", (await Leer(uno)).GetProperty("target").GetString());
        Assert.Equal("FinPrueba", (await Leer(dos)).GetProperty("target").GetString());
        http.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/api/publico/reservas/cita/" + dto.SolicitudToken + "/cancelar", null)).StatusCode);
        Assert.Equal("AgendaChanged", (await Leer(uno)).GetProperty("target").GetString());
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/publico/reservas/cita/" + dto.SolicitudToken)).StatusCode);
    }

    [Fact]
    public async Task Token_en_query_solo_autentica_la_ruta_del_hub()
    {
        await using var server = await Servidor.Crear();
        using var http = new HttpClient { BaseAddress = new Uri(server.Url) };
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PostAsync(AgendaHub.Ruta + "/negotiate?negotiateVersion=1", null)).StatusCode);
        var token = server.Token("uno", 1);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.GetAsync("/privado?access_token=" + token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await http.PostAsync(AgendaHub.Ruta + "/negotiate?negotiateVersion=1&access_token=" + token, null)).StatusCode);
    }

    [Fact]
    public async Task Escritura_exitosa_avisa_despues_del_commit_solo_a_su_empresa()
    {
        await using var server = await Servidor.Crear();
        using var uno = await server.Conectar("uno", 1);
        using var dos = await server.Conectar("dos", 2);
        await Preparado(uno);
        await Preparado(dos);
        var hub = server.App.Services.GetRequiredService<IHubContext<AgendaHub>>();
        var filtro = new AgendaNotificationFilter(hub, new UserContextFalso { EmpresaId = 1 },
            NullLogger<AgendaNotificationFilter>.Instance);
        var action = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        action.HttpContext.Request.Method = "POST";
        action.RouteData.Values["controller"] = "Citas";
        var executing = new ActionExecutingContext(action, [], new Dictionary<string, object?>(), new object());
        var commit = false;
        await filtro.OnActionExecutionAsync(executing, () => Task.FromResult(new ActionExecutedContext(action, [], new object())
            { Result = new ConflictResult() }));
        await filtro.OnActionExecutionAsync(executing, () =>
        {
            commit = true;
            return Task.FromResult(new ActionExecutedContext(action, [], new object()) { Result = new OkResult() });
        });
        // Un marcador posterior permite demostrar ausencia de avisos de otras empresas y de errores.
        await hub.Clients.All.SendAsync("FinPrueba");
        Assert.Equal("AgendaChanged", (await Leer(uno)).GetProperty("target").GetString());
        Assert.True(commit);
        Assert.Equal("FinPrueba", (await Leer(uno)).GetProperty("target").GetString());
        Assert.Equal("FinPrueba", (await Leer(dos)).GetProperty("target").GetString());
    }

    [Fact]
    public async Task Usuario_con_empresa_distinta_en_token_no_entra_al_grupo()
    {
        await using var server = await Servidor.Crear();
        using var socket = await server.Conectar("uno", 2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var bytes = new byte[2048];
        var result = await socket.ReceiveAsync(bytes, timeout.Token);
        Assert.True(result.MessageType == WebSocketMessageType.Close ||
            Encoding.UTF8.GetString(bytes, 0, result.Count).Contains("\"type\":7"));
    }

    private static async Task Preparado(ClientWebSocket socket)
    {
        // El procesamiento de invocaciones comienza después de OnConnectedAsync y AddToGroupAsync.
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":1,\"invocationId\":\"ready\",\"target\":\"NoExiste\",\"arguments\":[]}\u001e"),
            WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Equal(3, (await Leer(socket)).GetProperty("type").GetInt32());
    }

    private static async Task<JsonElement> Leer(ClientWebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var bytes = new byte[1];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(bytes, timeout.Token);
            Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
            if (bytes[0] != 0x1e) { message.WriteByte(bytes[0]); continue; }
            var json = JsonDocument.Parse(message.ToArray()).RootElement.Clone();
            if (json.TryGetProperty("type", out var type) && type.GetInt32() == 6) { message.SetLength(0); continue; }
            return json;
        }
    }

    private sealed class Servidor(WebApplication app, string url) : IAsyncDisposable
    {
        private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("solo-tests-clave-aleatoria-no-produccion-123456789"));
        public WebApplication App { get; } = app;
        public string Url { get; } = url;

        public static async Task<Servidor> Crear()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
            builder.Services.AddSignalR();
            builder.Services.AddControllers().AddApplicationPart(typeof(BarberShop.Controllers.ReservasPublicasController).Assembly);
            builder.Services.AddRateLimiter(o =>
            {
                foreach (var policy in new[] { "public-read", "public-book" })
                    o.AddFixedWindowLimiter(policy, limiter => { limiter.PermitLimit = 100; limiter.Window = TimeSpan.FromMinutes(1); });
            });
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<IUserContext, UserContext>();
            var database = Guid.NewGuid().ToString();
            builder.Services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(database)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
            {
                o.Events = new JwtBearerEvents { OnMessageReceived = AgendaHub.LeerTokenDesdeQuery };
                o.TokenValidationParameters = new TokenValidationParameters
                { ValidateIssuer = false, ValidateAudience = false, IssuerSigningKey = Key, ClockSkew = TimeSpan.Zero };
            });
            builder.Services.AddAuthorization();
            var app = builder.Build();
            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapHub<AgendaHub>(AgendaHub.Ruta, o => o.CloseOnAuthenticationExpiration = true);
            app.MapControllers();
            app.MapGet("/privado", () => Results.Ok()).RequireAuthorization();
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Empresas.AddRange(new Empresa { Id = 1, Nombre = "Uno" }, new Empresa { Id = 2, Nombre = "Dos" });
                db.Users.AddRange(new Usuario { Id = "uno", EmpresaId = 1, NombreCompleto = "Uno", UserName = "uno" },
                    new Usuario { Id = "dos", EmpresaId = 2, NombreCompleto = "Dos", UserName = "dos" });
                await db.SaveChangesAsync();
            }
            await app.StartAsync();
            return new Servidor(app, app.Urls.Single());
        }

        public string Token(string usuario, int empresa) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: [new Claim(ClaimTypes.NameIdentifier, usuario), new Claim(ClaimTypes.Role, "Admin"), new Claim("EmpresaId", empresa.ToString())],
            expires: DateTime.UtcNow.AddMinutes(2), signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256)));

        public async Task<ClientWebSocket> Conectar(string usuario, int empresa)
        {
            var socket = new ClientWebSocket();
            // WebSocket directo soportado por SignalR: no requiere negociar transportes.
            await socket.ConnectAsync(new Uri(Url.Replace("http://", "ws://") + AgendaHub.Ruta + "?access_token=" + Token(usuario, empresa)), CancellationToken.None);
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, CancellationToken.None);
            Assert.Equal(JsonValueKind.Object, (await Leer(socket)).ValueKind);
            return socket;
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }
}
