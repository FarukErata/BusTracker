namespace BusTracker.Api.Shapes;

public sealed record SourceVersion(string? ETag, DateTimeOffset? LastModified, long? Size)
{
    public bool SameAs(SourceVersion? other) =>
        other is not null && (ETag is not null && other.ETag is not null
            ? ETag == other.ETag
            : LastModified == other.LastModified && Size == other.Size);
}

public sealed class OpenDataClient(HttpClient http)
{
    public async Task<SourceVersion> GetVersionAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return new SourceVersion(
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified,
            response.Content.Headers.ContentLength);
    }

    public async Task<Stream> OpenAsync(string url, CancellationToken ct)
    {
        var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }
}
