static void Equal(string expected, string actual)
{
    if (expected != actual) throw new Exception($"Expected '{expected}', got '{actual}'");
}

Equal("mixed-name", Slug.Normalize("\tMiXeD Name\n"));
Equal("x", Slug.Normalize("X"));
Equal("", Slug.Normalize("  "));
Console.WriteLine("Hidden tests passed");
