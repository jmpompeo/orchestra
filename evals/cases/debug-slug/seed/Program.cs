static void Equal(string expected, string actual)
{
    if (expected != actual) throw new Exception($"Expected '{expected}', got '{actual}'");
}

Equal("hello-world", Slug.Normalize("Hello World"));
Equal("hello-world", Slug.Normalize("  Hello World  "));
Console.WriteLine("Visible tests passed");
