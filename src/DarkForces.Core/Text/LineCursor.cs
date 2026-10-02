namespace DarkForces.Core.Text;

/// <summary>Forward-only cursor over comment-stripped lines, with error messages that name the line.</summary>
public sealed class LineCursor(IReadOnlyList<string> lines, string fileKind)
{
    int _pos;

    public bool Eof => _pos >= lines.Count;
    public string Peek() => Eof ? "" : lines[_pos];
    public string[] PeekTokens() => DfText.Tokens(Peek());
    public string Next() => Eof ? throw Error("unexpected end of file") : lines[_pos++];
    public string[] NextTokens() => DfText.Tokens(Next());

    /// <summary>True if the next line's first token equals <paramref name="keyword"/> (case-insensitive).</summary>
    public bool At(string keyword)
    {
        if (Eof) return false;
        var t = PeekTokens();
        return t.Length > 0 && DfText.KeyIs(t[0], keyword);
    }

    /// <summary>Consumes a line that starts with <paramref name="keyword"/> and returns the tokens after it.</summary>
    public string[] Expect(string keyword)
    {
        if (!At(keyword))
            throw Error($"expected '{keyword}' but found '{Peek()}'");
        return NextTokens()[1..];
    }

    /// <summary>Remaining lines, joined; used to report junk after the last parsed element.</summary>
    public string Rest() => string.Join(DfText.NewLine, lines.Skip(_pos));

    public InvalidDataException Error(string message) =>
        new($"{fileKind}: {message} (line {_pos + 1} of comment-stripped text)");
}
