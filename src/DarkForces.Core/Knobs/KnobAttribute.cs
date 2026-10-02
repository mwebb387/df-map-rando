namespace DarkForces.Core.Knobs;

/// <summary>
/// Declares a settings property as a user-facing knob (docs/knobs.md). The catalog, CLI help, preset
/// validation and documentation all read this one declaration.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class KnobAttribute(string description) : Attribute
{
    public string Description { get; } = description;

    /// <summary>Inclusive lower bound for numeric knobs (NaN = none).</summary>
    public double Min { get; init; } = double.NaN;

    /// <summary>Inclusive upper bound for numeric knobs (NaN = none).</summary>
    public double Max { get; init; } = double.NaN;

    /// <summary>Allowed values for a string knob (case-insensitive); null = free text.</summary>
    public string[]? Choices { get; init; }

    /// <summary>Knob schema version this knob appeared in.</summary>
    public int Since { get; init; } = 1;
}

/// <summary>Marks a settings class as a knob section; its properties' paths are <c>section.knob</c>.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class KnobSectionAttribute : Attribute;
