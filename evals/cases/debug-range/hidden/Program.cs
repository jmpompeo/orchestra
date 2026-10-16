static void Equal(bool expected, bool actual)
{
    if (expected != actual) throw new Exception($"Expected {expected}, got {actual}");
}

Equal(false, new NumberRange(2, 5).Contains(1));
Equal(false, new NumberRange(0, 0).Contains(0));
Equal(true, new NumberRange(-3, 0).Contains(-1));
Equal(false, new NumberRange(-3, 0).Contains(0));
Console.WriteLine("Hidden tests passed");
