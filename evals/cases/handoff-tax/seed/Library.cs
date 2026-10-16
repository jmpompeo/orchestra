public static class TaxTotal
{
    public static decimal StandardTotal(decimal subtotal)
    {
        return Math.Round(subtotal + subtotal * 10m / 100m, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal PriorityTotal(decimal subtotal)
    {
        return Math.Round(subtotal + subtotal * 15m / 10m, 2, MidpointRounding.AwayFromZero);
    }
}
