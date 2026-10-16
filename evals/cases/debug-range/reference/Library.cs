public readonly record struct NumberRange(int Start, int End)
{
    // Intervals are half-open: [Start, End).
    public bool Contains(int value) => value >= Start && value < End;
}
