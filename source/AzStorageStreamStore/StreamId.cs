using AzStorageStreamStore;

/// <summary>
/// the "id" of an actual stream.
/// </summary>
/// <param name="TenantId"></param>
/// <param name="Id"></param>
public record StreamId(string TenantId, string Id) {
    public static implicit operator StreamKey(StreamId id) => new(new[] { id.TenantId, id.Id });
    public static bool operator ==(StreamId id, StreamKey key) => key.Equals(id);
    public static bool operator !=(StreamId id, StreamKey key) => !key.Equals(id);
}
