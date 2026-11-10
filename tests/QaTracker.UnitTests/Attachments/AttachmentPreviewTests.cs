using QaTracker.Web.Attachments;

namespace QaTracker.UnitTests.Attachments;

public sealed class AttachmentPreviewTests
{
    [Theory]
    [InlineData("shot.png", PreviewKind.Image, "image/png")]
    [InlineData("PHOTO.JPG", PreviewKind.Image, "image/jpeg")]
    [InlineData("anim.gif", PreviewKind.Image, "image/gif")]
    [InlineData("clip.mp4", PreviewKind.Video, "video/mp4")]
    [InlineData("clip.webm", PreviewKind.Video, "video/webm")]
    [InlineData("clip.mov", PreviewKind.Video, "video/quicktime")]
    [InlineData("memo.mp3", PreviewKind.Audio, "audio/mpeg")]
    [InlineData("memo.m4a", PreviewKind.Audio, "audio/mp4")]
    [InlineData("spec.pdf", PreviewKind.Pdf, "application/pdf")]
    public void Allow_listed_extensions_preview_with_their_canonical_type(string fileName, PreviewKind kind, string contentType) =>
        Assert.Equal((kind, contentType), AttachmentPreview.Resolve(fileName));

    [Theory]
    [InlineData("diagram.svg")]       // can carry script
    [InlineData("page.html")]
    [InlineData("notes.txt")]
    [InlineData("data.csv")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_download_only(string? fileName)
    {
        Assert.Null(AttachmentPreview.Resolve(fileName));
        Assert.False(AttachmentPreview.CanPreview(fileName));
    }

    [Fact]
    public void Kind_names_match_the_preview_script() =>
        Assert.Equal(["none", "image", "video", "audio", "pdf"],
            Enum.GetValues<PreviewKind>().Select(AttachmentPreview.KindName));
}
