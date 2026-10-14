using System.ComponentModel.DataAnnotations;

namespace BarberShop.Models
{
    public class CodigoVerificacion
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [StringLength(6)]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        public TipoCodigoVerificacion Tipo { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

        public DateTime FechaExpiracion { get; set; }

        public bool Usado { get; set; } = false;

        // Intentos fallidos de validación; el código se invalida al llegar al máximo
        public int IntentosFallidos { get; set; } = 0;

        // Navigation
        public virtual Usuario? Usuario { get; set; }
    }

    public enum TipoCodigoVerificacion
    {
        ConfirmacionEmail,
        ResetPassword
    }
}