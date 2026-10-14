using BarberShop.Enum;
using System.ComponentModel.DataAnnotations;

namespace BarberShop.Dtos
{
    // =====================
    // INICIO DE SESIÓN CON GOOGLE
    // =====================

    /// <summary>Paso 1: el cliente manda el ID token que le dio Google.</summary>
    public class GoogleAuthDto
    {
        [Required]
        public string IdToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// Paso 2 (solo usuarios nuevos): el mismo ID token más los datos del negocio
    /// que Google no puede aportar.
    ///
    /// Se reenvía el token en vez de emitir un ticket intermedio: los ID tokens de
    /// Google duran ~1 hora, así que alcanza de sobra para completar el formulario,
    /// y se vuelve a verificar del lado del servidor.
    /// </summary>
    public class GoogleRegisterDto
    {
        [Required]
        public string IdToken { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre del negocio es requerido")]
        [StringLength(100, MinimumLength = 3)]
        public string NombreBarberia { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Teléfono inválido")]
        public string? PhoneNumber { get; set; }

        [StringLength(200, ErrorMessage = "La dirección no puede superar los 200 caracteres")]
        public string? Direccion { get; set; }
    }

    // =====================
    // REGISTRO
    // =====================

    public class RegisterBusinessDto
    {
        [Required(ErrorMessage = "El nombre del negocio es requerido")]
        [StringLength(100, MinimumLength = 3)]
        public string NombreBarberia { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre completo es requerido")]
        [StringLength(100, MinimumLength = 3)]
        public string NombreCompleto { get; set; } = string.Empty;

        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress(ErrorMessage = "Email inválido")]
        public string Email { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Teléfono inválido")]
        public string? PhoneNumber { get; set; }

        [StringLength(200, ErrorMessage = "La dirección no puede superar los 200 caracteres")]
        public string? Direccion { get; set; }

        [Required(ErrorMessage = "La contraseña es requerida")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres")]
        public string Password { get; set; } = string.Empty;
    }

    // =====================
    // CONFIRMACIÓN EMAIL
    // =====================

    public class ConfirmEmailDto
    {
        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "El código es requerido")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "El código debe ser de 6 dígitos")]
        public string Codigo { get; set; } = string.Empty;
    }

    public class ResendCodeDto
    {
        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    // =====================
    // LOGIN
    // =====================

    public class LoginDto
    {
        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "La contraseña es requerida")]
        public string Password { get; set; } = string.Empty;
    }

    public class LoginResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime Expiration { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public IList<string> Roles { get; set; } = new List<string>();
        public string Empresa { get; set; } = string.Empty;
        public int EmpresaId { get; set; }
        public bool SubscriptionExpired { get; set; }
    }

    public class RefreshRequestDto
    {
        [Required]
        public string RefreshToken { get; set; } = string.Empty;
    }

    // =====================
    // RESET PASSWORD
    // =====================

    public class ForgotPasswordDto
    {
        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordDto
    {
        [Required(ErrorMessage = "El email es requerido")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "El código es requerido")]
        [StringLength(6, MinimumLength = 6)]
        public string Codigo { get; set; } = string.Empty;

        [Required(ErrorMessage = "La nueva contraseña es requerida")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres")]
        public string NewPassword { get; set; } = string.Empty;
    }

    // =====================
    // REGISTRO EMPLEADO (ya existente, lo mantenemos)
    // =====================

    public class RegisterEmployeeDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 6)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string NombreCompleto { get; set; } = string.Empty;

        public int? EmpleadoId { get; set; }
        public decimal? PorcentajeComision { get; set; }
        public PeriodicidadPago Periodicidad { get; set; }
    }
}
