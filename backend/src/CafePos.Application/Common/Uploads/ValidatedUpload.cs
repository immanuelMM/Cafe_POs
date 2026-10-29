namespace CafePos.Application.Common.Uploads;

/// <summary>An upload whose type, size and file signature have been checked.</summary>
public sealed record ValidatedUpload(byte[] Data, string ContentType);
