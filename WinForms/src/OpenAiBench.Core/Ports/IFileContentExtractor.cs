namespace OpenAiBench.Core.Ports;

/// <summary>
/// Converts an attached file into text suitable for inlining into a prompt. Milestone 1 ships a
/// text-file implementation only; PDF/DOCX extraction is a drop-in future implementation of this
/// same abstraction.
/// </summary>
public interface IFileContentExtractor
{
    bool CanHandle(string contentType, string fileName);
    Task<string> ExtractTextAsync(string absoluteFilePath, string contentType, CancellationToken cancellationToken = default);
}
