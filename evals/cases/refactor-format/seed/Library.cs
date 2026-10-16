using System.Globalization;

public static class MoneyFormatter
{
    public static string Usd(decimal amount)
    {
        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("0.00", CultureInfo.InvariantCulture);
        return "$" + text;
    }

    public static string Eur(decimal amount)
    {
        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("0.00", CultureInfo.InvariantCulture);
        return "€" + text;
    }
}
