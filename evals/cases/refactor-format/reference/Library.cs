using System.Globalization;

public static class MoneyFormatter
{
    public static string Usd(decimal amount) => Format(amount, "$");

    public static string Eur(decimal amount) => Format(amount, "€");

    private static string Format(decimal amount, string symbol)
    {
        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("0.00", CultureInfo.InvariantCulture);
        return symbol + text;
    }
}
