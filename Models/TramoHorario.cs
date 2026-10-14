namespace BarberShop.Models;

public class TramoHorario : BaseEntity
{
    public int EmpleadoId { get; set; }
    // 0 = domingo, 6 = sábado. Los extremos son minutos desde medianoche.
    public int DiaSemana { get; set; }
    public int InicioMinuto { get; set; }
    public int FinMinuto { get; set; }
}
