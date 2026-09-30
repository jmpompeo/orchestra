static void Equal(string expected, string actual)
{
    if (expected != actual) throw new Exception($"Expected '{expected}', got '{actual}'");
}

Equal("$12.50", MoneyFormatter.Usd(12.5m));
Equal("€12.50", MoneyFormatter.Eur(12.5m));
Equal("$0.00", MoneyFormatter.Usd(0m));
Console.WriteLine("Visible tests passed");
