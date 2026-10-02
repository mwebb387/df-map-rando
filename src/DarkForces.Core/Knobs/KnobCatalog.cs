using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DarkForces.Core.Knobs;

/// <summary>One declared knob: its dotted path, type, default and constraints.</summary>
public sealed record KnobInfo(string Path, Type Type, object? Default, KnobAttribute Declaration)
{
    public string TypeName => Type == typeof(bool) ? "bool" : Type == typeof(int) ? "int" : Type == typeof(double) ? "number" : "string";

    public string Range => Declaration.Choices is { } c ? string.Join("|", c) :
        (double.IsNaN(Declaration.Min) && double.IsNaN(Declaration.Max) ? ""
        : $"{(double.IsNaN(Declaration.Min) ? "" : Declaration.Min.ToString(CultureInfo.InvariantCulture))}..{(double.IsNaN(Declaration.Max) ? "" : Declaration.Max.ToString(CultureInfo.InvariantCulture))}");
}

/// <summary>Reflects the knob declarations on <see cref="RandomizerSettings"/>; loads, overrides and validates settings.</summary>
public static class KnobCatalog
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    static readonly Lazy<List<(KnobInfo Info, PropertyInfo Section, PropertyInfo Prop)>> Entries = new(Build);

    public static IReadOnlyList<KnobInfo> All => [.. Entries.Value.Select(e => e.Info)];

    static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    static List<(KnobInfo, PropertyInfo, PropertyInfo)> Build()
    {
        var defaults = new RandomizerSettings();
        var list = new List<(KnobInfo, PropertyInfo, PropertyInfo)>();
        foreach (var section in typeof(RandomizerSettings).GetProperties().Where(p => p.IsDefined(typeof(KnobSectionAttribute))))
        {
            var sectionDefault = section.GetValue(defaults);
            foreach (var prop in section.PropertyType.GetProperties())
            {
                if (prop.GetCustomAttribute<KnobAttribute>() is not { } k) continue;
                var path = $"{Camel(section.Name)}.{Camel(prop.Name)}";
                list.Add((new KnobInfo(path, prop.PropertyType, prop.GetValue(sectionDefault), k), section, prop));
            }
        }
        return list;
    }

    public static RandomizerSettings LoadPreset(string json)
    {
        var s = JsonSerializer.Deserialize<RandomizerSettings>(json, Json) ?? new RandomizerSettings();
        if (s.Schema > RandomizerSettings.CurrentSchema)
            throw new InvalidDataException($"preset schema {s.Schema} is newer than this build ({RandomizerSettings.CurrentSchema})");
        return s;
    }

    public static string ToJson(RandomizerSettings s) => JsonSerializer.Serialize(s, Json);

    /// <summary>Applies an override such as <c>layout.roomCount=5</c> or <c>seed=42</c>.</summary>
    public static void Set(RandomizerSettings s, string assignment)
    {
        var eq = assignment.IndexOf('=');
        if (eq <= 0)
            throw new ArgumentException($"expected knob=value, got '{assignment}'");
        var path = assignment[..eq].Trim();
        var value = assignment[(eq + 1)..].Trim();

        if (path.Equals("seed", StringComparison.OrdinalIgnoreCase))
        {
            s.Seed = ParseSeed(value);
            return;
        }
        var entry = Entries.Value.FirstOrDefault(e => e.Info.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (entry.Info == null)
            throw new ArgumentException($"unknown knob '{path}' (see: dftool knobs)");
        entry.Prop.SetValue(entry.Section.GetValue(s), Parse(entry.Info, value));
    }

    /// <summary>Seeds are unsigned 64-bit; any other text is hashed (FNV-1a) so words work as seeds too.</summary>
    public static ulong ParseSeed(string value)
    {
        if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return n;
        var h = 14695981039346656037UL;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(value))
            h = (h ^ b) * 1099511628211UL;
        return h;
    }

    static object Parse(KnobInfo k, string v)
    {
        try
        {
            if (k.Type == typeof(bool)) return bool.Parse(v);
            if (k.Type == typeof(int)) return int.Parse(v, CultureInfo.InvariantCulture);
            if (k.Type == typeof(double)) return double.Parse(v, CultureInfo.InvariantCulture);
            return v;
        }
        catch (FormatException)
        {
            throw new ArgumentException($"{k.Path}: '{v}' is not a valid {k.TypeName}");
        }
    }

    public static object? Get(RandomizerSettings s, KnobInfo k)
    {
        var e = Entries.Value.First(x => x.Info.Path == k.Path);
        return e.Prop.GetValue(e.Section.GetValue(s));
    }

    /// <summary>Range checks from the declarations. Returns messages naming the knob; empty if valid.</summary>
    public static List<string> Validate(RandomizerSettings s)
    {
        var errors = new List<string>();
        foreach (var k in All)
        {
            if (k.Declaration.Choices is { } choices && Get(s, k) is string str &&
                !choices.Contains(str, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{k.Path} = '{str}' must be one of: {string.Join(", ", choices)}");
            if (Get(s, k) is not IConvertible c || k.Type == typeof(string) || k.Type == typeof(bool)) continue;
            var v = c.ToDouble(CultureInfo.InvariantCulture);
            if (!double.IsNaN(k.Declaration.Min) && v < k.Declaration.Min)
                errors.Add($"{k.Path} = {v} is below the minimum {k.Declaration.Min}");
            if (!double.IsNaN(k.Declaration.Max) && v > k.Declaration.Max)
                errors.Add($"{k.Path} = {v} is above the maximum {k.Declaration.Max}");
        }
        if (s.Layout.HallwayMinLength > s.Layout.HallwayMaxLength)
            errors.Add($"layout.hallwayMinLength ({s.Layout.HallwayMinLength}) is above layout.hallwayMaxLength ({s.Layout.HallwayMaxLength})");
        var p = s.Progression;
        if (p.LockedDoorsMin > p.LockedDoorsMax)
            errors.Add($"progression.lockedDoorsMin ({p.LockedDoorsMin}) is above progression.lockedDoorsMax ({p.LockedDoorsMax})");
        var colors = p.ParsedKeyColors();
        foreach (var c in colors.Where(c => !ProgressionSettings.AllKeyColors.Contains(c)))
            errors.Add($"progression.keyColors: '{c}' is not a key color ({string.Join(", ", ProgressionSettings.AllKeyColors)})");
        if (p.LockedDoors && p.LockedDoorsMax > 0 && colors.Count == 0)
            errors.Add("progression.keyColors is empty but progression.lockedDoors is on");
        if (s.Output.LevelSlot.Length is 0 or > 8)
            errors.Add("output.levelSlot must be a 1-8 character level name");
        return errors;
    }

    /// <summary>A compact JSON form of the seed and every knob, for embedding in the output.</summary>
    public static string SettingsString(RandomizerSettings s)
    {
        var node = JsonSerializer.SerializeToNode(s, Json)!.AsObject();
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
