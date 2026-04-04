using System;
using System.Collections.Generic;

namespace ZUI
{
    public class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Default = new();

        public int Compare(string? x, string? y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    int numX = 0, numY = 0;
                    while (i < x.Length && char.IsDigit(x[i]))
                    {
                        numX = numX * 10 + (x[i] - '0');
                        i++;
                    }
                    while (j < y.Length && char.IsDigit(y[j]))
                    {
                        numY = numY * 10 + (y[j] - '0');
                        j++;
                    }
                    if (numX != numY)
                        return numX.CompareTo(numY);
                }
                else
                {
                    int cmp = char.ToLowerInvariant(x[i]).CompareTo(char.ToLowerInvariant(y[j]));
                    if (cmp != 0)
                        return cmp;
                    i++;
                    j++;
                }
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
