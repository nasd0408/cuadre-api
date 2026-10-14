// Helpers/PlanDefaults.cs
using BarberShop.Enum;
using BarberShop.Models;

namespace BarberShop.Helpers
{
    /// <summary>
    /// Fuente unica de verdad de "que incluye cada plan" (limites y modulos).
    /// Es una clase static: no guarda estado, solo funciones puras, lo que la
    /// hace facil de testear (ver BarberShop.Tests/PlanDefaultsTests.cs).
    /// Convencion: -1 significa "ilimitado".
    /// </summary>
    public static class PlanDefaults
    {
        /// <summary>
        /// Crea una EmpresaConfig con los valores por defecto del plan indicado.
        /// </summary>
        public static EmpresaConfig CrearConfig(int empresaId, PlanEmpresa plan)
        {
            return plan switch
            {
                // Plan gratuito permanente: monetizado con anuncios y sin acceso web.
                // MaxCitasOffline se mantiene igual que Básico a propósito: bajarlo dejaría
                // citas ya encoladas imposibles de sincronizar al degradar de plan
                // (ValidarCitasOfflineAsync rechaza el batch completo).
                PlanEmpresa.Gratis => new EmpresaConfig
                {
                    EmpresaId = empresaId,
                    Plan = PlanEmpresa.Gratis,
                    MaxEmpleados = 1,
                    MaxServicios = 3,
                    MaxCitasOffline = 5,
                    RetencionHistorialMeses = 1,
                    ModuloPagosEmpleados = false,
                    ModuloReportes = false,
                    ModuloRecordatorios = false,
                    ModuloCalendario = false,
                    MostrarAnuncios = true,
                    AccesoWeb = false,
                },

                PlanEmpresa.Basico => new EmpresaConfig
                {
                    EmpresaId = empresaId,
                    Plan = PlanEmpresa.Basico,
                    MaxEmpleados = 1,
                    MaxServicios = 5,
                    MaxCitasOffline = 5,
                    RetencionHistorialMeses = 3,
                    ModuloPagosEmpleados = false,
                    ModuloReportes = false,
                    ModuloRecordatorios = false,
                    ModuloCalendario = false,
                    MostrarAnuncios = false,
                    AccesoWeb = true,
                },

                PlanEmpresa.Profesional => new EmpresaConfig
                {
                    EmpresaId = empresaId,
                    Plan = PlanEmpresa.Profesional,
                    MaxEmpleados = 5,
                    MaxServicios = 20,
                    MaxCitasOffline = 15,
                    RetencionHistorialMeses = 12,
                    ModuloPagosEmpleados = true,
                    ModuloReportes = true,
                    ModuloRecordatorios = true,
                    ModuloCalendario = true,
                    MostrarAnuncios = false,
                    AccesoWeb = true,
                },

                PlanEmpresa.Premium => new EmpresaConfig
                {
                    EmpresaId = empresaId,
                    Plan = PlanEmpresa.Premium,
                    MaxEmpleados = -1,
                    MaxServicios = -1,
                    MaxCitasOffline = 50,
                    RetencionHistorialMeses = -1,
                    ModuloPagosEmpleados = true,
                    ModuloReportes = true,
                    ModuloRecordatorios = true,
                    ModuloCalendario = true,
                    MostrarAnuncios = false,
                    AccesoWeb = true,
                },

                _ => throw new ArgumentException($"Plan no reconocido: {plan}")
            };
        }

        /// <summary>
        /// Aplica todos los valores propios de un plan a una configuración existente.
        /// Centralizar esta transición evita olvidar campos cuando se añade una
        /// capacidad nueva, como anuncios o acceso web.
        /// </summary>
        public static void AplicarA(EmpresaConfig destino, PlanEmpresa plan)
        {
            var defaults = CrearConfig(destino.EmpresaId, plan);

            destino.Plan = defaults.Plan;
            destino.MaxEmpleados = defaults.MaxEmpleados;
            destino.MaxServicios = defaults.MaxServicios;
            destino.MaxCitasOffline = defaults.MaxCitasOffline;
            destino.RetencionHistorialMeses = defaults.RetencionHistorialMeses;
            destino.ModuloPagosEmpleados = defaults.ModuloPagosEmpleados;
            destino.ModuloReportes = defaults.ModuloReportes;
            destino.ModuloRecordatorios = defaults.ModuloRecordatorios;
            destino.ModuloCalendario = defaults.ModuloCalendario;
            destino.MostrarAnuncios = defaults.MostrarAnuncios;
            destino.AccesoWeb = defaults.AccesoWeb;
            destino.FechaActualizacion = DateTime.UtcNow;
        }
    }
}
