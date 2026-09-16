using System.Security.Claims;
using Analytics.Api.InstagramIntegration.Application;
using Analytics.Api.Security;

namespace Analytics.Api.InstagramIntegration;

public static class InstagramOAuthEndpoints
{
    public static IEndpointRouteBuilder MapInstagramOAuthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/integrations/instagram")
            .RequireAuthorization(ApplicationSecurity.AuthenticatedUserPolicy);

        group.MapGet("/connect", ConnectAsync)
            .RequireRateLimiting(ApplicationSecurity.TenantMutationRateLimitPolicy)
            .WithName("ConnectInstagram");
        group.MapGet("/callback", CallbackAsync)
            .RequireRateLimiting(ApplicationSecurity.TenantMutationRateLimitPolicy)
            .WithName("CompleteInstagramConnection");

        return endpoints;
    }

    private static async Task<IResult> ConnectAsync(
        ClaimsPrincipal principal,
        IInstagramOAuthFlowService flowService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(principal, out var ownerUserId))
        {
            return Results.Unauthorized();
        }

        var authorizationUri = await flowService.CreateAuthorizationUriAsync(
            ownerUserId,
            cancellationToken);
        return authorizationUri is null
            ? Disabled()
            : Results.Redirect(authorizationUri.AbsoluteUri);
    }

    private static async Task<IResult> CallbackAsync(
        HttpRequest request,
        ClaimsPrincipal principal,
        IInstagramOAuthFlowService flowService,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(principal, out var ownerUserId))
        {
            return Results.Unauthorized();
        }

        var result = await flowService.CompleteAsync(
            ownerUserId,
            request.Query["state"],
            request.Query["code"],
            request.Query["error"],
            cancellationToken);

        return result.Status switch
        {
            InstagramOAuthCompletionStatus.Connected => Results.Ok(result.Connection),
            InstagramOAuthCompletionStatus.Disabled => Disabled(),
            InstagramOAuthCompletionStatus.InvalidState => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Instagram authorization state",
                detail: "The authorization state is invalid, expired, or already used."),
            InstagramOAuthCompletionStatus.Denied => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Instagram authorization denied",
                detail: "Instagram access was not granted."),
            InstagramOAuthCompletionStatus.InvalidCallback => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Instagram callback",
                detail: "The authorization callback did not include a code."),
            InstagramOAuthCompletionStatus.UnsupportedAccount => Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Professional Instagram account required",
                detail: "Connect an Instagram Business or Creator account."),
            InstagramOAuthCompletionStatus.MissingScopes => Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Required Instagram permissions are missing",
                detail: $"Grant these permissions and try again: {string.Join(", ", result.MissingScopes ?? [])}."),
            InstagramOAuthCompletionStatus.AccountAlreadyOwned => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Instagram account already connected",
                detail: "This Instagram account cannot be connected to this user."),
            _ => Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Instagram authorization failed",
                detail: "Instagram could not complete the authorization request."),
        };
    }

    private static IResult Disabled() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Instagram integration disabled",
        detail: "Instagram integration is not configured for this environment.");

    private static bool TryGetOwnerId(ClaimsPrincipal principal, out Guid ownerUserId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out ownerUserId);
}
