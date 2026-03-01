namespace DentalID.Core.Entities;

public abstract class BaseEntity
{
    public int Id { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
