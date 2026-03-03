namespace HUBDTE.Application.Interfaces;

public interface IAzurianTxtBuilder
{
    string BuildTxt(string payloadJson, int tipoDte);
}
