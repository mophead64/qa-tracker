using Microsoft.Playwright;

namespace QaTracker.E2ETests;

/// <summary>System-wide project tags: dashboard add/remove, list + switcher badges, the System
/// settings tags page, and the history they leave.</summary>
[TestFixture]
public class TagTests : E2ETestBase
{
    [Test]
    public async Task A_tag_made_on_one_project_is_offered_on_others_and_managed_from_system_settings()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        var first = $"Tagged one {token}";
        var second = $"Tagged two {token}";
        // The tag carries its own token, so searching for it can only match through the tag.
        var tagToken = Guid.NewGuid().ToString("N")[..8];
        var tag = $"Stack {tagToken}";
        var renamed = $"Platform {tagToken}";

        // Type a new tag on the first project's dashboard. The box starts readonly (no autofill),
        // and adding updates the page in place: no reload, and the switcher follows.
        var firstUrl = await CreateProjectAsync(first);
        await Page.EvaluateAsync("() => window.__liveMarker = 'still here'");
        await Page.Locator("summary[aria-label='Add tag']").ClickAsync();
        await Expect(Page.Locator("#new-tag-name")).Not.ToBeEditableAsync();
        // A new tag takes the colour picked beside the name.
        await Page.Locator("[data-project-tags] [data-colour-picker]").GetByTitle("Purple", new() { Exact = true }).ClickAsync();
        await AddTagAsync(tag);
        var tags = Page.Locator("[data-project-tags] [data-tag]");
        await Expect(tags).ToHaveTextAsync([tag]);
        await Expect(tags).ToHaveClassAsync(new Regex("badge-purple"));
        Assert.That(await Page.EvaluateAsync<string?>("() => window.__liveMarker ?? null"), Is.EqualTo("still here"));
        await Expect(Page.Locator("#project-switcher form[action='projects/switch'] button")
            .Filter(new() { HasTextString = first }).Locator("[data-tag]")).ToHaveTextAsync([tag]);

        // The second project offers it as an existing tag.
        var secondUrl = await CreateProjectAsync(second);
        await OpenAddTagAsync();
        var pick = Page.Locator("[data-tag-picks] button").Filter(new() { HasTextString = tag });
        await Expect(pick).ToHaveTextAsync(tag); // just the tag — no project count
        await pick.ClickAsync();
        await Expect(tags).ToHaveTextAsync([tag]);

        // Both show it on All projects — found by searching for the tag — and in the switcher.
        await Page.GotoAsync($"{BaseUrl}/projects?q={tagToken}");
        foreach (var name in new[] { first, second })
        {
            await Expect(Page.Locator("li").Filter(new() { HasTextString = name }).Locator("[data-tags] [data-tag]"))
                .ToHaveTextAsync([tag]);
        }
        await Page.Locator("header summary").First.ClickAsync();
        await Expect(Page.Locator("header form[action='projects/switch'] button")
            .Filter(new() { HasTextString = first }).Locator("[data-tag]")).ToHaveTextAsync([tag]);

        // System settings: where it's used, then rename + recolour it.
        await Page.GotoAsync($"{BaseUrl}/admin/tags");
        var row = Page.Locator($"[data-tag-row='{tag}']");
        await Expect(row.Locator("[data-tag-usage]")).ToHaveTextAsync("Used on 2 projects");
        await Expect(row.Locator("[data-tag]")).ToHaveClassAsync(new Regex("badge-purple"));
        await row.Locator("[data-tag-usage]").ClickAsync();
        var usage = Page.Locator("dialog[open][data-tag-projects]");
        await Expect(usage.Locator("a")).ToHaveTextAsync([first, second]);
        await usage.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        await Expect(usage).ToHaveCountAsync(0);
        // Its name opens the edit modal.
        await row.Locator("[data-tag-edit-open]").ClickAsync();
        var editor = Page.Locator("dialog[open][data-tag-edit-dialog]");
        await Expect(editor).ToContainTextAsync("Changes show on all 2 projects with this tag.");
        await editor.Locator("input[name='tagName']").FillAsync(renamed);
        await editor.Locator("[data-colour-picker]").GetByTitle("Blue", new() { Exact = true }).ClickAsync();
        await editor.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        var renamedRow = Page.Locator($"[data-tag-row='{renamed}']");
        await Expect(renamedRow).ToContainTextAsync("Saved");
        await Expect(renamedRow.Locator("[data-tag]")).ToHaveClassAsync(new Regex("badge-blue"));

        // A clashing name is refused, with the error above the New tag form.
        await Page.Locator("#new-tag").ClickAsync(); // readonly until focused (no autofill)
        await Page.Locator("#new-tag").FillAsync(renamed.ToUpperInvariant());
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create tag" }).ClickAsync();
        await Expect(Page.Locator("[data-tag-error]")).ToContainTextAsync("already a tag");

        // Take it off the second project from its dashboard.
        await Page.GotoAsync(secondUrl);
        await Expect(tags).ToHaveTextAsync([renamed]);
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Remove tag {renamed}" }).ClickAsync();
        await Expect(tags).ToHaveCountAsync(0);

        // Delete it outright from the first project's dashboard, through the same modal.
        await Page.GotoAsync(firstUrl);
        await Page.Locator("[data-project-tags] [data-tag-edit-open]").ClickAsync();
        var dashboardEditor = Page.Locator("dialog[open][data-tag-edit-dialog]");
        await Expect(dashboardEditor).ToContainTextAsync("Changes show on the 1 project with this tag.");
        await dashboardEditor.Locator("[data-tag-delete] summary").ClickAsync();
        await dashboardEditor.GetByRole(AriaRole.Button, new() { Name = "Delete tag" }).ClickAsync();
        await Expect(tags).ToHaveCountAsync(0);
        await Page.GotoAsync($"{BaseUrl}/admin/tags");
        await Expect(renamedRow).ToHaveCountAsync(0);

        // Each project's history tells the story.
        await Page.GotoAsync($"{firstUrl}/activity");
        var firstHistory = Page.Locator("[data-history] > li");
        await Expect(firstHistory.Nth(0)).ToContainTextAsync($"deleted the tag {renamed}, removing it from the project");
        await Expect(firstHistory.Nth(1)).ToContainTextAsync($"renamed the project's tag {tag} to {renamed}");
        await Expect(firstHistory.Nth(2)).ToContainTextAsync($"tagged the project {tag}");

        await Page.GotoAsync($"{secondUrl}/activity");
        await Expect(Page.Locator("[data-history] > li").Nth(0)).ToContainTextAsync($"removed the tag {renamed} from the project");
    }

    [Test]
    public async Task A_bad_tag_name_reopens_the_picker_with_the_error()
    {
        await CreateProjectAsync($"Tag error {Guid.NewGuid():N}");
        await AddTagAsync(new string('x', 41));

        await Expect(Page.Locator("[data-project-tags] details[open] [data-tag-error]")).ToContainTextAsync("at most 40");
        await Expect(Page.Locator("#new-tag-name")).ToHaveValueAsync(new string('x', 41));
    }

    private async Task OpenAddTagAsync()
    {
        if (!await Page.Locator("#new-tag-name").IsVisibleAsync())
        {
            await Page.Locator("summary[aria-label='Add tag']").ClickAsync();
        }
        await Expect(Page.Locator("#new-tag-name")).ToBeVisibleAsync();
    }

    private async Task AddTagAsync(string name)
    {
        await OpenAddTagAsync();
        // Readonly until focused (no autofill). Past the input's maxlength the browser stops
        // typing, so set the value directly.
        await Page.Locator("#new-tag-name").ClickAsync();
        await Expect(Page.Locator("#new-tag-name")).ToBeEditableAsync();
        await Page.Locator("#new-tag-name").EvaluateAsync("(el, v) => { el.removeAttribute('maxlength'); el.value = v; }", name);
        await Page.Locator("[data-project-tags] form[action$='/tags']").First
            .GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
    }
}
