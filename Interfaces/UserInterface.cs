namespace BarberShop.Interfaces
{
    /// <summary>
    /// Datos del usuario que hace la peticion actual, sacados del JWT.
    /// Lo consumen AppDbContext (filtro multi-tenant) y los controladores.
    /// </summary>
    public interface IUserContext
    {
        /// <summary>Empresa (tenant) del usuario; null si no hay sesion.</summary>
        int? EmpresaId { get; }
        /// <summary>Id del usuario de Identity (AspNetUsers.Id); vacio si no hay sesion.</summary>
        string UsuarioId { get; }
        /// <summary>true si el usuario tiene el rol SuperAdmin (ve todas las empresas).</summary>
        bool IsSuperAdmin { get; }
    }
}
