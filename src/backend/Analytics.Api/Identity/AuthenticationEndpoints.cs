using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Analytics.Api.Security;
using Microsoft.AspNetCore.Identity;

namespace Analytics.Api.Identity;

public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");

        group.MapPost("/register", RegisterAsync)
            .RequireAntiforgery()
            .RequireRateLimiting(ApplicationSecurity.AuthenticationRateLimitPolicy)
            .WithName("RegisterUser");
        group.MapPost("/login", LoginAsync)
            .RequireAntiforgery()
            .RequireRateLimiting(ApplicationSecurity.AuthenticationRateLimitPolicy)
            .WithName("LoginUser");
        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization(ApplicationSecurity.AuthenticatedUserPolicy)
            .RequireAntiforgery()
            .RequireRateLimiting(ApplicationSecurity.AuthenticationRateLimitPolicy)
            .WithName("LogoutUser");
        group.MapGet("/session", GetSessionAsync)
            .WithName("GetAuthenticationSession");

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        var validationErrors = ValidateCredentials(request.Email, request.Password);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var normalizedEmail = request.Email!.Trim();
        var password = request.Password!;
        if (await userManager.FindByEmailAsync(normalizedEmail) is not null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Account already exists",
                detail: "An account with this email address already exists.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = normalizedEmail,
            Email = normalizedEmail,
        };
        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            return Results.ValidationProblem(ToValidationErrors(result));
        }

        await signInManager.SignInAsync(user, isPersistent: false);

        return Results.Created(
            "/api/auth/session",
            AuthenticationStateResponse.Authenticated(user));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        var validationErrors = ValidateCredentials(request.Email, request.Password);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var normalizedEmail = request.Email!.Trim();
        var password = request.Password!;
        var user = await userManager.FindByEmailAsync(normalizedEmail);
        if (user is null)
        {
            return InvalidCredentials();
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            password,
            request.RememberMe,
            lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Account temporarily locked",
                detail: "Too many failed sign-in attempts. Try again later.");
        }

        return result.Succeeded
            ? Results.Ok(AuthenticationStateResponse.Authenticated(user))
            : InvalidCredentials();
    }

    private static async Task<IResult> LogoutAsync(
        SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> GetSessionAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        if (principal.Identity?.IsAuthenticated is not true)
        {
            return Results.Ok(AuthenticationStateResponse.Anonymous());
        }

        var user = await userManager.GetUserAsync(principal);
        return Results.Ok(user is null
            ? AuthenticationStateResponse.Anonymous()
            : AuthenticationStateResponse.Authenticated(user));
    }

    private static Dictionary<string, string[]> ValidateCredentials(
        string? email,
        string? password)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(email)
            || !new EmailAddressAttribute().IsValid(email))
        {
            errors[nameof(email)] = ["A valid email address is required."];
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            errors[nameof(password)] = ["Password is required."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ToValidationErrors(IdentityResult result)
    {
        return result.Errors
            .GroupBy(error => error.Code.StartsWith("Password", StringComparison.Ordinal)
                ? "password"
                : "account")
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray(),
                StringComparer.Ordinal);
    }

    private static IResult InvalidCredentials()
    {
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Invalid credentials",
            detail: "The email address or password is incorrect.");
    }
}

public sealed record RegisterRequest(string? Email, string? Password);

public sealed record LoginRequest(string? Email, string? Password, bool RememberMe = false);

public sealed record AuthenticationStateResponse(
    bool IsAuthenticated,
    AuthenticatedUserResponse? User)
{
    public static AuthenticationStateResponse Anonymous() => new(false, null);

    public static AuthenticationStateResponse Authenticated(ApplicationUser user) =>
        new(true, new AuthenticatedUserResponse(user.Id, user.Email!));
}

public sealed record AuthenticatedUserResponse(Guid Id, string Email);
