namespace AiLab.Infrastructure.Files;

/// <summary>Converts an attached file into text suitable for inlining into a prompt. Additional formats (PDF, etc.) are a drop-in future implementation of this same abstraction.</summary>
public interface IFileContentExtractor
{
    bool CanHandle(string contentType, string fileName);

    Task<string> ExtractTextAsync(string absoluteFilePath, string contentType, CancellationToken cancellationToken = default);
}
