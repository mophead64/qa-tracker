namespace QaTracker.Web.Demo;

/// <summary>
/// The files the demo data attaches — bug screenshots, a screen recording, PDFs, a console log —
/// embedded in the app from Demo/Samples (about 500 KB). They're rendered from HTML mock-ups by the
/// docs-screenshots tool (<c>dotnet run --project tools/docs-screenshots -- samples</c>).
/// </summary>
public static class DemoSamples
{
    private const string Prefix = "QaTracker.Web.Demo.Samples.";

    public static Stream Open(string fileName) =>
        typeof(DemoSamples).Assembly.GetManifestResourceStream(Prefix + fileName)
        ?? throw new FileNotFoundException($"No embedded demo sample named {fileName}.", fileName);
}
