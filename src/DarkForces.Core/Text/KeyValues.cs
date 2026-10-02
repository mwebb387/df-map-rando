namespace DarkForces.Core.Text;

/// <summary>
/// Splits a token run like <c>LEFT: 0 RIGHT: 1 MID: 5 0.00 0.00 0</c> into ordered (key, values) pairs.
/// A key is any token ending in ':'; keys compare case-insensitively.
/// </summary>
public sealed class KeyValues
{
    readonly List<(string Key, List<string> Values)> _pairs = [];

    public KeyValues(IEnumerable<string> tokens)
    {
        foreach (var t in tokens)
        {
            if (t.Length > 1 && t[^1] == ':')
                _pairs.Add((t[..^1], []));
            else if (_pairs.Count > 0)
                _pairs[^1].Values.Add(t);
            else
                Leading.Add(t);
        }
    }

    /// <summary>Tokens that appeared before the first key.</summary>
    public List<string> Leading { get; } = [];

    public IEnumerable<(string Key, List<string> Values)> Pairs => _pairs;

    public bool Has(string key) => _pairs.Any(p => DfText.KeyIs(p.Key, key));

    public List<string>? Get(string key) =>
        _pairs.FirstOrDefault(p => DfText.KeyIs(p.Key, key)).Values;

    public List<string> Require(string key, string context) =>
        Get(key) ?? throw new InvalidDataException($"{context}: missing '{key}:'");
}
