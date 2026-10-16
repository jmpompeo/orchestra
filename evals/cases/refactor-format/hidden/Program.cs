static void Equal(string expected, string actual)
{
    if (expected != actual) throw new Exception($"Expected '{expected}', got '{actual}'");
}

Equal("$-1.25", MoneyFormatter.Usd(-1.25m));
Equal("€0.01", MoneyFormatter.Eur(0.01m));
Equal("$2.00", MoneyFormatter.Usd(1.999m));
Equal("€2.00", MoneyFormatter.Eur(1.999m));
Equal("$1.01", MoneyFormatter.Usd(1.005m));
Equal("€-1.01", MoneyFormatter.Eur(-1.005m));
Console.WriteLine("Hidden tests passed");
