using System.ComponentModel.DataAnnotations;

namespace BarberShop.Models
{
    public class RefreshToken
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public virtual Usuario? Usuario { get; set; }

        // Se guarda el hash SHA-256 del token, nunca el token en claro
        [Required]
        [StringLength(64)]
        public string TokenHash { get; set; } = string.Empty;

        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

        public DateTime FechaExpiracion { get; set; }

        public DateTime? FechaRevocacion { get; set; }

        public bool EstaActivo => FechaRevocacion == null && DateTime.UtcNow < FechaExpiracion;
    }
}
