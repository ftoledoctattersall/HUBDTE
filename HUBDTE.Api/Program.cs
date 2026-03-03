using HUBDTE.Application.DependencyInjection;
using HUBDTE.Infrastructure.DependencyInjection;
using Microsoft.OpenApi.Models;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// ✅ Infra (DbContext + repos + uow + filewriter, etc)
builder.Services.AddInfrastructure(builder.Configuration);

// ✅ Application SOLO INGESTA (NO DocumentProcessor)
builder.Services.AddApplicationIngestion();

// ✅ Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "HUBDTE API",
        Version = "v1",
        Description = "API para recibir documentos desde SAP (JSON), persistir Outbox y publicar a RabbitMQ para procesamiento y generación de TXT."
    });

    c.AddSecurityDefinition("ClientToken", new OpenApiSecurityScheme
    {
        Name = "X-Client-Token",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Token compartido para autorizar el envío de documentos. Ej: X-Client-Token: {token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ClientToken"
                }
            },
            Array.Empty<string>()
        }
    });

    c.OperationFilter<HUBDTE.Api.Swagger.DocumentsExamplesOperationFilter>();
});


var app = builder.Build();

// Swagger
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("QA"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "HUBDTE API v1");
        c.DisplayRequestDuration();
    });
}

app.UseHttpsRedirection();

// ✅ Middleware token ANTES de authorization (mejor)
app.Use(async (context, next) =>
{
    var expected = context.RequestServices.GetRequiredService<IConfiguration>()["Security:ClientToken"];
    if (!string.IsNullOrWhiteSpace(expected))
    {
        if (!context.Request.Headers.TryGetValue("X-Client-Token", out var provided) ||
            string.IsNullOrWhiteSpace(provided) ||
            !string.Equals(provided.ToString(), expected, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Token inválido o ausente (X-Client-Token)." });
            return;
        }
    }

    await next();
});

app.UseAuthorization();
app.MapControllers();
app.Run();