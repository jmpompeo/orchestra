static void Equal(decimal expected, decimal actual)
{
    if (expected != actual) throw new Exception($"Expected {expected}, got {actual}");
}

Equal(0m, TaxTotal.StandardTotal(0m));
Equal(0m, TaxTotal.PriorityTotal(0m));
Equal(1.1m, TaxTotal.StandardTotal(1m));
Equal(1.15m, TaxTotal.PriorityTotal(1m));
Equal(-11m, TaxTotal.StandardTotal(-10m));
Equal(-11.5m, TaxTotal.PriorityTotal(-10m));
Console.WriteLine("Hidden tests passed");
