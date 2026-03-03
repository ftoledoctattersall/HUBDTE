using HUBDTE.Application.Interfaces;
using System.Text;

namespace HUBDTE.Infrastructure.Azurian.Adapters
{
    public class AzurianFileWriterAdapter : IAzurianFileWriter
    {
        public async Task WriteTxtAsync(string outputDir, string fileName, string txtContent, CancellationToken ct)
        {
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            var fullPath = Path.Combine(outputDir, fileName);

            // Mantener ISO-8859-1 como en tu worker actual
            var enc = Encoding.GetEncoding("ISO-8859-1");
            await File.WriteAllTextAsync(fullPath, txtContent, enc, ct);
        }
    }
}