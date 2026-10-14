using BarberShop.Enum;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace BarberShop.Models
{
    public class Empresa
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [StringLength(150)]
        public string Nombre { get; set; } = null!;

        // NOT NULL en la base de datos, pero opcionales de hecho: 46 de 102
        // empresas tienen el telefono vacio y 48 la direccion. El valor de "sin
        // dato" es la cadena vacia, no null, asi que se inicializan como tal.
        [Required]
        public string Telefono { get; set; } = string.Empty;
        public string Direccion { get; set; } = string.Empty;

        [StringLength(500)]
        public string? LogoUrl { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

        public EstadoSuscripcion Estado { get; set; } = EstadoSuscripcion.Activo;

        public DateTime? FechaVencimiento { get; set; }

        public DateTime? FechaBloqueo { get; set; }
        public EmpresaConfig? Config { get; set; }


        // Relaciones (ignorar para evitar ciclos JSON)
        [JsonIgnore]
        public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();

        [JsonIgnore]
        public ICollection<Empleado> Empleados { get; set; } = new List<Empleado>();
    }
}
