using HUBDTE.Application.Interfaces;
using HUBDTE.Infrastructure.Azurian;
using Microsoft.Extensions.Options;

namespace HUBDTE.WorkerHost.HostedServices
{
    public class AzurianDevSettings : IAzurianDevSettings
    {
        private readonly AzurianDevOptions _opt;
        public AzurianDevSettings(IOptions<AzurianDevOptions> opt) { _opt = opt.Value; }
        public bool ForceWriteTxt => _opt.ForceWriteTxt;
        public string OutputPath => _opt.OutputPath;
    }
}