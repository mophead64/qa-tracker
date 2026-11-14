using QaTracker.Web.Attachments;

namespace QaTracker.UnitTests.Attachments;

/// <summary>Sorting attachments into the attachments filter's types.</summary>
public sealed class AttachmentCategoryTests
{
    [Theory]
    [InlineData("screen.PNG", null, AttachmentCategory.Image)]
    [InlineData("diagram.svg", null, AttachmentCategory.Image)]
    [InlineData("walkthrough.mov", null, AttachmentCategory.Video)]
    [InlineData("call.m4a", null, AttachmentCategory.Audio)]
    [InlineData("spec.pdf", null, AttachmentCategory.Document)]
    [InlineData("results.xlsx", null, AttachmentCategory.Document)]
    [InlineData("notes.txt", null, AttachmentCategory.Document)]
    [InlineData("bundle.zip", "application/zip", AttachmentCategory.Other)]
    [InlineData("no-extension", null, AttachmentCategory.Other)]
    public void Extension_decides_the_type(string fileName, string? contentType, AttachmentCategory expected) =>
        Assert.Equal(expected, AttachmentCategories.Of(fileName, contentType));

    [Theory]
    [InlineData("capture", "image/x-custom", AttachmentCategory.Image)]
    [InlineData("clip", "video/x-matroska", AttachmentCategory.Video)]
    [InlineData("memo", "audio/x-custom", AttachmentCategory.Audio)]
    [InlineData("readme", "text/plain", AttachmentCategory.Document)]
    [InlineData("report", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", AttachmentCategory.Document)]
    [InlineData("blob", "application/octet-stream", AttachmentCategory.Other)]
    public void Without_a_known_extension_the_content_type_decides(string fileName, string contentType, AttachmentCategory expected) =>
        Assert.Equal(expected, AttachmentCategories.Of(fileName, contentType));

    [Fact]
    public void Query_values_round_trip_and_unknown_values_mean_no_filter()
    {
        foreach (var category in AttachmentCategories.All)
        {
            Assert.Equal(category, AttachmentCategories.Parse(AttachmentCategories.Value(category)));
        }

        Assert.Equal("docs", AttachmentCategories.Value(AttachmentCategory.Document));
        Assert.Equal(AttachmentCategory.Image, AttachmentCategories.Parse(" IMAGE "));
        Assert.Null(AttachmentCategories.Parse("all"));
        Assert.Null(AttachmentCategories.Parse(null));
    }
}
