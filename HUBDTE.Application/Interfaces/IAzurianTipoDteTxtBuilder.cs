namespace HUBDTE.Application.Interfaces;

public interface IAzurianTipoDteTxtBuilder
{
    int TipoDte { get; }
    string BuildTxt(string payloadJson);
}
