public static class TaxTotal
{
    public static decimal StandardTotal(decimal subtotal) => WithTax(subtotal, 10m);

    public static decimal PriorityTotal(decimal subtotal) => WithTax(subtotal, 15m);

    private static decimal WithTax(decimal subtotal, decimal rate)
    {
        return Math.Round(subtotal + subtotal * rate / 100m, 2, MidpointRounding.AwayFromZero);
    }
}
