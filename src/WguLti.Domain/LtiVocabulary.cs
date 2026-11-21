namespace WguLti.Domain;

/// <summary>IMS claim names and message types the platform emits.</summary>
public static class LtiClaims
{
    public const string MessageType = "https://purl.imsglobal.org/spec/lti/claim/message_type";
    public const string Version = "https://purl.imsglobal.org/spec/lti/claim/version";
    public const string DeploymentId = "https://purl.imsglobal.org/spec/lti/claim/deployment_id";
    public const string TargetLinkUri = "https://purl.imsglobal.org/spec/lti/claim/target_link_uri";
    public const string ResourceLink = "https://purl.imsglobal.org/spec/lti/claim/resource_link";
    public const string Roles = "https://purl.imsglobal.org/spec/lti/claim/roles";
    public const string Context = "https://purl.imsglobal.org/spec/lti/claim/context";
    public const string ToolPlatform = "https://purl.imsglobal.org/spec/lti/claim/tool_platform";
    public const string LaunchPresentation = "https://purl.imsglobal.org/spec/lti/claim/launch_presentation";
    public const string Custom = "https://purl.imsglobal.org/spec/lti/claim/custom";
    public const string Lis = "https://purl.imsglobal.org/spec/lti/claim/lis";
    public const string AgsEndpoint = "https://purl.imsglobal.org/spec/lti-ags/claim/endpoint";
    public const string NamesAndRoles = "https://purl.imsglobal.org/spec/lti-nrps/claim/namesroleservice";
    public const string DeepLinkingSettings = "https://purl.imsglobal.org/spec/lti-dl/claim/deep_linking_settings";
    public const string Lti1p1 = "https://purl.imsglobal.org/spec/lti/claim/lti1p1";
    public const string PlatformConfiguration = "https://purl.imsglobal.org/spec/lti-platform-configuration";

    public const string VersionValue = "1.3.0";
    public const string ResourceLinkRequest = "LtiResourceLinkRequest";
    public const string DeepLinkingRequest = "LtiDeepLinkingRequest";
}

public static class LtiScopes
{
    public const string LineItem = "https://purl.imsglobal.org/spec/lti-ags/scope/lineitem";
    public const string LineItemReadonly = "https://purl.imsglobal.org/spec/lti-ags/scope/lineitem.readonly";
    public const string ResultReadonly = "https://purl.imsglobal.org/spec/lti-ags/scope/result.readonly";
    public const string Score = "https://purl.imsglobal.org/spec/lti-ags/scope/score";
    public const string MembershipReadonly = "https://purl.imsglobal.org/spec/lti-nrps/scope/contextmembership.readonly";
    public const string NoticeHandlers = "https://purl.imsglobal.org/spec/lti/scope/noticehandlers";

    public const string AgsPrefix = "https://purl.imsglobal.org/spec/lti-ags/scope/";

    public static readonly IReadOnlySet<string> Catalog = new HashSet<string>(StringComparer.Ordinal)
    {
        LineItem, LineItemReadonly, ResultReadonly, Score, MembershipReadonly, NoticeHandlers,
    };

    public static bool IsKnown(string scope) => Catalog.Contains(scope);
    public static bool IsAgs(string scope) => scope.StartsWith(AgsPrefix, StringComparison.Ordinal);
}

public static class LisRoles
{
    public const string Learner = "http://purl.imsglobal.org/vocab/lis/v2/membership#Learner";
    public const string Instructor = "http://purl.imsglobal.org/vocab/lis/v2/membership#Instructor";
    public const string CourseOffering = "http://purl.imsglobal.org/vocab/lis/v2/course#CourseOffering";
    public const string CourseTemplate = "http://purl.imsglobal.org/vocab/lis/v2/course#CourseTemplate";
}

public static class PrivacyLevels
{
    public const string Anonymous = "anonymous";
    public const string NameOnly = "name_only";
    public const string EmailOnly = "email_only";
    public const string Public = "public";

    public static bool IsKnown(string? value) =>
        value is Anonymous or NameOnly or EmailOnly or Public;

    public static int Rank(string? value) => value switch
    {
        NameOnly => 1,
        EmailOnly => 2,
        Public => 3,
        _ => 0,
    };
}

public static class RegistrationStates
{
    public const string On = "on";
    public const string Off = "off";
    public const string Allow = "allow";
    public const string Deleted = "deleted";

    public static bool CanLaunch(string? state) => state is On or Allow;
}

public static class NoncePurposes
{
    public const string AuthRequest = "auth_request";
    public const string ClientAssertionJti = "client_assertion_jti";
    public const string DeepLinkResponse = "deep_link_response";
}

public static class PlacementKinds
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "course_navigation", "account_navigation", "global_navigation", "user_navigation",
        "link_selection", "assignment_selection", "editor_button", "homework_submission",
        "module_menu", "file_menu", "submission_review", "ai_assistant",
    };
}

public static class PlatformInfo
{
    public const string ProductFamilyCode = "wgulti";
    public const string Version = "1.0";
    public const string ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
}
