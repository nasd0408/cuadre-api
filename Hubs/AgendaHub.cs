using BarberShop.Data;
using BarberShop.Enum;
using BarberShop.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Hubs;

[Authorize(Roles = "Admin,Barbero,SuperAdmin")]
public class AgendaHub(AppDbContext db, IUserContext usuario) : Hub
{
    public const string Ruta = "/api/hubs/agenda";
    public static string Grupo(int empresaId) => $"agenda:empresa:{empresaId}";

    public static Task LeerTokenDesdeQuery(MessageReceivedContext context)
    {
        if (context.Request.Path.StartsWithSegments(Ruta) &&
            context.Request.Query.TryGetValue("access_token", out var token)) context.Token = token;
        return Task.CompletedTask;
    }

    public override async Task OnConnectedAsync()
    {
        if (usuario.EmpresaId is not > 0 || string.IsNullOrEmpty(usuario.UsuarioId) ||
            !await db.Users.AnyAsync(u => u.Id == usuario.UsuarioId && u.EmpresaId == usuario.EmpresaId, Context.ConnectionAborted) ||
            !await db.Empresas.AnyAsync(e => e.Id == usuario.EmpresaId && e.Estado != EstadoSuscripcion.Suspendido &&
                (e.FechaBloqueo == null || e.FechaBloqueo > DateTime.UtcNow), Context.ConnectionAborted))
        {
            Context.Abort();
            throw new HubException("No hay acceso a la agenda de esta empresa.");
        }
        // El cliente no puede elegir ni cambiar de grupo.
        await Groups.AddToGroupAsync(Context.ConnectionId, Grupo(usuario.EmpresaId.Value), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}
