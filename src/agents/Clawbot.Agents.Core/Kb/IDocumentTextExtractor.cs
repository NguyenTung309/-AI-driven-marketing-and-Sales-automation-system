namespace Clawbot.Agents.Core.Kb;

public sealed record ExtractedDocument(string Markdown, int CharCount, string SourceFormat);

/// <summary>
/// Converts an uploaded knowledge file (docx, xlsx, csv, pdf, txt, md) into markdown
/// suitable for the KB ingestion pipeline. The result is a DRAFT — an operator reviews and
/// edits it before deploying, so light extraction noise is acceptable.
/// </summary>
public interface IDocumentTextExtractor
{
    /// <summary>Supported lower-case file extensions including the leading dot.</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    bool CanExtract(string fileName);

    /// <summary>
    /// Extracts markdown from the stream. <paramref name="fileName"/> drives format detection.
    /// Throws <see cref="DocumentExtractionException"/> on unsupported format or unreadable content.
    /// </summary>
    Task<ExtractedDocument> ExtractAsync(Stream content, string fileName, CancellationToken ct = default);
}

public sealed class DocumentExtractionException(string message) : Exception(message);
