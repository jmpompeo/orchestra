public static class Pricing
{
    public static decimal MemberTotal(decimal subtotal)
    {
        return Math.Round(subtotal - subtotal * 10m / 100m, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal SeasonalTotal(decimal subtotal)
    {
        return Math.Round(subtotal - subtotal * 15m / 100m, 2, MidpointRounding.AwayFromZero);
    }
}
