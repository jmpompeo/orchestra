public static class Pricing
{
    public static decimal MemberTotal(decimal subtotal) => DiscountedTotal(subtotal, 10m);

    public static decimal SeasonalTotal(decimal subtotal) => DiscountedTotal(subtotal, 15m);

    private static decimal DiscountedTotal(decimal subtotal, decimal percent)
    {
        return Math.Round(subtotal - subtotal * percent / 100m, 2, MidpointRounding.AwayFromZero);
    }
}
