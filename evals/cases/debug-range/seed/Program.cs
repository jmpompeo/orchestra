static void Equal(bool expected, bool actual)
{
    if (expected != actual) throw new Exception($"Expected {expected}, got {actual}");
}

var range = new NumberRange(2, 5);
Equal(true, range.Contains(2));
Equal(true, range.Contains(4));
Equal(false, range.Contains(5));
Console.WriteLine("Visible tests passed");
