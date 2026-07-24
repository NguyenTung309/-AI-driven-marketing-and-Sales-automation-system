namespace Clawbot.SharedKernel.Content.Visuals;

public sealed class ContentVisualContractException : Exception
{
    public ContentVisualContractException(string code, string path)
        : base($"{code} at {path}")
    {
        Code = code;
        Path = path;
    }

    public string Code { get; }
    public string Path { get; }
}
