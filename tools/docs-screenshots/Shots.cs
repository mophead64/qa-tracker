using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using QaTracker.Web.Data;

namespace DocsScreenshots;

/// <summary>
/// Captures the documentation screenshots into docs/screenshots. Every shot is dark mode at a
/// 1440×900 viewport with a 2× device scale factor (crisp on high-DPI screens); many are cropped
/// to the part of the page the docs talk about (see <see cref="Crop"/>). The shots themselves are
/// listed in Shots.List.cs.
/// </summary>
internal static partial class Shots
{
    public static async Task RunAsync(DocsOptions options, string[] only)
    {
        Directory.CreateDirectory(options.OutputDir);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var session = new Session(options, browser);

        var selected = only.Length == 0 ? All : All.Where(s => only.Contains(s.Name)).ToList();
        if (selected.Count == 0)
        {
            Console.WriteLine("No matching shots. Available: " + string.Join(", ", All.Select(s => s.Name)));
            return;
        }

        foreach (var shot in selected)
        {
            Console.WriteLine($"  {shot.Name}");
            // Opening the bell marks everything read; put the badge back before every shot.
            await session.ResetUnreadAsync();
            await shot.Run(session);
        }
    }

    public static async Task RunSsoOnlyLoginAsync(DocsOptions options)
    {
        Directory.CreateDirectory(options.OutputDir);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var session = new Session(options, browser);
        await LoginPageAsync(session, "login-sso-only");
    }

    private sealed record Shot(string Name, Func<Session, Task> Run);
}

/// <summary>Signed-in browser pages (one per demo person) plus id lookups for page URLs.</summary>
internal sealed class Session(DocsOptions options, IBrowser browser) : IAsyncDisposable
{
    public const int Width = 1440;
    public const int Height = 900;

    private readonly Dictionary<string, IPage> pages = new();

    public string SamplesDir => options.SamplesDir;

    public string Url(string path) => $"{options.BaseUrl}/{path.TrimStart('/')}";

    public string Out(string name) => Path.Combine(options.OutputDir, name + ".png");

    public async Task<IBrowserContext> NewContextAsync() => await browser.NewContextAsync(new()
    {
        ViewportSize = new() { Width = Width, Height = Height },
        DeviceScaleFactor = 2,
        ColorScheme = ColorScheme.Dark,
        Locale = "en-GB",
        TimezoneId = "Europe/London",
    });

    /// <summary>A page signed in as a demo person ("priya", "marcus" — local accounts only).</summary>
    public async Task<IPage> AsAsync(string person)
    {
        if (pages.TryGetValue(person, out var existing))
        {
            await existing.SetViewportSizeAsync(Width, Height);
            return existing;
        }

        var cast = DemoSeeder.Cast.First(p => p.Key == person);
        var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        // Leaving a half-filled form raises "unsaved changes" — always leave.
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await page.GotoAsync(Url("Account/Login"));
        await page.GetByLabel("Email").FillAsync(cast.Email);
        await page.GetByLabel("Password").FillAsync(DocsOptions.DemoPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(u => !u.Contains("/Account/Login"));

        // Signing in stamps today's date as the last sign-in; put back the story's.
        await using var db = Db();
        await db.Users.Where(u => u.Email == cast.Email).ExecuteUpdateAsync(s => s.SetProperty(
            u => u.LastLoginUtc, cast.LastLogin == null ? null : DemoClock.Parse(cast.LastLogin)));

        pages[person] = page;
        return page;
    }

    /// <summary>Opens <paramref name="path"/> as <paramref name="person"/> and lets it settle.</summary>
    public async Task<IPage> OpenAsync(string person, string path)
    {
        var page = await AsAsync(person);
        await page.GotoAsync(Url(path));
        await SettleAsync(page);
        return page;
    }

    /// <summary>Waits for the network and fonts, and parks the mouse on an empty bit of the top bar
    /// so no hover state or tooltip is showing.</summary>
    public static async Task SettleAsync(IPage page)
    {
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.EvaluateAsync("document.fonts.ready");
        await page.Mouse.MoveAsync(1000, 38);
        await page.WaitForTimeoutAsync(150);
    }

    // ---- data access --------------------------------------------------------------------------

    private ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(options.ConnectionString).Options);

    public async Task<Guid> ProjectAsync(string name = Hl.Project)
    {
        await using var db = Db();
        return await db.Projects.Where(p => p.Name == name).Select(p => p.Id).FirstAsync();
    }

    public async Task<Guid> DefectAsync(int number, string project = Hl.Project)
    {
        var projectId = await ProjectAsync(project);
        await using var db = Db();
        return await db.Defects.Where(d => d.ProjectId == projectId && d.Number == number).Select(d => d.Id).FirstAsync();
    }

    public async Task<Guid> TestCaseAsync(string scenario)
    {
        await using var db = Db();
        return await db.TestCases.Where(t => t.Scenario == scenario).Select(t => t.Id).FirstAsync();
    }

    public async Task<string> DefectPathAsync(int number) => $"projects/{await ProjectAsync()}/defects/{await DefectAsync(number)}";

    public async Task<string> TestCasePathAsync(string scenario) => $"projects/{await ProjectAsync()}/test-cases/{await TestCaseAsync(scenario)}";

    /// <summary>Marks everyone's notifications from the story's last day unread again — opening
    /// the bell marks them read, and the badge belongs in the shots.</summary>
    public async Task ResetUnreadAsync()
    {
        await using var db = Db();
        var cutoff = DemoClock.Parse(Hl.UnreadCutoff);
        await db.Notifications
            .Where(n => n.CreatedUtc >= cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadUtc, (DateTimeOffset?)null));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var page in pages.Values)
        {
            await page.Context.CloseAsync();
        }
    }
}

/// <summary>Screenshot helpers: the viewport, a tall page, or a crop around elements.</summary>
internal static class Crop
{
    /// <summary>Right edge of the side nav (16rem) and bottom of the top bar, in CSS px.</summary>
    private const float NavWidth = 256;
    private const float TopBarHeight = 57;

    public static Task ViewportAsync(IPage page, string path) => page.ScreenshotAsync(new() { Path = path });

    /// <summary>Drops focus, so no focus ring shows on whatever was last typed in or autofocused.</summary>
    public static Task BlurAsync(IPage page) =>
        page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");

    /// <summary>The main column only (no side nav, top bar or "On this page" rail), from the top of
    /// the page down to the bottom of <paramref name="until"/>. The app scrolls inside &lt;main&gt;,
    /// not the document, so the viewport is grown to fit first.</summary>
    /// <param name="fitWidth">Stop at <paramref name="until"/>'s right edge rather than the column's
    /// (for a form narrower than the page).</param>
    public static async Task ContentAsync(IPage page, string path, ILocator until, int pad = 24, bool fitWidth = false)
    {
        var height = await page.EvaluateAsync<int>(
            "() => { const m = document.querySelector('main'); return Math.ceil(m.scrollHeight + m.getBoundingClientRect().top); }");
        await page.SetViewportSizeAsync(Session.Width, Math.Min(height, 3000));
        await Session.SettleAsync(page);
        var box = await until.First.BoundingBoxAsync() ?? throw new InvalidOperationException($"No box for {until}");
        var right = fitWidth
            ? box.X + box.Width
            : await page.EvaluateAsync<float>("() => document.querySelector('main > div > div.min-w-0').getBoundingClientRect().right");
        await page.ScreenshotAsync(new()
        {
            Path = path,
            Clip = new()
            {
                X = NavWidth + 1,
                Y = TopBarHeight,
                Width = Math.Min(Session.Width, right + pad) - NavWidth - 1,
                Height = box.Y + box.Height + pad - TopBarHeight,
            },
        });
        await page.SetViewportSizeAsync(Session.Width, Session.Height);
    }

    /// <summary>Clips to the union of the elements' boxes, grown by <paramref name="pad"/> CSS px.
    /// A crop of things inside the main column never reaches into the side nav or top bar.</summary>
    public static async Task AroundAsync(IPage page, string path, int pad, params ILocator[] targets)
    {
        await targets[0].First.ScrollIntoViewIfNeededAsync();
        var boxes = new List<Box>();
        foreach (var target in targets)
        {
            boxes.Add(await BoxAsync(target));
        }

        await AroundBoxesAsync(page, path, pad, boxes);
    }

    public static async Task AroundBoxesAsync(IPage page, string path, int pad, IReadOnlyList<Box> boxes)
    {
        var x1 = boxes.Min(b => b.X);
        var y1 = boxes.Min(b => b.Y);
        var x2 = boxes.Max(b => b.X + b.Width);
        var y2 = boxes.Max(b => b.Y + b.Height);

        var size = page.ViewportSize!;
        var left = x1 >= NavWidth ? NavWidth + 1 : 0;
        var top = y1 >= TopBarHeight ? TopBarHeight : 0;
        var clip = new Clip { X = Math.Max(left, x1 - pad), Y = Math.Max(top, y1 - pad) };
        clip.Width = Math.Min(size.Width, x2 + pad) - clip.X;
        clip.Height = Math.Min(size.Height, y2 + pad) - clip.Y;
        await page.ScreenshotAsync(new() { Path = path, Clip = clip });
    }

    /// <summary>The box around an element's text itself (a heading's box spans the whole row).</summary>
    public static async Task<Box> TextBoxAsync(ILocator element)
    {
        var r = await element.First.EvaluateAsync<float[]>(
            "el => { const r = document.createRange(); r.selectNodeContents(el); const b = r.getBoundingClientRect(); return [b.x, b.y, b.width, b.height]; }");
        return new Box(r[0], r[1], r[2], r[3]);
    }

    public static async Task<Box> BoxAsync(ILocator element)
    {
        var b = await element.First.BoundingBoxAsync() ?? throw new InvalidOperationException($"No box for {element}");
        return new Box(b.X, b.Y, b.Width, b.Height);
    }
}

/// <summary>A rectangle in CSS px, relative to the viewport.</summary>
internal sealed record Box(float X, float Y, float Width, float Height);

/// <summary>Names and moments from the demo data the shots refer to.</summary>
internal static class Hl
{
    public const string Project = "Harbourline Checkout v2";

    /// <summary>Notifications from this moment on are left unread (the bell shows a badge).</summary>
    public const string UnreadCutoff = "2026-11-10 11:00";

    public const string PromoCase = "An expired promo code is rejected";
}
