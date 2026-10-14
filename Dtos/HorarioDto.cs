using System.ComponentModel.DataAnnotations;

namespace BarberShop.Dtos;

public class TramoHorarioDto
{
    [Range(0, 6)] public int DiaSemana { get; set; }
    [Range(0, 1439)] public int InicioMinuto { get; set; }
    [Range(1, 1440)] public int FinMinuto { get; set; }
}

public class HorarioDto : IValidatableObject
{
    public bool Activo { get; set; }
    [Required, MaxLength(100)] public string ZonaHoraria { get; set; } = "UTC";
    [Required, MaxLength(28)] public List<TramoHorarioDto> Tramos { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((ZonaHoraria != "UTC" && !ZonaHoraria.Contains('/')) ||
            !TimeZoneInfo.TryFindSystemTimeZoneById(ZonaHoraria, out _))
            yield return new ValidationResult("Usa UTC o una zona IANA válida, como Atlantic/Canary.", [nameof(ZonaHoraria)]);
        if (Tramos is null) yield break;
        foreach (var dia in Tramos.GroupBy(t => t.DiaSemana))
        {
            var finAnterior = -1;
            foreach (var tramo in dia.OrderBy(t => t.InicioMinuto))
            {
                if (tramo.InicioMinuto >= tramo.FinMinuto || tramo.InicioMinuto < finAnterior)
                    yield return new ValidationResult("Los turnos deben tener inicio anterior al fin y no superponerse.", [nameof(Tramos)]);
                finAnterior = tramo.FinMinuto;
            }
        }
    }
}

public record DisponibilidadDto(bool HorarioConfigurado, string ZonaHoraria, IReadOnlyList<DateTime> IniciosUtc);
