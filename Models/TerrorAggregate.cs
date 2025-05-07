namespace ToNRoundCounter.Models
{
    public class TerrorAggregate
    {
        public int Total { get; set; }
        public int Death { get; set; }
        public int Survival { get; set; }
        public double SurvivalRate => Total == 0 ? 0 : (Survival * 100.0 / Total);
    }
}
