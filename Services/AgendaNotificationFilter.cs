using BarberShop.Hubs;
using BarberShop.Interfaces;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.SignalR;

namespace BarberShop.Services;

// El filtro se ejecuta al retornar la acción: SaveChanges/Commit ya finalizaron.
public class AgendaNotificationFilter(IHubContext<AgendaHub> hub, IUserContext usuario,
    ILogger<AgendaNotificationFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var result = await next();
        var controller = context.RouteData.Values["controller"]?.ToString();
        var status = (result.Result as IStatusCodeActionResult)?.StatusCode ?? 200;
        if (result.Exception is not null || result.Canceled || status is < 200 or >= 300 ||
            HttpMethods.IsGet(context.HttpContext.Request.Method) ||
            controller is not ("Citas" or "Horarios" or "Ausencias" or "Sync" or "Empleados" or "Servicios") ||
            usuario.EmpresaId is not > 0) return;
        await Notificar(hub, logger, usuario.EmpresaId.Value);
    }

    public static async Task Notificar(IHubContext<AgendaHub> hub, ILogger logger, int empresaId)
    {
        try
        {
            await hub.Clients.Group(AgendaHub.Grupo(empresaId))
                .SendAsync("AgendaChanged", new { fechaUtc = DateTime.UtcNow })
                .WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            // Un fallo del canal en vivo no convierte una operación ya guardada en error.
            logger.LogWarning(ex, "No se pudo avisar el cambio de agenda de empresa {EmpresaId}", empresaId);
        }
    }
}
