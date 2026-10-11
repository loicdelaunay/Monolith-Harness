using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MonolithHarness.Server.Api;

/// <summary>Describes the API and its bearer authentication in the generated OpenAPI document.</summary>
public sealed class OpenApiSetup : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = ApiSupport.Name + " API",
            Version = "v1",
            Description = "REST API shared by the Desktop, CLI and Android clients and the web interface. " +
                "Real-time events (streaming replies, run progress, approvals, shared conversations) flow over the WebSocket at /ws."
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "opaque session token",
            Description = "Token returned by POST /api/auth/login."
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("bearer", document)] = [] });
        return Task.CompletedTask;
    }
}
