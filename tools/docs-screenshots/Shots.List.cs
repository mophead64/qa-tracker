using Microsoft.Playwright;

namespace DocsScreenshots;

/// <summary>
/// Every screenshot the docs use, in the order they're taken. Add a line here (and a method
/// below) for a new one; `shots name1 name2` re-takes just those. Shots never save anything
/// they change: forms are filled but not submitted, dialogs are cancelled.
/// </summary>
internal static partial class Shots
{
    private static readonly List<Shot> All =
    [
        new("login", s => LoginPageAsync(s, "login")),
        new("dashboard", DashboardAsync),
        new("projects", ProjectsAsync),
        new("project-switcher", ProjectSwitcherAsync),
        new("project-form", ProjectFormAsync),
        new("team", TeamAsync),
        new("test-plan", TestPlanAsync),
        new("test-plan-collapsed", TestPlanCollapsedAsync),
        new("test-case-filter", TestCaseFilterAsync),
        new("test-case", TestCaseAsync),
        new("result-menu", ResultMenuAsync),
        new("test-case-history", TestCaseHistoryAsync),
        new("link-defect", LinkDefectAsync),
        new("raise-defect", RaiseDefectAsync),
        new("defects", DefectsAsync),
        new("defect", DefectAsync),
        new("defect-dev", DefectDevAsync),
        new("defect-verify", DefectVerifyAsync),
        new("defect-history", DefectHistoryAsync),
        new("attachment-preview", AttachmentPreviewAsync),
        new("upload", UploadAsync),
        new("comments", CommentsAsync),
        new("mention-picker", MentionPickerAsync),
        new("reference-picker", ReferencePickerAsync),
        new("dashboard-actions", DashboardActionsAsync),
        new("dashboard-activity", DashboardActivityAsync),
        new("project-activity", ProjectActivityAsync),
        new("notifications", NotificationsAsync),
        new("import-preview", ImportPreviewAsync),
        new("admin", AdminAsync),
        new("tags", TagsAsync),
        new("users", UsersAsync),
        new("user-activity", UserActivityAsync),
        new("new-user", NewUserAsync),
        new("settings", SettingsAsync),
    ];

    // ---- sign-in ------------------------------------------------------------------------------

    private static async Task LoginPageAsync(Session s, string name)
    {
        var context = await s.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(s.Url("Account/Login"));
        await Session.SettleAsync(page);
        await Crop.AroundAsync(page, s.Out(name), 40, page.Locator(".card"));
        await context.CloseAsync();
    }

    // ---- projects -----------------------------------------------------------------------------

    private static async Task DashboardAsync(Session s)
    {
        var page = await s.OpenAsync("priya", $"projects/{await s.ProjectAsync()}");
        await Crop.ViewportAsync(page, s.Out("dashboard"));
    }

    private static async Task ProjectsAsync(Session s)
    {
        var page = await s.OpenAsync("priya", "projects");
        await Crop.ViewportAsync(page, s.Out("projects"));
    }

    private static async Task ProjectFormAsync(Session s)
    {
        var page = await s.OpenAsync("priya", "projects/new");
        await page.Locator("#name").FillAsync("Wrenfield Broker Portal");
        await page.Locator("#brief").FillAsync("Broker-facing portal for the new quote engine: quotes, referrals and documents.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add link" }).ClickAsync();
        await page.GetByPlaceholder("Label").Last.FillAsync("Staging");
        await page.GetByPlaceholder("https://").Last.FillAsync("https://brokers.wrenfield.example");
        await page.Locator("#testing-start").FillAsync("2026-12-07");
        await page.Locator("#testing-end").FillAsync("2026-12-11");
        await page.Locator("#dev-days").FillAsync("16");
        await page.Locator("#notes").FillAsync("Phase 2 of the Wrenfield work: the broker portal on top of the quote engine. Staging and test broker accounts are ready from 1 December.");
        await Crop.BlurAsync(page);
        await Session.SettleAsync(page);
        await Crop.ContentAsync(page, s.Out("project-form"), page.Locator("main form").First, fitWidth: true);
    }

    private static async Task TeamAsync(Session s)
    {
        var page = await s.OpenAsync("priya", $"projects/{await s.ProjectAsync()}");
        await ElementAsync(page, s.Out("team"), page.Locator("#team"));
    }

    private static async Task DashboardActionsAsync(Session s)
    {
        var page = await s.OpenAsync("priya", $"projects/{await s.ProjectAsync()}");
        await ElementAsync(page, s.Out("dashboard-actions"), page.Locator("#actions"));
    }

    private static async Task DashboardActivityAsync(Session s)
    {
        var page = await s.OpenAsync("priya", $"projects/{await s.ProjectAsync()}");
        var card = page.Locator("#activity");
        await card.ScrollIntoViewIfNeededAsync();
        // The card's entries load once it scrolls into view.
        await card.Locator("[data-history] > li").First.WaitForAsync();
        await Session.SettleAsync(page);
        await ElementAsync(page, s.Out("dashboard-activity"), card);
    }

    private static async Task ProjectActivityAsync(Session s)
    {
        var page = await s.OpenAsync("priya", $"projects/{await s.ProjectAsync()}/activity");
        await Crop.ViewportAsync(page, s.Out("project-activity"));
    }

    private static async Task CommentsAsync(Session s)
    {
        // Page 1 of 2: the newest page holds just one comment.
        var page = await s.OpenAsync("priya", $"projects/{await s.ProjectAsync()}?commentsPage=1#comments");
        await ElementAsync(page, s.Out("comments"), page.Locator("#comments"));
    }

    private static async Task NotificationsAsync(Session s)
    {
        var page = await s.OpenAsync("priya", $"projects/{await s.ProjectAsync()}");
        await page.Locator("#notification-bell > summary").ClickAsync();
        await page.Locator("[data-notif-panel] li").First.WaitForAsync();
        await Session.SettleAsync(page);
        // The panel drops over the page; hide the page itself so only the top bar and panel show.
        await WithStyleAsync(page, "main { visibility: hidden !important; }",
            () => Crop.AroundAsync(page, s.Out("notifications"), 16,
                page.Locator("#notification-bell > summary"), page.Locator("[data-notif-panel]"),
                page.Locator("[data-current-user]").Locator("xpath=ancestor::summary[1]")));
        await page.Keyboard.PressAsync("Escape");
    }

    // ---- test cases ---------------------------------------------------------------------------

    private static async Task TestPlanAsync(Session s)
    {
        var page = await s.OpenAsync("priya", $"projects/{await s.ProjectAsync()}/test-cases");
        await Crop.ViewportAsync(page, s.Out("test-plan"));
    }

    private static async Task TestPlanCollapsedAsync(Session s)
    {
        var page = await s.OpenAsync("priya", $"projects/{await s.ProjectAsync()}/test-cases");
        await page.GetByRole(AriaRole.Button, new() { Name = "Collapsed", Exact = true }).ClickAsync();
        await Session.SettleAsync(page);
        try
        {
            await Crop.ViewportAsync(page, s.Out("test-plan-collapsed"));
        }
        finally
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Expanded", Exact = true }).ClickAsync();
            await Session.SettleAsync(page);
        }
    }

    private static async Task TestCaseFilterAsync(Session s)
    {
        var page = await s.OpenAsync("priya", $"projects/{await s.ProjectAsync()}/test-cases");
        var toggle = page.Locator("summary[aria-label='Filter test cases']");
        await toggle.ClickAsync();
        await Session.SettleAsync(page);
        await Crop.AroundBoxesAsync(page, s.Out("test-case-filter"), 24,
        [
            await Crop.TextBoxAsync(page.Locator("main h1")),
            await Crop.BoxAsync(page.GetByRole(AriaRole.Link, new() { Name = "New scope" })),
            await Crop.BoxAsync(page.Locator("details[open] > summary[aria-label='Filter test cases'] + div")),
        ]);
        await page.Keyboard.PressAsync("Escape");
    }

    private static async Task TestCaseAsync(Session s)
    {
        var page = await s.OpenAsync("priya", await s.TestCasePathAsync(Hl.PromoCase));
        await Crop.ContentAsync(page, s.Out("test-case"), page.Locator("#comments"));
    }

    private static async Task ProjectSwitcherAsync(Session s)
    {
        var page = await s.OpenAsync("priya", "projects");
        await page.Locator("header details[data-dropdown] > summary").First.ClickAsync();
        await Session.SettleAsync(page);
        await Crop.AroundAsync(page, s.Out("project-switcher"), 16,
            page.Locator("header details[data-dropdown] > summary").First, page.Locator("[data-project-switcher]"));
    }

    private static async Task ResultMenuAsync(Session s)
    {
        var page = await s.OpenAsync("priya", await s.TestCasePathAsync(Hl.PromoCase));
        var toggle = page.Locator("summary[aria-label='Change result']");
        await toggle.ClickAsync();
        await Session.SettleAsync(page);
        await Crop.AroundAsync(page, s.Out("result-menu"), 24,
            page.Locator("main h1").Locator("xpath=../.."), page.Locator("details[open] > summary[aria-label='Change result'] + div"));
        await page.Keyboard.PressAsync("Escape");
    }

    private static async Task TestCaseHistoryAsync(Session s)
    {
        var page = await s.OpenAsync("priya", await s.TestCasePathAsync(Hl.PromoCase));
        await HistoryAsync(page, s.Out("test-case-history"));
    }

    private static async Task LinkDefectAsync(Session s)
    {
        var page = await s.OpenAsync("priya", await s.TestCasePathAsync("Screen readers announce form errors"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Link or create defect" }).ClickAsync();
        var dialog = page.Locator("#link-defects-modal");
        await dialog.WaitForAsync();
        await DialogAsync(page, s.Out("link-defect"), dialog);
        await page.Keyboard.PressAsync("Escape");
    }

    private static async Task RaiseDefectAsync(Session s)
    {
        var testCaseId = await s.TestCaseAsync("Screen readers announce form errors");
        var page = await s.OpenAsync("priya", $"projects/{await s.ProjectAsync()}/defects/new?testCaseId={testCaseId}");
        await page.GetByLabel("Summary").FillAsync("Form errors are announced twice by screen readers");
        await page.GetByLabel("Expected results").FillAsync("Each error is announced once, and focus moves to the first field with an error");
        await page.GetByLabel("Actual results").FillAsync("VoiceOver reads every error twice: once from the field and once from the error summary");
        await Crop.BlurAsync(page);
        await Session.SettleAsync(page);
        await Crop.ContentAsync(page, s.Out("raise-defect"), page.Locator("main form").First, fitWidth: true);
    }

    private static async Task ImportPreviewAsync(Session s)
    {
        var page = await s.OpenAsync("priya", $"projects/{await s.ProjectAsync()}/test-cases");
        var export = await page.Context.APIRequest.GetAsync(s.Url($"projects/{await s.ProjectAsync()}/test-cases/export.csv"));
        var csv = await export.TextAsync();

        // A bulk edit in a spreadsheet: one case gains a step, two cases are new, plus a new scope.
        // Rows carry the Scope ID / Test Case ID columns (blank for the new ones).
        csv = Replace(csv, ",,,,,Expected: delivery shows £0.00,",
            ",,,,,3. Switch to next-day delivery (£6.95),\n,,,,,\"Expected: delivery shows £0.00 for both standard and next-day\",");
        // Rows for a scope must stay together, so the new Payment case goes before the next scope.
        var delivery = System.Text.RegularExpressions.Regex.Match(csv, "\n(S-\\d+),Delivery & collection,").Value;
        csv = ReplaceFirst(csv, delivery, "\n" + string.Join("\n",
            ",Payment,Functional,,Pay with a saved card after changing the billing address,1. Sign in as the saved card customer and change the billing address,",
            ",,,,,2. Pay with the saved card,",
            ",,,,,\"Expected: the payment goes through, and the order has the new address\",") + delivery);
        csv = csv.TrimEnd('\n') + "\n" + string.Join("\n",
            ",Gift cards,Functional,,Pay for an order with a gift card,1. Apply gift card 6006 4911 2233 4455 at the payment step,",
            ",,,,,Expected: the balance covers the order and no card is needed,",
            ",Gift cards,Functional,,A gift card and a promo code can be used together,1. Apply WELCOME10,",
            ",,,,,2. Pay the rest with a gift card,",
            ",,,,,Expected: the discount applies first and the gift card covers the remainder,") + "\n";

        await page.GotoAsync(s.Url($"projects/{await s.ProjectAsync()}/test-cases/import"));
        await page.Locator("#file").SetInputFilesAsync(new FilePayload
        {
            Name = "harbourline-test-plan.csv",
            MimeType = "text/csv",
            Buffer = System.Text.Encoding.UTF8.GetBytes(csv),
        });
        await page.GetByRole(AriaRole.Button, new() { Name = "Validate" }).ClickAsync();
        var import = page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Import ") });
        try
        {
            await import.WaitForAsync(new() { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            await File.WriteAllTextAsync(Path.Combine(s.SamplesDir, "import-debug.csv"), csv);
            await Crop.ContentAsync(page, Path.Combine(s.SamplesDir, "import-debug.png"), page.Locator("main > div"));
            throw new InvalidOperationException($"The import preview didn't appear — see import-debug.png / .csv in {s.SamplesDir}");
        }
        await Session.SettleAsync(page);
        await Crop.ContentAsync(page, s.Out("import-preview"), import);
    }

    private static string Replace(string text, string find, string with) =>
        text.Contains(find) ? text.Replace(find, with) : throw new InvalidOperationException($"Export no longer contains: {find}");

    private static string ReplaceFirst(string text, string find, string with)
    {
        var at = text.IndexOf(find, StringComparison.Ordinal);
        return at < 0
            ? throw new InvalidOperationException($"Export no longer contains: {find}")
            : string.Concat(text.AsSpan(0, at), with, text.AsSpan(at + find.Length));
    }

    // ---- defects ------------------------------------------------------------------------------

    private static async Task DefectsAsync(Session s)
    {
        var page = await s.OpenAsync("priya", $"projects/{await s.ProjectAsync()}/defects");
        await Crop.ViewportAsync(page, s.Out("defects"));
    }

    private static async Task DefectAsync(Session s)
    {
        var page = await s.OpenAsync("priya", await s.DefectPathAsync(2));
        await Crop.ContentAsync(page, s.Out("defect"), page.Locator("#comments"));
    }

    /// <summary>Marcus (a developer) on a defect sent back to him: "Start fixing", and his queue.</summary>
    private static async Task DefectDevAsync(Session s)
    {
        var page = await s.OpenAsync("marcus", await s.DefectPathAsync(6));
        await DefectHeaderAsync(page, s.Out("defect-dev"));
    }

    /// <summary>Priya (QA) on a fix waiting for her: "Not fixed" / "Fixed".</summary>
    private static async Task DefectVerifyAsync(Session s)
    {
        var page = await s.OpenAsync("priya", await s.DefectPathAsync(2));
        await DefectHeaderAsync(page, s.Out("defect-verify"));
    }

    private static async Task DefectHistoryAsync(Session s)
    {
        var page = await s.OpenAsync("priya", await s.DefectPathAsync(6));
        await HistoryAsync(page, s.Out("defect-history"));
    }

    private static async Task AttachmentPreviewAsync(Session s)
    {
        var page = await s.OpenAsync("priya", await s.DefectPathAsync(2));
        await page.Locator("button[aria-label='Preview promo-code-expired.png']").ClickAsync();
        await page.Locator("dialog[open] img").WaitForAsync();
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('dialog[open] img')].every(i => i.complete && i.naturalWidth > 0)");
        await Crop.BlurAsync(page);
        await Session.SettleAsync(page);
        await Crop.ViewportAsync(page, s.Out("attachment-preview"));
        await page.Keyboard.PressAsync("Escape");
    }

    private static async Task UploadAsync(Session s)
    {
        var page = await s.OpenAsync("priya", await s.DefectPathAsync(8));
        await page.Locator("#files [data-upload-open]").ClickAsync();
        var dialog = page.Locator("dialog[data-upload-dialog][open]");
        await dialog.WaitForAsync();
        var samples = s.SamplesDir;
        await dialog.Locator("[data-upload-input]").SetInputFilesAsync(new[]
        {
            Path.Combine(samples, "basket-second-device.png"),
            Path.Combine(samples, "network-trace.har"),
        });
        await dialog.Locator("[data-upload-description]").FillAsync("Phone basket after signing in, plus the network trace");
        await DialogAsync(page, s.Out("upload"), dialog);
        await dialog.Locator("[data-upload-cancel]").ClickAsync();
    }

    private static async Task MentionPickerAsync(Session s)
    {
        var page = await s.OpenAsync("priya", await s.DefectPathAsync(5));
        var box = page.Locator("#comments textarea[data-mention-input]");
        await box.ScrollIntoViewIfNeededAsync();
        await box.ClickAsync();
        await box.PressSequentiallyAsync("Same cut-off for every store, thanks @", new() { Delay = 20 });
        var list = page.Locator("#comments [data-mention-list]:not([hidden])");
        await list.WaitForAsync();
        await page.WaitForTimeoutAsync(200);
        await Crop.AroundAsync(page, s.Out("mention-picker"), 24, box, list);
        await box.FillAsync("");
    }

    private static async Task ReferencePickerAsync(Session s)
    {
        var page = await s.OpenAsync("priya", await s.DefectPathAsync(2));
        var box = page.Locator("#comments textarea[data-mention-input]");
        // Centred, so the list (taller than the @ picker) fits below the box.
        await box.EvaluateAsync("el => el.scrollIntoView({ block: 'center' })");
        await box.ClickAsync();
        await box.PressSequentiallyAsync("Probably the same validation gap as #promo", new() { Delay = 20 });
        var list = page.Locator("#comments [data-mention-list]:not([hidden])");
        await list.WaitForAsync();
        await page.WaitForTimeoutAsync(200);
        await Crop.AroundAsync(page, s.Out("reference-picker"), 24, box, list);
        await box.FillAsync("");
    }

    // ---- admin and settings -------------------------------------------------------------------

    private static async Task AdminAsync(Session s)
    {
        var page = await s.OpenAsync("priya", "admin");
        await Crop.ViewportAsync(page, s.Out("admin"));
    }

    private static async Task TagsAsync(Session s)
    {
        var page = await s.OpenAsync("priya", "admin/tags");
        await Crop.ViewportAsync(page, s.Out("tags"));
    }

    private static async Task UsersAsync(Session s)
    {
        var page = await s.OpenAsync("priya", "admin/users");
        await Crop.ViewportAsync(page, s.Out("users"));
    }

    private static async Task UserActivityAsync(Session s)
    {
        // A QA teammate's work across every project: tiles, the per-project breakdown and the
        // first row of drill-down panels.
        var page = await s.OpenAsync("priya", $"admin/users/{await s.UserIdAsync("tom")}/activity");
        await Crop.ContentAsync(page, s.Out("user-activity"), page.Locator("[data-activity-panel='raised']"));
    }

    private static async Task NewUserAsync(Session s)
    {
        // Filled in with a generated password, never submitted.
        var page = await s.OpenAsync("priya", "admin/users/new");
        await page.GetByLabel("Full name").FillAsync("Sam Whitfield");
        await page.GetByLabel("Email").FillAsync("sam.whitfield@example.com");
        await page.GetByRole(AriaRole.Button, new() { Name = "Generate a strong password" }).ClickAsync();
        await page.WaitForTimeoutAsync(300); // the strength meter's colour transition
        await Crop.ContentAsync(page, s.Out("new-user"), page.Locator("main form").First, fitWidth: true);
    }

    private static async Task SettingsAsync(Session s)
    {
        var page = await s.OpenAsync("priya", "settings");
        var cards = page.Locator("main .card");
        var targets = new List<ILocator> { page.Locator("main h1") };
        for (var i = 0; i < await cards.CountAsync(); i++)
        {
            targets.Add(cards.Nth(i));
        }

        await Crop.AroundAsync(page, s.Out("settings"), 28, targets.ToArray());
    }

    // ---- shared pieces ------------------------------------------------------------------------

    /// <summary>A defect's header: breadcrumb, title, severity / status / workflow buttons, the
    /// Previous / Next queue and the assignee line.</summary>
    private static async Task DefectHeaderAsync(IPage page, string path)
    {
        var header = page.Locator("main h1").Locator("xpath=../..");
        var assigned = page.Locator("main").GetByText("Assigned to", new() { Exact = true });
        await Crop.AroundAsync(page, path, 24, header, assigned);
    }

    /// <summary>Opens the History dialog and waits for its timeline (it's fetched when opened).</summary>
    private static async Task HistoryAsync(IPage page, string path)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "History", Exact = true }).ClickAsync();
        var dialog = page.Locator("#history-modal");
        await dialog.Locator("time").First.WaitForAsync();
        await DialogAsync(page, path, dialog);
        await page.Keyboard.PressAsync("Escape");
    }

    /// <summary>A dialog with a margin of the dimmed page around it, and no focus ring on its
    /// autofocused button.</summary>
    private static async Task DialogAsync(IPage page, string path, ILocator dialog)
    {
        await Crop.BlurAsync(page);
        await Session.SettleAsync(page);
        // Only the dialog and its dimmed backdrop: page text showing round the edges is noise.
        await WithStyleAsync(page, "body * { visibility: hidden !important; } dialog[open], dialog[open] * { visibility: visible !important; }",
            () => Crop.AroundAsync(page, path, 20, dialog));
    }

    /// <summary>Runs <paramref name="action"/> with some extra CSS applied to the page.</summary>
    private static async Task WithStyleAsync(IPage page, string css, Func<Task> action)
    {
        await page.EvaluateAsync("css => { const s = document.createElement('style'); s.id = 'docs-shot-style'; s.textContent = css; document.head.appendChild(s); }", css);
        try
        {
            await action();
        }
        finally
        {
            await page.EvaluateAsync("() => document.getElementById('docs-shot-style')?.remove()");
        }
    }

    /// <summary>One element, with the viewport grown first so tall cards aren't cut off.</summary>
    private static async Task ElementAsync(IPage page, string path, ILocator element)
    {
        await page.SetViewportSizeAsync(Session.Width, 2400);
        await Session.SettleAsync(page);
        await Crop.AroundAsync(page, path, 24, element);
        await page.SetViewportSizeAsync(Session.Width, Session.Height);
    }
}
