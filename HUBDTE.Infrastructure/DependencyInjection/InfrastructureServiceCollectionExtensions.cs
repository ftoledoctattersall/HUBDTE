using HUBDTE.Application.Interfaces;
using HUBDTE.Application.Interfaces.Messaging;
using HUBDTE.Application.Interfaces.Persistence;
using HUBDTE.Infrastructure.Azurian.Adapters;
using HUBDTE.Infrastructure.Messaging;
using HUBDTE.Infrastructure.Persistence;
using HUBDTE.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HUBDTE.Infrastructure.DependencyInjection
{
    public static class InfrastructureServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(config.GetConnectionString("SqlServer")));

            services.AddScoped<ISapDocumentRepository, SapDocumentRepository>();
            services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();
            services.AddScoped<IUnitOfWork, EfUnitOfWork>();

            services.AddSingleton<IAzurianFileWriter, AzurianFileWriterAdapter>();

            services.AddSingleton<RabbitConnectionFactoryProvider>();
            services.AddSingleton<RabbitChannelFactory>();
            services.AddSingleton<RabbitMqPublisher>();

            services.AddSingleton<IRetryPolicyService, RetryPolicyService>();
            services.AddSingleton<IRabbitTopologyService, RabbitTopologyService>();
            services.AddSingleton<IMessageRoutingResolver, MessageRoutingResolver>();

            return services;
        }
    }
}