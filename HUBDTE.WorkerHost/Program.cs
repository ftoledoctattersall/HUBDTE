using HUBDTE.Application.Azurian;
using HUBDTE.Application.DependencyInjection;
using HUBDTE.Application.DocumentProcessing;
using HUBDTE.Application.Interfaces;
using HUBDTE.Infrastructure.Azurian;
using HUBDTE.Infrastructure.Azurian.Builders;
using HUBDTE.Infrastructure.Azurian.Layouts;
using HUBDTE.Infrastructure.DependencyInjection;
using HUBDTE.Infrastructure.Messaging;
using HUBDTE.Infrastructure.Messaging.Options;
using HUBDTE.WorkerHost.Consumers;
using HUBDTE.WorkerHost.HostedServices;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// =========================
// Options / Config
// =========================
builder.Services
    .AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection("RabbitMq"))
    .Validate(o =>
        !string.IsNullOrWhiteSpace(o.HostName) &&
        !string.IsNullOrWhiteSpace(o.UserName) &&
        !string.IsNullOrWhiteSpace(o.Password) &&
        !string.IsNullOrWhiteSpace(o.Exchange) &&
        o.Port > 0,
        "RabbitMq inválido")
    .ValidateOnStart();

builder.Services
    .AddOptions<RetryPolicyOptions>()
    .Bind(builder.Configuration.GetSection("RetryPolicy"))
    .Validate(o => o.MaxAttempts >= 1, "RetryPolicy: MaxAttempts debe ser >= 1")
    .Validate(o => o.DelaysSeconds is not null, "RetryPolicy: DelaysSeconds no puede ser null")
    .ValidateOnStart();

builder.Services
    .AddOptions<QueuesOptions>()
    .Bind(builder.Configuration.GetSection("Queues"))
    .Validate(o =>
        !string.IsNullOrWhiteSpace(o.Dte39) &&
        !string.IsNullOrWhiteSpace(o.Dte33) &&
        !string.IsNullOrWhiteSpace(o.Dte34) &&
        !string.IsNullOrWhiteSpace(o.Dte110) &&
        !string.IsNullOrWhiteSpace(o.Dte61) &&
        !string.IsNullOrWhiteSpace(o.Dte56) &&
        !string.IsNullOrWhiteSpace(o.Dte111) &&
        !string.IsNullOrWhiteSpace(o.Dte112) &&
        !string.IsNullOrWhiteSpace(o.Dte52),
        "Queues inválido")
    .ValidateOnStart();

builder.Services.Configure<AzurianLayoutFilesOptions>(
    builder.Configuration.GetSection("AzurianLayoutFiles"));

builder.Services.AddSingleton<IAzurianLayoutRepository, AzurianLayoutRepository>();

builder.Services.Configure<AzurianSoapOptions>(
    builder.Configuration.GetSection("AzurianSoap"));

builder.Services.AddSingleton<IAzurianClient, AzurianSoapClient>();

builder.Services.Configure<AzurianDevOptions>(
    builder.Configuration.GetSection("AzurianDev"));

builder.Services.AddSingleton<IAzurianDevSettings, AzurianDevSettings>();

// =========================
// Startup validators
// =========================
builder.Services.AddHostedService<AzurianLayoutStartupValidator>();
builder.Services.AddHostedService<AzurianOutputDirStartupValidator>();

// =========================
// Layer DI
// =========================
builder.Services.AddApplicationProcessing();
builder.Services.AddInfrastructure(builder.Configuration);

// Seguridad: si no lo registraste en AddApplicationProcessing()
builder.Services.AddScoped<IProcessingFailureRecorder, ProcessingFailureRecorder>();

// =========================
// Hosted services
// =========================
builder.Services.AddHostedService<RabbitTopologyInitializerHostedService>();
builder.Services.AddHostedService<OutboxPublisherHostedService>();

// =========================
// Rabbit Consumers per queue
// =========================
void AddQueueWorker(string queueName)
{
    builder.Services.AddSingleton<IHostedService>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<RabbitConsumerWorker>>();
        var config = sp.GetRequiredService<IConfiguration>();
        var rabbitFactory = sp.GetRequiredService<RabbitChannelFactory>();
        var retryPolicy = sp.GetRequiredService<IRetryPolicyService>();
        var rabbitOptions = sp.GetRequiredService<IOptions<RabbitMqOptions>>();

        return new RabbitConsumerWorker(
            sp,
            logger,
            config,
            rabbitFactory,
            retryPolicy,
            rabbitOptions,
            queueName);
    });
}

void AddDlqReprocessor(string mainQueue)
{
    if (string.IsNullOrWhiteSpace(mainQueue))
        throw new InvalidOperationException("mainQueue no puede ser null/vacía al registrar DLQ.");

    var dlqQueue = QueueNames.Dlq(mainQueue);

    builder.Services.AddSingleton<IHostedService>(sp =>
    {
        var rabbitFactory = sp.GetRequiredService<RabbitChannelFactory>();
        var rabbitOptions = sp.GetRequiredService<IOptions<RabbitMqOptions>>();
        var logger = sp.GetRequiredService<ILogger<RabbitDlqReprocessorWorker>>();

        return new RabbitDlqReprocessorWorker(
            rabbitFactory,
            rabbitOptions,
            sp,
            logger,
            dlqQueue,
            mainQueue);
    });
}

string GetQueue(string key)
{
    var value = builder.Configuration.GetSection("Queues").GetValue<string>(key);
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"Falta configuración: Queues:{key}");
    return value;
}

var queueKeys = new[]
{
    "Dte39",
    "Dte33",
    "Dte34",
    "Dte110",
    "Dte61",
    "Dte56",
    "Dte111",
    "Dte112",
    "Dte52"
};

var allQueues = queueKeys
    .Select(GetQueue)
    .ToArray();

foreach (var queue in allQueues)
{
    AddQueueWorker(queue);
    AddDlqReprocessor(queue);
}

// =========================
// TXT builder + Builders por tipo
// =========================
builder.Services.AddSingleton<IAzurianTxtBuilder, AzurianTxtBuilder>();

void AddAzurianTipoDteBuilder(int tipoDte)
{
    builder.Services.AddSingleton<IAzurianTipoDteTxtBuilder>(sp =>
        new AzurianTipoDteFixedWidthBuilder(
            sp.GetRequiredService<IAzurianLayoutRepository>(),
            tipoDte));
}

var tiposDteSoportados = new[] { 33, 39, 34, 52, 56, 61, 110, 111, 112 };

foreach (var tipoDte in tiposDteSoportados)
{
    AddAzurianTipoDteBuilder(tipoDte);
}

var host = builder.Build();
host.Run();