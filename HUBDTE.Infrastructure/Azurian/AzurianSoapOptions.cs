namespace HUBDTE.Infrastructure.Azurian;

public sealed class AzurianSoapOptions
{
    public string ApiKey { get; set; } = "";
    public int RutEmpresa { get; set; }
    public int ResolucionSii { get; set; }
    public string Soap12Endpoint { get; set; } = "";
}