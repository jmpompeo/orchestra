static void Equal(decimal expected, decimal actual)
{
    if (expected != actual) throw new Exception($"Expected {expected}, got {actual}");
}

Equal(90.00m, Pricing.MemberTotal(100m));
Equal(85.00m, Pricing.SeasonalTotal(100m));
Equal(9.00m, Pricing.MemberTotal(10m));
Console.WriteLine("Visible tests passed");
