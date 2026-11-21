namespace WguLti.Api;

public static class StubEndpoints
{
    public static void MapStubs(this WebApplication app)
    {
        app.MapPost("/api/lti/deep_linking/return", () => Todo(
            "Phase 2",
            "Accept an LtiDeepLinkingResponse JWT, verify it with the tool JWKS, and create resource links."));
        app.MapPost("/api/lti/register", () => Todo(
            "Phase 5",
            "Dynamic Registration: consume a one-time registration_token and return a client_id. Registration stays off until an admin approves it."));
        app.MapPost("/api/admin/dynamic_registrations", () => Todo(
            "Phase 5",
            "Start a dynamic registration by minting a registration_token and opening the tool's registration URL."));
        app.MapPost("/api/admin/keys/rotate", () => Todo(
            "Phase 6",
            "Create a 'next' signing key, publish it, then promote it to active. Dev keys stay on the local RSA file."));

        app.MapGet("/api/lti/courses/{ctx}/line_items", (string ctx) => Todo(
            "Phase 3",
            $"AGS line item container for context {ctx}. Filter by resource_link_id, resource_id, tag, and limit."));
        app.MapPost("/api/lti/courses/{ctx}/line_items", (string ctx) => Todo(
            "Phase 3",
            $"Create an AGS line item (and gradebook column) in context {ctx}."));
        app.MapGet("/api/lti/courses/{ctx}/line_items/{id}", (string ctx, string id) => Todo(
            "Phase 3", $"Read line item {id} in context {ctx}."));
        app.MapPut("/api/lti/courses/{ctx}/line_items/{id}", (string ctx, string id) => Todo(
            "Phase 3", $"Update line item {id} in context {ctx}."));
        app.MapDelete("/api/lti/courses/{ctx}/line_items/{id}", (string ctx, string id) => Todo(
            "Phase 3", $"Delete line item {id} in context {ctx}."));
        app.MapPost("/api/lti/courses/{ctx}/line_items/{id}/scores", (string ctx, string id) => Todo(
            "Phase 3",
            $"Append an AGS score for line item {id}. The database trigger updates results only when the tool timestamp is newer."));
        app.MapGet("/api/lti/courses/{ctx}/line_items/{id}/results", (string ctx, string id) => Todo(
            "Phase 3", $"Current AGS results for line item {id} in context {ctx}."));

        app.MapGet("/api/lti/courses/{ctx}/names_and_roles", (string ctx) => Todo(
            "Phase 4",
            $"NRPS membership container for context {ctx}. Honor privacy level, role filter, and Link rel=next paging."));

        app.MapGet("/api/lti/deployments/{id}/notice_handlers", (string id) => Todo(
            "Phase 7", $"List Platform Notification Service handlers for deployment {id}."));
        app.MapPut("/api/lti/deployments/{id}/notice_handlers", (string id) => Todo(
            "Phase 7", $"Register or clear a PNS handler for deployment {id}. Delivery is the outbox worker."));
    }

    private static IResult Todo(string phase, string detail) =>
        Results.Problem(
            title: "Not implemented",
            detail: $"TODO {phase}: {detail} See docs/design.md.",
            statusCode: StatusCodes.Status501NotImplemented);
}
