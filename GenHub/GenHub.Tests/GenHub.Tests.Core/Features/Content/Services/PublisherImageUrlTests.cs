using GenHub.Features.Content.Services;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Tests recognizing and repairing publisher card image links in <see cref="PublisherImageUrl"/>.
/// </summary>
public sealed class PublisherImageUrlTests
{
    private const string DriveId = "18onwpszLRsc62P92f7biat6ORbXtL7u4";
    private const string DriveDirect = "https://drive.usercontent.google.com/download?id=" + DriveId + "&export=view";

    /// <summary>
    /// Google Drive viewer and open links are rewritten to the direct download form that serves image bytes.
    /// </summary>
    /// <param name="url">The pasted Drive link.</param>
    [Theory]
    [InlineData("https://drive.google.com/file/d/" + DriveId + "/view")]
    [InlineData("https://drive.google.com/file/d/" + DriveId + "/view?usp=sharing")]
    [InlineData("https://drive.google.com/file/d/" + DriveId + "/preview")]
    [InlineData("https://drive.google.com/file/d/" + DriveId)]
    [InlineData("https://drive.google.com/open?id=" + DriveId)]
    [InlineData("https://drive.google.com/uc?export=view&id=" + DriveId)]
    [InlineData("https://drive.google.com/file/d/" + DriveId + "/")]
    [InlineData("https://drive.google.com/file/d/" + DriveId + "/view/")]
    [InlineData("https://drive.google.com/u/0/file/d/" + DriveId + "/view?usp=drive_link")]
    [InlineData("https://DRIVE.GOOGLE.COM/file/d/" + DriveId + "/VIEW")]
    public void Normalize_DriveViewerLink_RewritesToDirectImage(string url)
    {
        Assert.Equal(DriveDirect, PublisherImageUrl.Normalize(url));
    }

    /// <summary>
    /// A Drive resource key, needed for some older shared links, is carried over to the direct link.
    /// </summary>
    [Fact]
    public void Normalize_DriveLinkWithResourceKey_KeepsResourceKey()
    {
        Assert.Equal(
            DriveDirect + "&resourcekey=0-AbC_dEf",
            PublisherImageUrl.Normalize("https://drive.google.com/file/d/" + DriveId + "/view?usp=sharing&resourcekey=0-AbC_dEf"));
    }

    /// <summary>
    /// Links that only look like Drive links are never rewritten: other hosts, plain HTTP, user info, or another port.
    /// </summary>
    /// <param name="url">The adversarial link.</param>
    [Theory]
    [InlineData("https://drive.google.com.evil.com/file/d/" + DriveId + "/view")]
    [InlineData("https://evil.com/drive.google.com/file/d/" + DriveId + "/view")]
    [InlineData("https://evildrive.google.com/file/d/" + DriveId + "/view")]
    [InlineData("http://drive.google.com/file/d/" + DriveId + "/view")]
    [InlineData("https://user:pass@drive.google.com/file/d/" + DriveId + "/view")]
    [InlineData("https://drive.google.com:8443/file/d/" + DriveId + "/view")]
    [InlineData("https://drive.google.com/file/d/short/view")]
    [InlineData("https://drive.google.com/file/d/" + DriveId + "/view/extra")]
    public void Normalize_LookalikeDriveLink_IsUnchanged(string url)
    {
        Assert.Equal(url, PublisherImageUrl.Normalize(url));
    }

    /// <summary>
    /// Links that are not Drive viewer links are left unchanged.
    /// </summary>
    /// <param name="url">The link.</param>
    [Theory]
    [InlineData("https://example.com/card.png")]
    [InlineData("https://drive.google.com/drive/folders/" + DriveId)]
    [InlineData("http://drive.google.com/file/d/" + DriveId + "/view")]
    [InlineData("avares://GenHub/Assets/Covers/usa-cover.jpg")]
    [InlineData(null)]
    public void Normalize_OtherLink_IsUnchanged(string? url)
    {
        Assert.Equal(url, PublisherImageUrl.Normalize(url));
    }

    /// <summary>
    /// Signed Discord attachment links are recognized as expiring. Other Discord and image links are not.
    /// </summary>
    /// <param name="url">The link.</param>
    /// <param name="expected">Whether the link expires.</param>
    [Theory]
    [InlineData("https://cdn.discordapp.com/attachments/1/2/card.png?ex=6720b1c3&is=671f6043&hm=abcdef&", true)]
    [InlineData("https://media.discordapp.net/attachments/1/2/card.gif?ex=6720b1c3&is=671f6043&hm=abcdef&=&width=400", true)]
    [InlineData("https://cdn.discordapp.com/ephemeral-attachments/1/2/card.png?ex=1&is=2&hm=3", true)]
    [InlineData("https://cdn.discordapp.com/attachments/1/2/card.png", false)]
    [InlineData("https://cdn.discordapp.com/avatars/1/abc.png?size=128", false)]
    [InlineData("https://example.com/card.png?ex=1&hm=2", false)]
    public void IsExpiringDiscordAttachment_RecognizesSignedLinks(string url, bool expected)
    {
        Assert.Equal(expected, PublisherImageUrl.IsExpiringDiscordAttachment(url));
    }
}
