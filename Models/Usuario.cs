using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace BarberShop.Models
{
    public class Usuario : IdentityUser
    {
        [Required]
        public int EmpresaId { get; set; }

        [JsonIgnore]
        public Empresa Empresa { get; set; } = null!;

        [Required]
        [StringLength(150)]
        public string NombreCompleto { get; set; } = null!;

        public int? EmpleadoId { get; set; }

        [JsonIgnore]
        public Empleado? Empleado { get; set; }
    }
}
