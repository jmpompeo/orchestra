public static class Slug
{
    public static string Normalize(string input) => input.Trim().ToLowerInvariant().Replace(" ", "-");
}
