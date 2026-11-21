using System.Text.RegularExpressions;
using WguLti.Domain;

namespace WguLti.Core.Substitution;

public sealed record SubstitutionValues(
    string? UserId,
    string? FullName,
    string? Email,
    string? ContextId,
    string? ContextTitle,
    string? SectionSourcedId,
    string? ResourceLinkId,
    string? ResourceLinkEnd,
    string? ProgramCode,
    string? Term,
    string PrivacyLevel,
    IReadOnlySet<string>? ExtensionAllowList);

public static partial class VariableSubstitution
{
    public static string Apply(string? input, SubstitutionValues values)
    {
        if (string.IsNullOrEmpty(input)) return input ?? "";
        return VariablePattern().Replace(input, match =>
        {
            var name = match.Groups[1].Success && match.Groups[1].Length > 0
                ? match.Groups[1].Value
                : match.Groups[2].Value;
            return TryResolve(name, values, out var resolved) ? resolved : match.Value;
        });
    }

    public static bool TryResolve(string name, SubstitutionValues values, out string resolved)
    {
        resolved = "";
        var rank = PrivacyLevels.Rank(values.PrivacyLevel);
        string? value = name switch
        {
            "User.id" when rank >= PrivacyLevels.Rank(PrivacyLevels.NameOnly) => values.UserId,
            "Person.name.full" when rank >= PrivacyLevels.Rank(PrivacyLevels.NameOnly) => values.FullName,
            "Person.email.primary" when rank >= PrivacyLevels.Rank(PrivacyLevels.EmailOnly) => values.Email,
            "Context.id" => values.ContextId,
            "Context.title" => values.ContextTitle,
            "CourseSection.sourcedId" when rank >= PrivacyLevels.Rank(PrivacyLevels.Public) => values.SectionSourcedId,
            "ResourceLink.id" => values.ResourceLinkId,
            "ResourceLink.available.endDateTime" => values.ResourceLinkEnd,
            "Wgu.program.code" when Allowed(values, "Wgu.program.code") => values.ProgramCode,
            "Wgu.term" when Allowed(values, "Wgu.term") => values.Term,
            _ => null,
        };

        if (value is null) return false;
        resolved = value;
        return true;
    }

    private static bool Allowed(SubstitutionValues values, string name) =>
        values.ExtensionAllowList is not null && values.ExtensionAllowList.Contains(name);

    [GeneratedRegex(@"\$\{([A-Za-z0-9_.]+)\}|\$([A-Za-z0-9_.]+)")]
    private static partial Regex VariablePattern();
}
