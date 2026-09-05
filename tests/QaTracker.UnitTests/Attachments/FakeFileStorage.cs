using QaTracker.Web.Storage;

namespace QaTracker.UnitTests.Attachments;

/// <summary>In-memory stand-in for <see cref="IFileStorage"/>, shared by every test that needs an AttachmentService.</summary>
public sealed class FakeFileStorage : IFileStorage
{
    private readonly Dictionary<string, byte[]> objects = [];

    public bool IsConfigured => true;

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        objects[key] = buffer.ToArray();
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult<Stream>(new MemoryStream(objects[key]));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        objects.Remove(key);
        return Task.CompletedTask;
    }

    public bool Contains(string key) => objects.ContainsKey(key);

    public int Count => objects.Count;
}
