namespace HUBDTE.Application.Interfaces;

public interface IAzurianDevSettings
{
    bool ForceWriteTxt { get; }
    string OutputPath { get; }
}