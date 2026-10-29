namespace CafePos.Application.Common.Uploads;

/// <summary>A file received from the client, independent of the web framework. The stream is opened only when the content is read.</summary>
/// <param name="ContentType">Declared content type (may carry parameters such as a charset).</param>
/// <param name="Length">Declared length in bytes.</param>
/// <param name="OpenRead">Opens the content stream.</param>
public sealed record UploadedFile(string? ContentType, long Length, Func<Stream> OpenRead);
