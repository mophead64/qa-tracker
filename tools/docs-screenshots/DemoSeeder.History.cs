using QaTracker.Web.Attachments;
using QaTracker.Web.Comments;
using QaTracker.Web.Defects;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;
using static QaTracker.Web.Defects.DefectSeverity;
using static QaTracker.Web.TestCases.TestCaseKind;

namespace DocsScreenshots;

/// <summary>The completed projects: a steady run of short engagements through the year, each
/// tested, fixed, re-tested and signed off.</summary>
internal sealed partial class DemoSeeder
{
    private sealed record HistScope(TestCaseKind Kind, string Name, params string[] Cases);

    /// <param name="Scope">Index into the project's scopes.</param>
    /// <param name="Case">Index into that scope's cases — the case that failed and raised it.</param>
    private sealed record HistDefect(int Scope, int Case, string Summary, DefectSeverity Severity, string Dev, bool NotADefect = false);

    private sealed record HistProject(
        string Name, string Brief, string Created, string Lead, string Qa, string[] Devs,
        DateOnly Start, DateOnly End, decimal DevDays, string Completed,
        HistScope[] Scopes, HistDefect[] Defects, bool EndsMidday = false);

    private static readonly HistProject[] HistoricalProjects =
    [
        new("Fernhill Appointment Booking",
            "Online booking for 14 GP and physio clinics, with SMS reminders.",
            "2026-02-16", "priya", "elena", ["aisha", "diego"],
            new(2026, 3, 2), new(2026, 3, 6), 18, "2026-03-09",
            [
                new(Functional, "Find a clinic", "Search clinics by postcode", "Filter clinics by service", "Clinic page shows the next free slot"),
                new(Functional, "Book an appointment", "Book a GP appointment", "Book an appointment for a dependant", "The same slot can't be booked twice", "Booking is confirmed by text message"),
                new(Functional, "Manage bookings", "Reschedule an appointment", "Cancelling within 24 hours shows the late-cancellation notice", "Add the appointment to a calendar"),
                new(NonFunctional, "Accessibility", "The booking flow works with a screen reader", "Text resizes to 200% without losing content"),
            ],
            [
                new(1, 1, "Dependant's date of birth accepts future dates", Medium, "aisha"),
                new(1, 3, "Confirmation text shows the time in UTC", High, "diego"),
                new(2, 2, "Calendar invite has no location", Low, "aisha"),
                new(0, 1, "Clinic search ignores the 'open now' filter", Medium, "diego"),
                new(2, 0, "Back button loses the selected slot", Low, "aisha", NotADefect: true),
            ]),

        new("Quayside Shipment Tracking API",
            "Public REST API and webhooks for tracking freight shipments end to end.",
            "2026-03-23", "tom", "priya", ["marcus", "noah"],
            new(2026, 4, 7), new(2026, 4, 10), 15, "2026-04-13",
            [
                new(Functional, "Tracking endpoints", "GET /shipments/{id} returns the latest status", "An unknown shipment returns 404 with a problem body", "Status history is ordered newest first"),
                new(Functional, "Webhooks", "A status change sends a webhook within 30 seconds", "Failed deliveries are retried with back-off", "Webhook payloads are signed"),
                new(Functional, "Authentication", "Requests without an API key are rejected", "Revoked keys stop working within a minute"),
                new(NonFunctional, "Performance", "p95 stays under 300 ms at 200 requests per second", "Rate limiting returns 429 with Retry-After"),
            ],
            [
                new(1, 2, "Webhook signature is sent in the wrong header", High, "marcus"),
                new(1, 1, "Retry back-off doesn't cap at one hour", Medium, "noah"),
                new(0, 1, "404 body is HTML instead of JSON", Low, "marcus"),
                new(3, 1, "Rate limit counts health checks", Medium, "noah"),
            ]),

        new("Alder & Finch Onboarding Portal",
            "Self-service onboarding for new starters: personal details, documents and e-signing.",
            "2026-04-27", "elena", "tom", ["hana", "liam"],
            new(2026, 5, 11), new(2026, 5, 15), 22, "2026-05-18",
            [
                new(Functional, "Invitations", "Invite a new starter by email", "Invitation links expire after 7 days", "Resend an invitation"),
                new(Functional, "Personal details", "Starter enters their bank details", "National Insurance number is validated", "Emergency contact is optional"),
                new(Functional, "Documents", "Upload a passport scan", "Sign the contract electronically", "Download the signed contract"),
                new(Functional, "Manager view", "Managers see outstanding tasks", "Managers are emailed when onboarding completes"),
            ],
            [
                new(0, 0, "Invitation email shows a broken logo", Low, "liam"),
                new(1, 0, "Bank sort code accepts letters", High, "hana"),
                new(2, 1, "Signed contract PDF is blank on iOS", High, "liam"),
                new(3, 1, "Managers are emailed twice", Medium, "hana"),
                new(2, 0, "Passport uploads are limited to 2 MB", Low, "liam", NotADefect: true),
            ]),

        new("Larkspur CMS Migration",
            "Moving 1,200 articles and their images from the legacy CMS, with redirects for every URL.",
            "2026-05-25", "priya", "elena", ["diego", "marcus"],
            new(2026, 6, 8), new(2026, 6, 12), 16, "2026-06-15",
            [
                new(Functional, "Content import", "All 1,200 articles are migrated", "Article authors are kept", "Embedded images are re-hosted"),
                new(Functional, "Redirects", "Old article URLs redirect with a 301", "Redirect chains are never more than one hop"),
                new(Functional, "Editorial workflow", "Draft, review and publish an article", "Scheduled articles publish on time", "Roll back to a previous revision"),
                new(NonFunctional, "SEO", "Every article has a canonical tag", "The sitemap lists every published article"),
            ],
            [
                new(0, 2, "Images inside tables lose their alt text", Medium, "diego"),
                new(1, 0, "URLs with a trailing slash return 404", High, "marcus"),
                new(2, 1, "Scheduled publishing uses the server's time zone", Medium, "diego"),
                new(2, 2, "Migrated articles have no revision history", Low, "marcus", NotADefect: true),
            ]),

        new("Pinecrest Points Wallet",
            "Loyalty points wallet in the customer account: earn, spend and track points.",
            "2026-06-29", "tom", "priya", ["aisha", "noah"],
            new(2026, 7, 13), new(2026, 7, 17), 14, "2026-07-20",
            [
                new(Functional, "Earning points", "Points are added after a purchase", "Points for a returned item are taken back", "Bonus points campaigns apply once per order"),
                new(Functional, "Spending points", "Pay part of an order with points", "Points can't take the balance below zero"),
                new(Functional, "Wallet", "The wallet shows the balance and expiry dates", "Transaction history can be filtered by month"),
                new(NonFunctional, "Security", "The wallet API requires a signed-in customer", "A points transfer can't be replayed"),
            ],
            [
                new(0, 2, "Bonus points are applied twice for split orders", High, "aisha"),
                new(2, 0, "Expiry dates are shown in US format", Low, "noah"),
                new(2, 1, "Wallet balance is cached for 10 minutes", Medium, "aisha"),
            ]),

        new("Tidewater Reporting Dashboard",
            "Self-serve sales reporting for regional managers, replacing the weekly spreadsheet.",
            "2026-08-10", "elena", "tom", ["hana", "diego"],
            new(2026, 8, 24), new(2026, 8, 28), 20, "2026-09-01",
            [
                new(Functional, "Charts", "The revenue chart matches the finance export", "Custom date ranges can be picked", "Chart tooltips show exact values"),
                new(Functional, "Exports", "Export a report to CSV", "The scheduled report email arrives at 7am"),
                new(Functional, "Permissions", "Viewers can't edit dashboards", "Shared dashboard links respect permissions"),
                new(NonFunctional, "Performance", "Dashboards load in under 3 seconds with 12 months of data"),
            ],
            [
                new(1, 0, "CSV export drops rows after 10,000", Critical, "hana"),
                new(0, 2, "Tooltips round values to the nearest thousand", Low, "diego"),
                new(1, 1, "Scheduled email arrives an hour late", Medium, "hana"),
                new(2, 1, "Viewers can rename a shared dashboard", High, "diego"),
            ],
            EndsMidday: true),

        new("Kestrel Offline Sync",
            "Offline mode for the field engineers' app: jobs, notes and photos sync when back online.",
            "2026-09-07", "priya", "tom", ["liam", "marcus"],
            new(2026, 9, 21), new(2026, 9, 25), 24, "2026-09-28",
            [
                new(Functional, "Offline mode", "Create a job while offline", "Edit a job while offline", "The app shows an offline banner"),
                new(Functional, "Sync", "Changes sync when the device is back online", "Conflicting edits show a resolution screen", "Photos upload after reconnecting"),
                new(NonFunctional, "Battery and data", "Background sync uses less than 5% battery a day", "Sync respects the Wi-Fi only setting"),
            ],
            [
                new(1, 2, "Photos are uploaded twice after reconnecting", High, "liam"),
                new(1, 1, "Conflict screen shows raw JSON", Medium, "marcus"),
                new(0, 2, "Offline banner covers the toolbar", Low, "liam"),
                new(2, 1, "Wi-Fi only setting is ignored for photos", Medium, "marcus"),
                new(1, 0, "Sync spinner never stops when there's nothing to sync", Low, "liam"),
            ]),

        new("Saltmarsh 3-D Secure Upgrade",
            "Moving card payments to 3-D Secure 2 with frictionless flows and exemptions.",
            "2026-10-05", "tom", "elena", ["marcus", "noah"],
            new(2026, 10, 19), new(2026, 10, 23), 12, "2026-10-26",
            [
                new(Functional, "Challenge flow", "Frictionless authentication completes without a challenge", "The challenge flow completes with a one-time passcode", "An abandoned challenge returns to the payment page"),
                new(Functional, "Fallback", "Cards not enrolled in 3-D Secure still complete", "An issuer timeout offers a retry"),
                new(NonFunctional, "Compliance", "Low-value payments are flagged as exempt", "Authentication data is kept for 13 months"),
            ],
            [
                new(0, 1, "Challenge window is too small on mobile", Medium, "marcus"),
                new(1, 1, "Retrying after a timeout charges twice", Critical, "noah"),
                new(2, 0, "Exemption flag is missing on recurring payments", High, "marcus"),
            ]),
    ];

    private async Task SeedHistoricalAsync(HistProject h)
    {
        var team = new[] { h.Lead, h.Qa }.Concat(h.Devs).ToArray();
        var projectId = await CreateProjectAsync($"{h.Created} 10:00", h.Lead, h.Name, h.Brief, null, [], team,
            new ProjectSchedule(h.Start, h.End, false, h.EndsMidday, h.DevDays, null, null));

        // Write the test plan a few days after kick-off.
        clock.Set(DemoClock.Parse($"{h.Created} 10:00").AddDays(3));
        var qas = new[] { h.Lead, h.Qa };
        var caseIds = new List<List<Guid>>();
        var n = 0;
        foreach (var scope in h.Scopes)
        {
            var scopeId = await ScopeAsync(projectId, scope.Kind, scope.Name, qas[n % 2]);
            var ids = new List<Guid>();
            foreach (var scenario in scope.Cases)
            {
                ids.Add(await CaseAsync(scopeId, scenario, null, qas[n++ % 2]));
            }

            caseIds.Add(ids);
        }

        var start = new DateTimeOffset(h.Start.ToDateTime(new TimeOnly(9, 5)), TimeSpan.Zero);
        await ProjectCommentAsync(Fmt(start), projectId, h.Lead,
            $"Testing starts today and runs until {h.End:ddd d MMM}. Daily summaries will go here.");

        // Run every case over the first three days of the window, spaced through the working day.
        var all = caseIds.SelectMany((ids, s) => ids.Select((id, c) => (s, c, id))).ToList();
        var t = start.AddMinutes(25);
        var buildNo = 2;
        for (var i = 0; i < all.Count; i++)
        {
            var (s, c, caseId) = all[i];
            var tester = qas[i % 2];
            var defect = h.Defects.FirstOrDefault(d => d.Scope == s && d.Case == c);
            if (defect is null)
            {
                await ResultAsync(Fmt(t), caseId, TestResult.Passed, tester);
            }
            else
            {
                await ResultAsync(Fmt(t), caseId, TestResult.Failed, tester);
                var defectId = await DefectAsync(Fmt(t.AddMinutes(3)), projectId, tester, defect.Summary, defect.Severity,
                    "Steps are in the linked test case.", assignee: defect.Dev, linkedCases: [caseId]);

                if (defect.NotADefect)
                {
                    await DefectCommentAsync(Fmt(Work(t, 70)), defectId, defect.Dev,
                        "This is working as designed — it's in the spec agreed with the client. Happy to close?");
                    clock.Set(Work(t, 95));
                    await defects.SetStatusAsync(defectId, DefectStatus.NotADefect, Id(tester));
                    await ResultAsync(Fmt(Work(t, 100)), caseId, TestResult.Passed, tester);
                }
                else
                {
                    await StartFixingAsync(Fmt(Work(t, 45)), defectId, defect.Dev);
                    var fixedAt = Work(t, 200);
                    await DefectCommentAsync(Fmt(fixedAt), defectId, defect.Dev, $"Fixed in build 1.{buildNo++} — on staging now.");
                    await MarkFixedAsync(Fmt(fixedAt.AddMinutes(1)), projectId, defectId, defect.Dev, tester);
                    var verified = Work(fixedAt, 75);
                    await VerifyAsync(Fmt(verified), defectId, tester);
                    await ResultAsync(Fmt(verified.AddMinutes(4)), caseId, TestResult.Passed, tester);
                }
            }

            t = Work(t, 35);
        }

        var end = new DateTimeOffset(h.End.ToDateTime(new TimeOnly(h.EndsMidday ? 11 : 16, 20)), TimeSpan.Zero);
        var signOff = await ProjectCommentAsync(Fmt(end), projectId, h.Lead,
            $"Signed off: all {all.Count} test cases passed and every defect is closed. Report attached under Files. Thanks all!");
        await ReactAsync(Fmt(end.AddMinutes(9)), CommentKind.Project, signOff, h.Devs[0]);
        await ReactAsync(Fmt(end.AddMinutes(14)), CommentKind.Project, signOff, h.Qa);
        await AttachAsync(Fmt(end.AddMinutes(2)), AttachmentOwner.Project, projectId, "sign-off-report.pdf", h.Lead, "Test sign-off");

        clock.At($"{h.Completed} 09:15");
        await projects.SetStatusAsync(projectId, ProjectStatus.Complete, Id(h.Lead));
    }

    private static string Fmt(DateTimeOffset t) => t.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary><paramref name="from"/> plus <paramref name="minutes"/>, kept inside a 09:30–17:30
    /// weekday — anything that would land later moves to the next working morning.</summary>
    private static DateTimeOffset Work(DateTimeOffset from, int minutes)
    {
        var t = from.AddMinutes(minutes);
        if (t.Date != from.Date || t.TimeOfDay > new TimeSpan(17, 30, 0))
        {
            var spill = (int)(t - new DateTimeOffset(from.Date.AddHours(17).AddMinutes(30), TimeSpan.Zero)).TotalMinutes;
            t = new DateTimeOffset(from.Date.AddDays(1).AddHours(9).AddMinutes(30 + Math.Clamp(spill, 0, 120)), TimeSpan.Zero);
        }

        while (t.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            t = t.AddDays(1);
        }

        return t;
    }
}
