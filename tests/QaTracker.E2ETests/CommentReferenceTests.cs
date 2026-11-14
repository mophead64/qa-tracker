using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>"#D-n" / "#T-n" in comments: the "#" picker, the link once posted, and comments in the History modals.</summary>
[TestFixture]
public class CommentReferenceTests : E2ETestBase
{
    private ILocator Box => Page.Locator("#new-comment");

    private ILocator Picker => Page.Locator("[data-mention-list]");

    private ILocator Options => Page.Locator("[data-reference-option]");

    private async Task PostCommentAsync(string text)
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add comment" }).ClickAsync();
        // Live page: wait for the comment to land and the box to clear.
        await Expect(Page.Locator("#comments li").Filter(new() { HasTextString = text })).ToBeVisibleAsync();
        await Expect(Box).ToHaveValueAsync("");
    }

    [Test]
    public async Task Hash_opens_a_picker_of_defects_and_test_cases_and_posts_a_link()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateProjectAsync($"E2E refs {suffix}");
        var scenario = $"Signs in {suffix}";
        var caseUrl = (await CreateScopeWithCasesAsync(dashboardUrl, $"Refs scope {suffix}", scenario))[0];
        var summary = $"Login broken {suffix}";
        var defectUrl = await RaiseDefectAsync(dashboardUrl, summary);

        await Page.GotoAsync(dashboardUrl);
        await Box.ClickAsync();
        await Box.PressSequentiallyAsync("Caused by #");
        await Expect(Picker).ToBeVisibleAsync();
        await Expect(Options).ToHaveCountAsync(2);
        await Expect(Options.Nth(0)).ToHaveAttributeAsync("data-reference-option", "D-1");
        await Expect(Options.Nth(1)).ToHaveAttributeAsync("data-reference-option", "T-1");

        // Narrow by title, then pick with Enter.
        await Box.PressSequentiallyAsync("login");
        await Expect(Options).ToHaveCountAsync(1);
        await Box.PressAsync("Enter");
        await Expect(Picker).ToBeHiddenAsync();
        await Expect(Box).ToHaveValueAsync("Caused by #D-1 ");
        await Expect(Page.Locator("[data-mention-mirror] mark")).ToHaveTextAsync("#D-1");

        // A typed-out ID works too; an unknown one stays plain text.
        await Box.PressSequentiallyAsync("in #T-1 (not #D-99)");
        await PostCommentAsync("Caused by");

        var comment = Page.Locator("#comments li").Filter(new() { HasTextString = "Caused by" });
        await Expect(comment.Locator("a[data-comment-ref]")).ToHaveCountAsync(2);
        var defectLink = comment.Locator("a[data-comment-ref='D-1']");
        await Expect(defectLink).ToHaveTextAsync("#D-1");
        await Expect(comment.Locator("a[data-comment-ref='T-1']")).ToHaveAttributeAsync("href", new Regex(Regex.Escape(new Uri(caseUrl).AbsolutePath.TrimStart('/')) + "$"));
        await Expect(comment).ToContainTextAsync("(not #D-99)");

        await defectLink.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = summary })).ToBeVisibleAsync();
        Assert.That(Page.Url, Is.EqualTo(defectUrl));
    }

    [Test]
    public async Task Comments_show_in_the_defect_history_and_lead_back_to_the_comment()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dashboardUrl = await CreateProjectAsync($"E2E history comments {suffix}");
        await RaiseDefectAsync(dashboardUrl, $"History comment defect {suffix}");

        var note = $"Seen again on staging {suffix}";
        await Box.FillAsync(note);
        await PostCommentAsync(note);

        await Page.GetByRole(AriaRole.Button, new() { Name = "History", Exact = true }).ClickAsync();
        var modal = Page.Locator("#history-modal");
        var entry = modal.Locator("[data-history] > li[data-history-kind='Commented']");
        await Expect(entry).ToHaveCountAsync(1);
        await Expect(entry.Locator("[data-history-comment]")).ToHaveTextAsync(note);
        // The whole line, so stray template text around the excerpt would fail it.
        await Expect(entry.Locator(".timeline-what")).ToHaveTextAsync(new Regex($@"^\s*\S.*\s+commented\s+{Regex.Escape(note)}\s+·\s+View comment\s*$", RegexOptions.Singleline));
        // Newest first: the comment came after raising the defect.
        await Expect(modal.Locator("[data-history] > li").First).ToHaveAttributeAsync("data-history-kind", "Commented");

        await entry.GetByRole(AriaRole.Link, new() { Name = "View comment" }).ClickAsync();
        await Expect(modal).Not.ToBeVisibleAsync();
        await Expect(Page.Locator("#comments li.comment-highlight")).ToContainTextAsync(note);
    }
}
