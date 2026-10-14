using System.ComponentModel.DataAnnotations;

namespace BarberShop.Dtos;

public class CrearAusenciaDto : IValidatableObject
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }
    [MaxLength(250)] public string? Motivo { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Desde.Year < 2000 || Hasta.Year > 2100 || Hasta < Desde || Hasta.DayNumber - Desde.DayNumber > 365)
            yield return new ValidationResult("Indica un rango válido de hasta 366 días, entre 2000 y 2100.", [nameof(Desde), nameof(Hasta)]);
    }
}
public record AusenciaDto(int Id, DateOnly Desde, DateOnly Hasta, string ZonaHoraria, string? Motivo);
