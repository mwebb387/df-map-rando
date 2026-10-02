using System.Globalization;
using System.Text;

namespace DarkForces.Core.Text;

/// <summary>Shared helpers for DF's line-oriented text formats (LEV, O, INF, GOL, JEDI.LVL).</summary>
public static class DfText
{
    public static readonly Encoding Encoding = Encoding.Latin1;
    public const string NewLine = "\r\n";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Decode(ReadOnlySpan<byte> data)
    {
        var eof = data.IndexOf((byte)0x1A); // DOS EOF marker; anything after it is padding
        return Encoding.GetString(eof >= 0 ? data[..eof] : data);
    }

    public static byte[] Encode(string text) => Encoding.GetBytes(text);

    /// <summary>
    /// Splits text into lines with comments removed: <c>#</c> and <c>//</c> to end of line,
    /// <c>/* */</c> across lines. Blank lines are dropped.
    /// </summary>
    public static List<string> CleanLines(string text)
    {
        var lines = new List<string>();
        var sb = new StringBuilder();
        var inBlock = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (inBlock)
            {
                if (c == '*' && next == '/') { inBlock = false; i++; }
                else if (c == '\n') Flush();
                continue;
            }
            if (c == '/' && next == '*') { inBlock = true; i++; continue; }
            if (c == '#' || (c == '/' && next == '/'))
            {
                while (i + 1 < text.Length && text[i + 1] != '\n') i++;
                continue;
            }
            if (c == '\n') { Flush(); continue; }
            sb.Append(c == '\t' || c == '\r' ? ' ' : c);
        }
        Flush();
        return lines;

        void Flush()
        {
            var line = sb.ToString().Trim();
            if (line.Length > 0)
                lines.Add(line);
            sb.Clear();
        }
    }

    public static string[] Tokens(string line) =>
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public static double ParseDouble(string s) => double.Parse(s, NumberStyles.Float, Inv);
    public static int ParseInt(string s) => int.Parse(s, NumberStyles.Integer, Inv);
    public static long ParseLong(string s) => long.Parse(s, NumberStyles.Integer, Inv);

    /// <summary>Formats a coordinate/offset with at least two decimals and no precision loss.</summary>
    public static string F(double v) => (v == 0 ? 0.0 : v).ToString("0.00##########", Inv);

    public static string I(long v) => v.ToString(Inv);

    public static bool KeyIs(string token, string key) =>
        token.Equals(key, StringComparison.OrdinalIgnoreCase);
}
