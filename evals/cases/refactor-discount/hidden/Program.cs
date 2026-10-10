static void Equal(decimal expected, decimal actual)
{
    if (expected != actual) throw new Exception($"Expected {expected}, got {actual}");
}

Equal(0m, Pricing.MemberTotal(0m));
Equal(0m, Pricing.SeasonalTotal(0m));
Equal(0.90m, Pricing.MemberTotal(1m));
Equal(0.85m, Pricing.SeasonalTotal(1m));
Equal(-9m, Pricing.MemberTotal(-10m));
Equal(-8.5m, Pricing.SeasonalTotal(-10m));
Console.WriteLine("Hidden tests passed");
