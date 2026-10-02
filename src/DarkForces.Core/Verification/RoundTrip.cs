using System.Globalization;
using DarkForces.Core.Gob;
using DarkForces.Core.Gol;
using DarkForces.Core.Inf;
using DarkForces.Core.Lev;
using DarkForces.Core.Lvl;
using DarkForces.Core.Objects;
using DarkForces.Core.Text;

namespace DarkForces.Core.Verification;

public enum RoundTripStatus { Ok, Mismatch, Error, Unsupported }

/// <param name="Trailing">Unparsed text after the last element of the source (comment-stripped), if any.</param>
public sealed record RoundTripResult(string File, RoundTripStatus Status, string Detail = "", string Trailing = "")
{
    public override string ToString() =>
        $"{Status,-11} {File}" + (Detail.Length > 0 ? $"  {Detail}" : "") +
        (Trailing.Length > 0 ? $"  [ignored trailing: {Trailing.Replace("\r\n", " ")}]" : "");
}

/// <summary>
/// Verifies that the format readers/writers preserve data:
/// GOBs must rewrite byte-identically; text files must parse, rewrite stably, and keep the
/// source's comment-free token stream (numbers compared by value, case-insensitive).
/// </summary>
public static class RoundTrip
{
    public static IEnumerable<RoundTripResult> CheckGob(string label, byte[] raw)
    {
        GobArchive gob;
        RoundTripResult? header;
        try
        {
            gob = GobArchive.Read(raw);
            header = gob.Write().AsSpan().SequenceEqual(raw)
                ? new RoundTripResult(label, RoundTripStatus.Ok, $"{gob.Entries.Count} entries, byte-identical")
                : new RoundTripResult(label, RoundTripStatus.Mismatch, "GOB rewrite differs from source");
        }
        catch (Exception ex)
        {
            return [new RoundTripResult(label, RoundTripStatus.Error, ex.Message)];
        }
        return gob.Entries
            .Select(e => CheckFile($"{label}/{e.Name}", e.Name, e.Data))
            .Where(r => r.Status != RoundTripStatus.Unsupported)
            .Prepend(header);
    }

    public static bool IsSupported(string name) => Codec(name) != null;

    /// <summary>Checks every GOB and supported loose file among <paramref name="paths"/> (directories are searched recursively).</summary>
    public static IEnumerable<RoundTripResult> CheckPaths(IEnumerable<string> paths)
    {
        var files = paths.SelectMany<string, string>(p => Directory.Exists(p)
            ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)
            : [p]);
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".GOB", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var r in CheckGob(file, File.ReadAllBytes(file)))
                    yield return r;
            }
            else if (IsSupported(name))
            {
                yield return CheckFile(file, name, File.ReadAllBytes(file));
            }
        }
    }

    public static RoundTripResult CheckFile(string label, string name, byte[] data)
    {
        var codec = Codec(name);
        if (codec == null)
            return new RoundTripResult(label, RoundTripStatus.Unsupported);
        try
        {
            var source = DfText.Decode(data);
            var (written, trailing, note) = codec(source);
            var (rewritten, _, _) = codec(written);
            if (rewritten != written)
                return new RoundTripResult(label, RoundTripStatus.Mismatch, "second write differs from first (writer not stable)", trailing);

            var expected = Tokens(source);
            var trailingCount = Tokens(trailing).Count;
            expected.RemoveRange(expected.Count - trailingCount, trailingCount);
            var actual = Tokens(written);
            var diff = FirstDifference(expected, actual);
            return diff == null
                ? new RoundTripResult(label, RoundTripStatus.Ok, $"{expected.Count} tokens{note}", trailing)
                : new RoundTripResult(label, RoundTripStatus.Mismatch, diff, trailing);
        }
        catch (Exception ex)
        {
            return new RoundTripResult(label, RoundTripStatus.Error, ex.Message);
        }
    }

    /// <summary>
    /// Parse-then-write for a file name; returns the written text, the parser's ignored trailing text,
    /// and a note about normalizations the writer applied.
    /// </summary>
    static Func<string, (string Written, string Trailing, string Note)>? Codec(string name)
    {
        var ext = Path.GetExtension(name).ToUpperInvariant();
        if (name.Equals("JEDI.LVL", StringComparison.OrdinalIgnoreCase))
            return s => { var f = JediLvl.Parse(s); return (f.Write(), f.Trailing, ""); };
        return ext switch
        {
            ".LEV" => s =>
            {
                var f = LevFile.Parse(s);
                return (f.Write(), f.Trailing, f.HadNonSequentialSectorLabels ? ", sector labels renumbered" : "");
            },
            ".O" => s => { var f = ObjFile.Parse(s); return (f.Write(), f.Trailing, ""); },
            ".INF" => s =>
            {
                var f = InfFile.Parse(s);
                return (f.Write(), f.Trailing,
                    f.DeclaredItemCount != f.Items.Count ? $", declared items {f.DeclaredItemCount} corrected to {f.Items.Count}" : "");
            },
            ".GOL" => s => { var f = GolFile.Parse(s); return (f.Write(), f.Trailing, ""); },
            _ => null,
        };
    }

    /// <summary>
    /// Comment-free tokens, with two values masked because the writers deliberately recompute them:
    /// the number on a <c>SECTOR n</c> line (a label the engine ignores; ADJOIN uses file position), and
    /// the INF <c>items n</c> count (stale in several stock levels, e.g. SECBASE declares 60 but has 58).
    /// </summary>
    static List<string> Tokens(string text) =>
        DfText.CleanLines(text)
            .Select(DfText.Tokens)
            .SelectMany(t => t is [var k, _] && (DfText.KeyIs(k, "SECTOR") || DfText.KeyIs(k, "items")) ? [k, "#"] : t)
            .Select(Normalize)
            .ToList();

    static string Normalize(string token)
    {
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return (d == 0 ? 0.0 : d).ToString("R", CultureInfo.InvariantCulture);
        return token.ToLowerInvariant();
    }

    static string? FirstDifference(List<string> expected, List<string> actual)
    {
        var n = Math.Min(expected.Count, actual.Count);
        for (var i = 0; i < n; i++)
            if (expected[i] != actual[i])
                return $"token {i}: source '{Context(expected, i)}' vs written '{Context(actual, i)}'";
        return expected.Count == actual.Count
            ? null
            : $"token count: source {expected.Count} vs written {actual.Count}; next: '{Context(expected.Count > n ? expected : actual, n)}'";
    }

    static string Context(List<string> t, int i) =>
        string.Join(' ', t.Skip(Math.Max(0, i - 4)).Take(9));
}
