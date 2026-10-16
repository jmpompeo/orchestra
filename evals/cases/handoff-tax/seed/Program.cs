static void Equal(decimal expected, decimal actual)
{
    if (expected != actual) throw new Exception($"Expected {expected}, got {actual}");
}

Equal(110m, TaxTotal.StandardTotal(100m));
Equal(115m, TaxTotal.PriorityTotal(100m));
Console.WriteLine("Visible tests passed");
