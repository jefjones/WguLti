using WguLti.Core.Models;
using WguLti.Core.Substitution;

namespace WguLti.Core.Launch;

public static class CustomParams
{
    public static IReadOnlyDictionary<string, string> MergeAndSubstitute(
        LaunchBundle launch,
        string resourceLinkClaimId,
        SubstitutionValues values)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        void Apply(IEnumerable<CustomPair> pairs)
        {
            foreach (var pair in pairs)
                merged[pair.Name] = pair.Value;
        }

        Apply(launch.CustomParams.Where(p => p.Scope == "registration" && p.ScopeRefId is null));
        if (launch.PlacementId is Guid placementId)
            Apply(launch.CustomParams.Where(p => p.Scope == "placement" && p.ScopeRefId == placementId));
        Apply(launch.CustomParams.Where(p => p.Scope == "deployment" && p.ScopeRefId == launch.DeploymentRowId));
        if (launch.ResourceLinkRowId is Guid linkId)
            Apply(launch.CustomParams.Where(p => p.Scope == "resource_link" && p.ScopeRefId == linkId));
        foreach (var pair in launch.LinkCustom)
            merged[pair.Key] = pair.Value;

        var substituted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in merged)
        {
            var withLink = values with { ResourceLinkId = values.ResourceLinkId ?? resourceLinkClaimId };
            substituted[pair.Key] = VariableSubstitution.Apply(pair.Value, withLink);
        }
        return substituted;
    }
}
