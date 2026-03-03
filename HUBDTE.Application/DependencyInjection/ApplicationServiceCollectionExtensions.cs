using HUBDTE.Application.DocumentIngestion;
using HUBDTE.Application.DocumentProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace HUBDTE.Application.DependencyInjection;
public static class ApplicationServiceCollectionExtensions
{
    // ✅ Para API (solo ingesta)
    public static IServiceCollection AddApplicationIngestion(this IServiceCollection services)
    {
        services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
        return services;
    }

    // ✅ Para WorkerHost (procesamiento + soporte)
    public static IServiceCollection AddApplicationProcessing(this IServiceCollection services)
    {
        services.AddScoped<IDocumentProcessor, DocumentProcessor>();

        // ✅ NUEVO: el Consumer no toca EF; registra fallas vía Application
        services.AddScoped<IProcessingFailureRecorder, ProcessingFailureRecorder>();

        return services;
    }
}