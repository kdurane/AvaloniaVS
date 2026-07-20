using System.Collections.Generic;

namespace Avalonia.Ide.CompletionEngine.NamespaceTrasformations;

internal class ReplaceDot(char sobstituion) : INamespaceTrasformation
{
    public IEnumerable<char> Apply(IEnumerable<char> input)
    {
        foreach (char c in input)
        {
            if (c == '.')
                yield return sobstituion;
            else
            {
                yield return c;
            }
        }
    }
}
