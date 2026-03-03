using HUBDTE.Application.Azurian;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProcessDTEService;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Xml;

namespace HUBDTE.Infrastructure.Azurian;

public sealed class AzurianSoapClient : IAzurianClient
{
    private readonly AzurianSoapOptions _opt;
    private readonly ILogger<AzurianSoapClient> _logger;

    public AzurianSoapClient(
        IOptions<AzurianSoapOptions> opt,
        ILogger<AzurianSoapClient> logger)
    {
        _opt = opt.Value;
        _logger = logger;
    }

    public async Task<AzurianProcessResult> SendTxtAsync(
        string txtContent,
        string fileName,
        string? empresa,
        CancellationToken ct)
    {
        var validation = ValidateConfiguration();
        if (validation is not null)
            return validation;

        var client = CreateClient();

        try
        {
            var request = BuildRequest(txtContent, fileName);
            var response = await client.importarDocumentosAsync(request);

            return MapResponse(response);
        }
        catch (FaultException faultEx)
        {
            _logger.LogError(faultEx, "SOAP Fault Azurian");
            return Fail(502, $"SOAP Fault: {faultEx.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error llamando SOAP Azurian");
            return Fail(502, ex.Message);
        }
        finally
        {
            await SafeCloseAsync(client);
        }
    }

    private AzurianProcessResult? ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_opt.Soap12Endpoint))
            return Fail(500, "AzurianSoap:Soap12Endpoint viene vacío en appsettings.");

        if (string.IsNullOrWhiteSpace(_opt.ApiKey))
            return Fail(500, "AzurianSoap:ApiKey viene vacío en appsettings.");

        return null;
    }

    private ProcessDTEServicePortTypeClient CreateClient()
    {
        var endpointAddress = new EndpointAddress(_opt.Soap12Endpoint);
        return new ProcessDTEServicePortTypeClient(
            CreateSoap12HttpsBinding(),
            endpointAddress);
    }

    private DTEProcessRequest BuildRequest(string txtContent, string fileName)
    {
        var normalizedTxt = NormalizeTxt(txtContent);

        return new DTEProcessRequest
        {
            apiKey = _opt.ApiKey,
            archivo = fileName,
            data = normalizedTxt,

            resolucionSii = _opt.ResolucionSii,
            resolucionSiiSpecified = true,

            rutEmpresa = _opt.RutEmpresa,
            rutEmpresaSpecified = true
        };
    }

    private static string NormalizeTxt(string txtContent)
    {
        var enc = Encoding.GetEncoding("ISO-8859-1");
        return enc.GetString(enc.GetBytes(txtContent));
    }

    private static AzurianProcessResult MapResponse(importarDocumentosResponse? response)
    {
        var result = response?.@return;

        if (result is null)
            return Fail(502, "Respuesta SOAP nula (return null).");

        var ok = string.Equals(result.codigoRespuesta, "0", StringComparison.OrdinalIgnoreCase);

        return new AzurianProcessResult
        {
            Ok = ok,
            StatusCode = ok ? 200 : 422,
            ResponseBody = $"codigoRespuesta={result.codigoRespuesta}; descripcion={result.descripcionRespuesta}"
        };
    }

    private static async Task SafeCloseAsync(ProcessDTEServicePortTypeClient client)
    {
        try
        {
            await client.CloseAsync();
        }
        catch
        {
            try { client.Abort(); } catch { }
        }
    }

    private static Binding CreateSoap12HttpsBinding()
    {
        var text = new TextMessageEncodingBindingElement(
            MessageVersion.CreateVersion(EnvelopeVersion.Soap12, AddressingVersion.None),
            Encoding.UTF8);

        text.ReaderQuotas = XmlDictionaryReaderQuotas.Max;

        var https = new HttpsTransportBindingElement
        {
            AllowCookies = true,
            MaxBufferSize = int.MaxValue,
            MaxReceivedMessageSize = int.MaxValue
        };

        return new CustomBinding(text, https)
        {
            SendTimeout = TimeSpan.FromSeconds(60),
            ReceiveTimeout = TimeSpan.FromSeconds(60)
        };
    }

    private static AzurianProcessResult Fail(int status, string body) => new()
    {
        Ok = false,
        StatusCode = status,
        ResponseBody = body
    };
}