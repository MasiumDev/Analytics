using System.Security.Claims;
using Analytics.Api.InstagramAccounts.Application;
using Analytics.Api.InstagramAccounts.Domain;
using Analytics.Api.Security;

namespace Analytics.Api.InstagramAccounts;

public static class InstagramAccountEndpoints
{
    public static IEndpointRouteBuilder MapInstagramAccountEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/instagram-accounts")
            .RequireAuthorization(ApplicationSecurity.AuthenticatedUserPolicy);

        group.MapGet("/", ListAsync).WithName("ListInstagramAccounts");
        group.MapGet("/{accountId:guid}", GetAsync).WithName("GetInstagramAccount");
        group.MapPost("/", CreateAsync)
            .RequireAntiforgery()
            .RequireRateLimiting(ApplicationSecurity.TenantMutationRateLimitPolicy)
            .WithName("CreateInstagramAccount");
        group.MapPut("/{accountId:guid}", UpdateAsync)
            .RequireAntiforgery()
            .RequireRateLimiting(ApplicationSecurity.TenantMutationRateLimitPolicy)
            .WithName("UpdateInstagramAccount");

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        IInstagramAccountService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(principal, out var ownerUserId))
        {
            return Results.Unauthorized();
        }

        var accounts = await service.ListAsync(ownerUserId, cancellationToken);
        return Results.Ok(accounts.Select(ToResponse));
    }

    private static async Task<IResult> GetAsync(
        Guid accountId,
        ClaimsPrincipal principal,
        IInstagramAccountService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(principal, out var ownerUserId))
        {
            return Results.Unauthorized();
        }

        var account = await service.GetAsync(ownerUserId, accountId, cancellationToken);
        return account is null ? Results.NotFound() : Results.Ok(ToResponse(account));
    }

    private static async Task<IResult> CreateAsync(
        InstagramAccountRequest request,
        ClaimsPrincipal principal,
        IInstagramAccountService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(principal, out var ownerUserId))
        {
            return Results.Unauthorized();
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await service.CreateAsync(
            ownerUserId,
            request.InstagramUserId!,
            request.Username!,
            request.DisplayName,
            cancellationToken);
        if (result.AlreadyConnected)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Instagram account already connected",
                detail: "This Instagram account is already connected.");
        }

        var response = ToResponse(result.Account!);
        return Results.Created($"/api/instagram-accounts/{response.Id}", response);
    }

    private static async Task<IResult> UpdateAsync(
        Guid accountId,
        InstagramAccountProfileRequest request,
        ClaimsPrincipal principal,
        IInstagramAccountService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(principal, out var ownerUserId))
        {
            return Results.Unauthorized();
        }

        var errors = ValidateProfile(request.Username, request.DisplayName);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var account = await service.UpdateAsync(
            ownerUserId,
            accountId,
            request.Username!,
            request.DisplayName,
            cancellationToken);
        return account is null ? Results.NotFound() : Results.Ok(ToResponse(account));
    }

    private static Dictionary<string, string[]> Validate(InstagramAccountRequest request)
    {
        var errors = ValidateProfile(request.Username, request.DisplayName);
        if (string.IsNullOrWhiteSpace(request.InstagramUserId)
            || request.InstagramUserId.Length > 64)
        {
            errors[nameof(request.InstagramUserId)] =
                ["An Instagram user ID with at most 64 characters is required."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateProfile(
        string? username,
        string? displayName)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(username) || username.Length > 64)
        {
            errors[nameof(username)] =
                ["A username with at most 64 characters is required."];
        }

        if (displayName?.Length > 200)
        {
            errors[nameof(displayName)] =
                ["Display name cannot exceed 200 characters."];
        }

        return errors;
    }

    private static bool TryGetOwnerId(ClaimsPrincipal principal, out Guid ownerUserId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out ownerUserId);

    private static InstagramAccountResponse ToResponse(InstagramAccount account) =>
        new(
            account.Id,
            account.InstagramUserId,
            account.Username,
            account.DisplayName,
            account.CreatedAtUtc,
            account.UpdatedAtUtc);
}

public sealed record InstagramAccountRequest(
    string? InstagramUserId,
    string? Username,
    string? DisplayName = null);

public sealed record InstagramAccountProfileRequest(
    string? Username,
    string? DisplayName = null);

public sealed record InstagramAccountResponse(
    Guid Id,
    string InstagramUserId,
    string Username,
    string? DisplayName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
