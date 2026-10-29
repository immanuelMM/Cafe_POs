namespace CafePos.Domain;

/// <summary>Photo bytes for an Item, kept in their own table so catalog queries never load them.</summary>
public class ItemImage
{
    public Guid ItemId { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "";
}
