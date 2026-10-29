namespace CafePos.Application.Common.Uploads;

/// <summary>Image (or PDF) bytes stored in the database, with the content type they were validated as.</summary>
public sealed record StoredImage(byte[] Data, string ContentType);
