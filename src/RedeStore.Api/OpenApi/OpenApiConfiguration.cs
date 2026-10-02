using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RedeStore.Api.OpenApi;

public static class OpenApiConfiguration
{
    private const string BearerSchemeId = "Bearer";

    public static void Configurar(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "RedeStore API",
                Version = "v1",
                Description =
                    "API da RedeStore: autenticação, loja (produtos e pedidos) e eventos (com inscrições). " +
                    "Endpoints protegidos exigem o header `Authorization: Bearer <token>`, obtido em `/auth/login` ou `/auth/cadastro`. " +
                    "Erros seguem o formato ProblemDetails (RFC 7807): `title` traz o código do erro (ex.: `EVENTO_NAO_ENCONTRADO`) e `detail` a mensagem.",
            };

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[BearerSchemeId] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "JWT retornado por /auth/login ou /auth/cadastro. Validade padrão: 8 horas.",
            };

            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            if (!metadata.OfType<IAuthorizeData>().Any())
            {
                return Task.CompletedTask;
            }

            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerSchemeId, context.Document)] = [],
            });

            return Task.CompletedTask;
        });
    }
}
