using System.ComponentModel.DataAnnotations;

namespace BarberShop.Models
{
    /// <summary>
    /// Token de notificaciones push (Expo) de un dispositivo de un usuario.
    /// No es BaseEntity: el job en segundo plano necesita consultar todos los
    /// tokens de todas las empresas sin el filtro de tenant.
    /// </summary>
    public class DispositivoPush
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public virtual Usuario? Usuario { get; set; }

        public int EmpresaId { get; set; }

        /// <summary>ExpoPushToken del dispositivo (ExponentPushToken[...]).</summary>
        [Required]
        [StringLength(255)]
        public string Token { get; set; } = string.Empty;

        [StringLength(20)]
        public string Plataforma { get; set; } = "android";

        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
        public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
    }
}
