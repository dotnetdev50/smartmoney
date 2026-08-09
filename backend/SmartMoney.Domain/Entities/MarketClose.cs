namespace SmartMoney.Domain.Entities;

public sealed class MarketClose
{
    public DateTime Date { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public double Close { get; set; }
}