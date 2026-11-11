using Microsoft.Playwright;

namespace DocsScreenshots;

/// <summary>
/// The files the demo data attaches (bug screenshots, a screen recording, a test plan PDF, a
/// console log…), rendered from small HTML mock-ups of the fictional client's website so the
/// attachment previews show something believable.
/// </summary>
internal static class SampleFiles
{
    public static async Task GenerateAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        if (File.Exists(Path.Combine(dir, ".complete")))
        {
            return;
        }

        Console.WriteLine($"Rendering sample attachments into {dir}");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();

        await PngAsync(browser, Path.Combine(dir, "promo-code-expired.png"), 1100, 680, PromoCodeExpired);
        await PngAsync(browser, Path.Combine(dir, "keyboard-focus-lost.png"), 1100, 640, KeyboardFocusLost);
        await PngAsync(browser, Path.Combine(dir, "delivery-slots-after-cutoff.png"), 1100, 620, DeliverySlots);
        await PngAsync(browser, Path.Combine(dir, "invoice-vat.png"), 900, 520, InvoiceVat);
        await PngAsync(browser, Path.Combine(dir, "checkout-flow.png"), 1400, 520, CheckoutFlow);
        await PngAsync(browser, Path.Combine(dir, "basket-second-device.png"), 1100, 640, BasketSecondDevice);
        await PdfAsync(browser, Path.Combine(dir, "harbourline-test-plan.pdf"), TestPlanPdf);
        await PdfAsync(browser, Path.Combine(dir, "checkout-performance-report.pdf"), PerformancePdf);
        await PdfAsync(browser, Path.Combine(dir, "sign-off-report.pdf"), SignOffPdf);
        await VideoAsync(browser, Path.Combine(dir, "3ds-challenge-loop.webm"));

        await File.WriteAllTextAsync(Path.Combine(dir, "safari-console.log"), SafariConsoleLog);
        await File.WriteAllTextAsync(Path.Combine(dir, "test-accounts.csv"), TestAccountsCsv);
        await File.WriteAllTextAsync(Path.Combine(dir, "network-trace.har"), NetworkHar);
        await File.WriteAllTextAsync(Path.Combine(dir, "promo-codes.csv"), PromoCodesCsv);
        await File.WriteAllTextAsync(Path.Combine(dir, ".complete"), DateTime.UtcNow.ToString("O"));
    }

    private static async Task PngAsync(IBrowser browser, string path, int width, int height, string body)
    {
        // A short viewport plus FullPage makes the image exactly as tall as the mock-up's content.
        var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = width, Height = Math.Min(height, 200) }, DeviceScaleFactor = 1 });
        await page.SetContentAsync(Shell(body));
        await page.ScreenshotAsync(new() { Path = path, FullPage = true });
        await page.CloseAsync();
    }

    private static async Task PdfAsync(IBrowser browser, string path, string body)
    {
        var page = await browser.NewPageAsync();
        await page.SetContentAsync(Shell(body));
        await page.PdfAsync(new() { Path = path, Format = "A4", PrintBackground = true, Margin = new() { Top = "18mm", Bottom = "18mm", Left = "16mm", Right = "16mm" } });
        await page.CloseAsync();
    }

    /// <summary>A few seconds of the 3-D Secure challenge "looping" back to the payment step.</summary>
    private static async Task VideoAsync(IBrowser browser, string path)
    {
        var videoDir = Path.Combine(Path.GetDirectoryName(path)!, "video-tmp");
        var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 960, Height = 600 },
            RecordVideoDir = videoDir,
            RecordVideoSize = new() { Width = 960, Height = 600 },
        });
        var page = await context.NewPageAsync();
        await page.SetContentAsync(Shell(ThreeDsLoop));
        await page.WaitForTimeoutAsync(6500);
        var video = page.Video!;
        await context.CloseAsync();
        await video.SaveAsAsync(path);
        Directory.Delete(videoDir, recursive: true);
    }

    private static string Shell(string body) => $$"""
        <!doctype html><html><head><meta charset="utf-8"><style>
          * { box-sizing: border-box; }
          body { margin: 0; font-family: -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; color: #1f2937; background: #f4f1ea; }
          .bar { display:flex; align-items:center; justify-content:space-between; padding: 18px 32px; background:#0f3d3e; color:#f8f5ee; }
          .logo { font-weight: 800; letter-spacing: .06em; font-size: 20px; }
          .logo span { color:#f2b84b; }
          .secure { font-size: 13px; opacity:.85; }
          .wrap { padding: 28px 32px; display:flex; gap: 28px; }
          .card { background:#fff; border-radius: 12px; box-shadow: 0 1px 3px rgba(0,0,0,.08); padding: 22px 24px; }
          h2 { margin: 0 0 14px; font-size: 18px; }
          .row { display:flex; justify-content:space-between; padding: 9px 0; border-bottom: 1px solid #eee; font-size: 15px; }
          .muted { color:#6b7280; }
          .field { border:1px solid #d1d5db; border-radius:8px; padding: 11px 12px; margin: 6px 0 14px; color:#9ca3af; font-size: 14px; background:#fff; }
          label { font-size: 13px; font-weight: 600; color:#374151; }
          .btn { display:inline-block; background:#0f3d3e; color:#fff; border-radius: 8px; padding: 12px 18px; font-weight: 600; font-size: 14px; }
          .callout { display:inline-block; background:#b91c1c; color:#fff; font-size: 13px; font-weight:600; padding: 8px 12px; border-radius: 8px; box-shadow: 0 4px 14px rgba(185,28,28,.35); }
          .hl { outline: 3px solid #dc2626; outline-offset: 4px; border-radius: 4px; }
          table { border-collapse: collapse; width: 100%; font-size: 12.5px; }
          th, td { text-align:left; padding: 6px 8px; border-bottom: 1px solid #e5e7eb; vertical-align: top; }
          th { background:#f3f4f6; }
          .doc { background:#fff; padding: 0; color:#111827; }
          .doc h1 { font-size: 24px; margin: 0 0 4px; }
          .doc h3 { font-size: 15px; margin: 22px 0 8px; color:#0f3d3e; }
          .doc p, .doc li { font-size: 13px; line-height: 1.55; }
        </style></head><body>{{body}}</body></html>
        """;

    private const string Header = """
        <div class="bar"><div class="logo">HARBOUR<span>LINE</span></div><div class="secure">&#128274; Secure checkout · staging · build 1.4.0</div></div>
        """;

    private const string PromoCodeExpired = Header + """
        <div class="wrap">
          <div class="card" style="flex:1.2">
            <h2>Payment</h2>
            <label>Card number</label><div class="field">•••• •••• •••• 4242</div>
            <div style="display:flex; gap:14px"><div style="flex:1"><label>Expiry</label><div class="field">12 / 28</div></div><div style="flex:1"><label>CVC</label><div class="field">•••</div></div></div>
            <label>Promo code</label>
            <div style="display:flex; gap:10px; align-items:center"><div class="field" style="flex:1; color:#111">SUMMER25</div><div class="btn" style="background:#15803d">✓ Applied</div></div>
            <div style="margin-top:22px" class="btn">Pay £51.15</div>
          </div>
          <div class="card" style="flex:1">
            <h2>Order summary</h2>
            <div class="row"><span>Merino crew jumper · Navy · M</span><span>£49.60</span></div>
            <div class="row"><span>Wool socks · 3 pack</span><span>£12.00</span></div>
            <div class="row"><span class="muted">Subtotal</span><span>£61.60</span></div>
            <div class="row hl" style="color:#15803d; font-weight:600"><span>Promo SUMMER25 (25%)</span><span>−£15.40</span></div>
            <div class="row"><span class="muted">Delivery</span><span>£4.95</span></div>
            <div class="row" style="border:0; font-weight:700; font-size:17px"><span>Total</span><span>£51.15</span></div>
            <div class="callout" style="margin-top:10px">SUMMER25 expired on 31 Oct — it should be rejected</div>
          </div>
        </div>
        """;

    private const string KeyboardFocusLost = Header + """
        <div class="wrap">
          <div class="card" style="flex:1">
            <h2>Delivery address</h2>
            <div class="row"><span>12 Quay Street, Bristol BS1 4DJ</span><span class="muted">Default</span></div>
            <div class="row"><span>Flat 3, 48 Mill Lane, Leeds LS6 2AA</span><span></span></div>
            <div style="margin-top:18px; display:flex; gap:12px">
              <div class="btn" style="background:#fff; color:#0f3d3e; border:2px solid #0f3d3e">+ Add a new address</div>
              <div class="btn">Continue to payment</div>
            </div>
          </div>
          <div class="card" style="width:380px">
            <h2>Keyboard check</h2>
            <p style="font-size:14px; line-height:1.6; margin:0">1. Tab to <b>Add a new address</b> and press Enter<br>2. Press <b>Esc</b> to close the dialog<br>3. Press <b>Tab</b></p>
            <div class="callout">Focus is on &lt;body&gt; — the next Tab lands on the site logo, not back on the button</div>
          </div>
        </div>
        """;

    private const string DeliverySlots = Header + """
        <div class="wrap" style="display:block">
          <div class="card">
            <div style="display:flex; justify-content:space-between; align-items:center"><h2>Choose a delivery slot</h2><div class="muted" style="font-size:14px">Device time: <b style="color:#b91c1c">Tue 10 Nov, 14:37</b></div></div>
            <table style="font-size:14px; margin-top:6px">
              <tr><th></th><th>Today · Tue 10</th><th>Wed 11</th><th>Thu 12</th><th>Fri 13</th></tr>
              <tr><td class="muted">8–10am</td><td class="muted">—</td><td>£3.50</td><td>£3.50</td><td>£2.50</td></tr>
              <tr><td class="muted">10am–12pm</td><td class="muted">—</td><td>£3.50</td><td>Full</td><td>£2.50</td></tr>
              <tr><td class="muted">12–2pm</td><td class="muted">—</td><td>£2.50</td><td>£2.50</td><td>£2.50</td></tr>
              <tr><td class="muted">4–6pm</td><td style="background:#fee2e2; font-weight:600">£4.95</td><td>£2.50</td><td>£2.50</td><td>Full</td></tr>
              <tr><td class="muted">6–8pm</td><td style="background:#fee2e2; font-weight:600">£4.95</td><td>£3.50</td><td>£3.50</td><td>£3.50</td></tr>
            </table>
            <div class="callout" style="margin-top:14px">Same-day slots are still offered after the 2pm cut-off</div>
          </div>
        </div>
        """;

    private const string InvoiceVat = """
        <div style="padding:30px">
          <div class="card" style="width:820px">
            <div style="display:flex; justify-content:space-between"><div class="logo" style="color:#0f3d3e">HARBOUR<span>LINE</span></div><div class="muted" style="font-size:13px">Invoice HL-104882 · 10 Nov 2026</div></div>
            <div style="margin-top:18px">
              <div class="row"><span>Merino crew jumper · Navy · M</span><span>£19.99</span></div>
              <div class="row" style="color:#15803d"><span>WELCOME10</span><span>−£2.00</span></div>
              <div class="row"><span class="muted">Total (inc. VAT)</span><span>£17.99</span></div>
              <div class="row hl" style="font-weight:700"><span>VAT (20%)</span><span>£2.99</span></div>
            </div>
            <div class="callout" style="margin-top:14px">Should be £3.00 — the invoice still rounds down</div>
          </div>
        </div>
        """;

    private const string CheckoutFlow = """
        <div style="padding:40px; background:#fff; height:100%">
          <h2 style="font-size:22px">Harbourline checkout v2 — flow under test</h2>
          <div style="display:flex; align-items:center; gap:14px; margin-top:40px">
            <div class="card" style="background:#0f3d3e; color:#fff; width:200px; text-align:center"><b>Basket</b><div style="font-size:12px; opacity:.8; margin-top:6px">quantities · promo codes</div></div>
            <div style="font-size:30px; color:#9ca3af">→</div>
            <div class="card" style="background:#0f3d3e; color:#fff; width:200px; text-align:center"><b>Delivery</b><div style="font-size:12px; opacity:.8; margin-top:6px">slots · click &amp; collect</div></div>
            <div style="font-size:30px; color:#9ca3af">→</div>
            <div class="card" style="background:#0f3d3e; color:#fff; width:200px; text-align:center"><b>Payment</b><div style="font-size:12px; opacity:.8; margin-top:6px">saved cards · wallets</div></div>
            <div style="font-size:30px; color:#9ca3af">→</div>
            <div class="card" style="background:#f2b84b; width:200px; text-align:center"><b>3-D Secure</b><div style="font-size:12px; margin-top:6px">bank challenge</div></div>
            <div style="font-size:30px; color:#9ca3af">→</div>
            <div class="card" style="background:#15803d; color:#fff; width:200px; text-align:center"><b>Confirmation</b><div style="font-size:12px; opacity:.8; margin-top:6px">order · email</div></div>
          </div>
          <p class="muted" style="margin-top:40px; font-size:14px">One-page checkout replaces the four-step flow. Everything in green boxes is in scope for this round of testing.</p>
        </div>
        """;

    private const string BasketSecondDevice = Header + """
        <div class="wrap">
          <div class="card" style="flex:1">
            <h2>Your basket (1)</h2>
            <div class="row"><span>Linen shirt · White · L</span><span>£38.00</span></div>
            <p class="muted" style="font-size:14px">Signed in as demo.customer@example.com on a phone.<br>The two items added on the laptop before signing in are missing.</p>
          </div>
        </div>
        """;

    private const string ThreeDsLoop = """
        <div class="bar"><div class="logo">HARBOUR<span>LINE</span></div><div class="secure">&#128274; Secure checkout · Safari 18</div></div>
        <div id="stage" style="padding:40px; display:flex; justify-content:center"></div>
        <script>
          const stage = document.getElementById('stage');
          const pay = `<div class="card" style="width:520px"><h2>Payment</h2><label>Card number</label><div class="field">4000 0000 0000 3220</div><div class="btn">Pay £51.15</div></div>`;
          const bank = `<div class="card" style="width:520px; text-align:center"><h2>Verifying with your bank…</h2><p class="muted">Enter the code we sent to your phone</p><div class="field" style="color:#111">1 2 3 4</div><div class="btn" style="background:#1d4ed8">Submit</div></div>`;
          const back = `<div class="card" style="width:520px; outline:3px solid #dc2626"><h2>Payment</h2><p style="color:#b91c1c; font-weight:600">Back on the payment step — card details cleared</p><label>Card number</label><div class="field">Card number</div><div class="btn">Pay £51.15</div></div>`;
          const frames = [pay, bank, back];
          let i = 0; stage.innerHTML = frames[0];
          setInterval(() => { i = (i + 1) % frames.length; stage.innerHTML = frames[i]; }, 2000);
        </script>
        """;

    private const string TestPlanPdf = """
        <div class="doc">
          <h1>Harbourline checkout v2 — test plan</h1>
          <p class="muted">Version 1.2 · Priya Raman · 6 November 2026</p>
          <h3>Scope</h3>
          <p>The new one-page checkout on web: basket, promo codes, delivery and click &amp; collect, card and wallet payments with 3-D Secure, and order confirmation. Out of scope: gift cards, PayPal, the mobile apps.</p>
          <h3>Schedule</h3>
          <table><tr><th>Day</th><th>Focus</th></tr>
            <tr><td>Mon 9 Nov</td><td>Basket, promo codes, payment happy paths</td></tr>
            <tr><td>Tue 10 Nov</td><td>Delivery, accessibility, performance</td></tr>
            <tr><td>Wed 11 Nov</td><td>Re-tests, wallets (once the sandbox merchant ID is live)</td></tr>
            <tr><td>Thu 12 Nov</td><td>Regression on build 1.4.3</td></tr>
            <tr><td>Fri 13 Nov</td><td>Sign-off</td></tr></table>
          <h3>Entry and exit criteria</h3>
          <ul><li>Entry: build deployed to staging, test accounts and cards available.</li>
              <li>Exit: no open Critical or High defects; every test case run; known issues agreed with the product owner.</li></ul>
          <h3>Environments</h3>
          <p>staging.harbourline.example · payment sandbox · Safari 18, Chrome 131, Firefox 133, iOS 18, Android 15.</p>
        </div>
        """;

    private const string PerformancePdf = """
        <div class="doc">
          <h1>Checkout performance — staging</h1>
          <p class="muted">Simulated 4G (9 Mbps, 170 ms RTT) · median of 5 runs · 10 Nov 2026</p>
          <table><tr><th>Metric</th><th>Result</th><th>Budget</th></tr>
            <tr><td>Largest contentful paint</td><td>1.6 s</td><td>2.0 s</td></tr>
            <tr><td>Interaction to next paint</td><td>120 ms</td><td>200 ms</td></tr>
            <tr><td>Cumulative layout shift</td><td>0.02</td><td>0.1</td></tr>
            <tr><td>JavaScript transferred</td><td>212 KB</td><td>300 KB</td></tr></table>
          <p>All metrics are inside budget.</p>
        </div>
        """;

    private const string SignOffPdf = """
        <div class="doc">
          <h1>Test sign-off</h1>
          <p class="muted">Prepared by the QA team</p>
          <h3>Summary</h3>
          <p>Every planned test case was run and passed after fixes. All defects raised during testing were fixed and re-tested, or agreed not to be defects. No known issues block release.</p>
          <h3>Recommendation</h3>
          <p>Ready for production.</p>
        </div>
        """;

    private const string SafariConsoleLog = """
        [Log] [checkout] step=payment card=visa-3ds
        [Log] [checkout] POST /api/payments/intent 200 (412 ms)
        [Log] [3ds] challenge window opened (issuer sandbox)
        [Log] [3ds] challenge complete, posting result to /checkout/3ds/return
        [Warning] Cookie "hl_session" has been rejected because it is in a cross-site context and its "SameSite" is "Lax".
        [Error] Failed to load resource: the server responded with a status of 401 (Unauthorized) — /checkout/3ds/return
        [Log] [checkout] session missing after 3DS return — restarting at step=payment
        [Log] [checkout] step=payment card=<cleared>
        """;

    private const string TestAccountsCsv = """
        Account,Email,Password,Notes
        Saved card customer,demo.saved@example.com,see vault,Visa ending 4242 saved
        Expired card customer,demo.expired@example.com,see vault,Mastercard ending 4444 expired 09/26
        New customer,demo.new@example.com,see vault,No orders
        3-D Secure card,4000 0000 0000 3220,n/a,Challenge code 1234
        Declined card,4000 0000 0000 0002,n/a,Always declines
        """;

    private const string PromoCodesCsv = """
        Code,Discount,Valid from,Expires,Expected
        WELCOME10,10% off items,2026-01-01,2026-12-31,Accepted
        FREEDEL,Free standard delivery,2026-10-01,2026-12-31,Accepted
        SUMMER25,25% off items,2026-06-01,2026-10-31,Rejected (expired)
        AUTUMN15,15% off items,2026-09-01,2026-11-08,Rejected (expired)
        STAFF50,50% off items,2026-01-01,2026-12-31,Rejected (staff accounts only)
        """;

    private const string NetworkHar = """
        {"log":{"version":"1.2","creator":{"name":"Browser DevTools","version":"1"},"entries":[
          {"startedDateTime":"2026-11-10T13:02:11Z","request":{"method":"POST","url":"https://staging.harbourline.example/api/basket/merge"},"response":{"status":409,"statusText":"Conflict"}}
        ]}}
        """;
}
