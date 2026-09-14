namespace OpenAiBench.Infrastructure.Persistence;

public static class ContentTypeGuesser
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".json"] = "application/json",
        [".csv"] = "text/csv",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg"
    };

    public static string Guess(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return ByExtension.TryGetValue(extension, out var contentType) ? contentType : "application/octet-stream";
    }
}
