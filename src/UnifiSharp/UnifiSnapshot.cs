namespace UnifiSharp;

/// <summary>A structured, read-only snapshot of the UniFi network (discover output).</summary>
public sealed record UnifiSnapshot
{
    public required IReadOnlyList<UnifiSiteSnapshot> Sites { get; init; }
}

/// <summary>A site and what it contains.</summary>
public sealed record UnifiSiteSnapshot
{
    public required string Site { get; init; }
    public Guid? Id { get; init; }
    public int Devices { get; init; }
    public int Clients { get; init; }
    public IReadOnlyList<string> Networks { get; init; } = [];
}
