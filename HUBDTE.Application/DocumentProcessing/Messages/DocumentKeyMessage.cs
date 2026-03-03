namespace HUBDTE.Application.DocumentProcessing.Messages;

public sealed record DocumentKeyMessage(string FilialCode, long DocEntry, int TipoDte);