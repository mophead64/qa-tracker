using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>Small defect-page touches: labelled severity / status, linked test case results,
/// and the "after you create it" note on the new-defect form.</summary>
[TestFixture]
public class DefectPageDetailsTests : E2ETestBase
{
    [Test]
    public async Task Defect_page_labels_its_fields_and_shows_linked_case_results()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var scenario = $"Checkout fails {suffix}";
        var dashboardUrl = await CreateProjectAsync($"E2E defect page {suffix}");
        var caseUrl = (await CreateScopeWithCasesAsync(dashboardUrl, $"Checkout {suffix}", scenario))[0];

        await Page.GotoAsync(caseUrl);
        await RetryUntil(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Mark as failed" }).ClickAsync(),
            Page.Locator("[data-tested-by]"));

        // Raise a defect from the case (prefilled and linked on save).
        var testCaseId = caseUrl[(caseUrl.LastIndexOf('/') + 1)..];
        await Page.GotoAsync($"{dashboardUrl}/defects/new?testCaseId={testCaseId}");
        await Expect(Page.Locator("[data-after-create-note]"))
            .ToContainTextAsync("Once the defect is created you can attach screenshots and other files");
        // Inside the form's card, not between the card and the buttons.
        await Expect(Page.Locator(".defect-form > .card [data-after-create-note]")).ToHaveCountAsync(1);

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } formShots)
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ScrollIntoViewIfNeededAsync();
            await Page.ScreenshotAsync(new() { Path = Path.Combine(formShots, "defect-form-note.png") });
        }
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create defect" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = scenario })).ToBeVisibleAsync();
        var defectUrl = Page.Url;

        // Severity and status chips carry labels.
        await Expect(Page.Locator("[data-field='severity']")).ToContainTextAsync("Severity");
        await Expect(Page.Locator("[data-field='severity'] summary[aria-label='Change severity']")).ToContainTextAsync("Medium");
        await Expect(Page.Locator("[data-field='status']")).ToContainTextAsync("Status");

        // The linked case shows its current result, and follows it when the result changes.
        var linked = Page.Locator("#linked-test-cases li").Filter(new() { HasTextString = scenario });
        await Expect(linked.Locator("[data-linked-result]")).ToHaveTextAsync("Failed");

        await Page.GotoAsync(caseUrl);
        await RetryUntil(
            async () =>
            {
                await Page.Locator("summary[aria-label='Change result']").ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Passed", Exact = true }).ClickAsync();
            },
            Page.Locator("summary[aria-label='Change result']").Filter(new() { HasTextString = "Passed" }));
        await Page.GotoAsync(defectUrl);
        await Expect(linked.Locator("[data-linked-result]")).ToHaveTextAsync("Passed");

        // Editing doesn't show the "after you create it" note.
        await Page.GotoAsync($"{defectUrl}/edit");
        await Expect(Page.Locator("[data-after-create-note]")).ToHaveCountAsync(0);

        if (Environment.GetEnvironmentVariable("QATRACKER_E2E_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            await Page.GotoAsync(defectUrl);
            await Page.Locator("#linked-test-cases").ScrollIntoViewIfNeededAsync();
            await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, "defect-page-details.png"), FullPage = true });
        }
    }
}
