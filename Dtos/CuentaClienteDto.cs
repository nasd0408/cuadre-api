namespace BarberShop.Dtos
{
    public class CrearAbonoDto
    {
        public int ClienteId { get; set; }
        public int? CitaId { get; set; }
        public decimal Monto { get; set; }
        public string? Observaciones { get; set; }
    }

    public class CrearCargoDto
    {
        public int ClienteId { get; set; }
        public int? CitaId { get; set; }
        public decimal Monto { get; set; }
        public string? Observaciones { get; set; }
    }

}
