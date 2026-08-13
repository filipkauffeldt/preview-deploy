using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using PreviewDeploy.Server.Auth;
using PreviewDeploy.Server.Data;

namespace PreviewDeploy.Server.Endpoints;

public static partial class DeployEventsEndpoint
{
    public const string CreateAction = "create";
    public const string UpdateAction = "update";
    public const string TeardownAction = "teardown";

    private static readonly string[] AllowedActions = [CreateAction, UpdateAction, TeardownAction];

    public static IEndpointRouteBuilder MapDeployEvents(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/deploy-events", HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        DeployEventRequest request,
        HttpContext httpContext,
        AppTokenValidator tokenValidator,
        PreviewDeployDbContext db,
        Channel<DeployEventRequest> queue,
        CancellationToken cancellationToken)
    {
        if (!AllowedActions.Contains(request.Action))
        {
            return TypedResults.BadRequest("Action must be one of: create, update, teardown");
        }

        if (request.Pr <= 0)
        {
            return TypedResults.BadRequest("Pr must be a positive integer");
        }

        if (!ShaRegex().IsMatch(request.Sha))
        {
            return TypedResults.BadRequest("Sha must be a git commit hash");
        }

        if (!AppNameRegex().IsMatch(request.App))
        {
            return TypedResults.BadRequest("App must be a lowercase DNS-safe name");
        }

        var bearerToken = ExtractBearerToken(httpContext.Request);
        var app = await tokenValidator.ValidateAsync(bearerToken, request.App, cancellationToken);
        if (app is null)
        {
            return TypedResults.Unauthorized();
        }

        db.DeployEvents.Add(new DeployEvent
        {
            AppId = app.Id,
            PrNumber = request.Pr,
            Sha = request.Sha,
            Action = request.Action,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        await queue.Writer.WriteAsync(request, cancellationToken);

        return Results.Accepted();
    }
    private static string? ExtractBearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..]
            : null;
    }

    [GeneratedRegex("^[0-9a-fA-F]{7,40}$")]
    private static partial Regex ShaRegex();

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,62}[a-z0-9])?$")]
    private static partial Regex AppNameRegex();
}

public sealed record DeployEventRequest(string App, int Pr, string Sha, string Action);
