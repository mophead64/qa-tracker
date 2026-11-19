using Microsoft.EntityFrameworkCore;
using QaTracker.Web.Attachments;
using QaTracker.Web.Board;
using QaTracker.Web.Comments;

namespace QaTracker.Web.Demo;

/// <summary>
/// Harbourline's development board: the stories the developers built during the fortnight before
/// testing, the wallet work still blocked on the merchant ID, a known problem carried over from the
/// old checkout (a bug from day one), and bugs made from three of the week's defects — each linked
/// to the test cases and defects that cover it.
/// </summary>
public sealed partial class DemoSeeder
{
    private sealed record HarbourlineBoardLinks(
        Guid Basket, Guid Promos, Guid Confirmation,
        Guid SavedCard, Guid NewCard, Guid ExpiredCards, Guid ApplePay, Guid GooglePay, Guid ThreeDs, Guid DeliveryNotes,
        Guid ThreeDsDefect, Guid VatDefect, Guid BasketDefect, Guid ExpiredCardsDefect);

    private async Task SeedHarbourlineBoardAsync(Guid p, HarbourlineBoardLinks l)
    {
        clock.At("2026-10-19 10:05");
        await board.SetModeAsync(p, BoardMode.Local, Id("priya"));

        // ---- stories, written up as the build started ------------------------------------------
        var flow = await BoardItemAsync("2026-10-19 11:00", p, BoardItemType.Story, "marcus", "One-page checkout flow",
            """
            Basket, delivery and payment on one page, with the order summary kept in view.

            Done when: a signed-in customer can check out without leaving the page, and the old multi-step routes redirect here.
            """, "marcus");
        await AttachAsync("2026-10-19 11:01", AttachmentOwner.BoardItem, flow, "checkout-flow.png", "marcus", "The agreed flow");
        var cards = await BoardItemAsync("2026-10-19 11:20", p, BoardItemType.Story, "aisha", "Save a card for next time",
            """
            Customers can tick 'Save this card' when they pay, and pick a saved card next time (re-entering only the CVC).

            Expired cards are hidden. Card details stay with the payment provider: we only keep its token, the brand and the last four digits.
            """, "aisha");
        var wallets = await BoardItemAsync("2026-10-19 11:40", p, BoardItemType.Story, "marcus", "Apple Pay and Google Pay",
            "Wallet buttons above the card form, on browsers that support them. Needs Harbourline's merchant ID for Apple Pay.", "liam");

        // ---- a known problem in today's checkout, put on the board as a bug from the start ------
        var notes = await BoardItemAsync("2026-10-19 11:50", p, BoardItemType.Bug, "priya", "Delivery notes over 50 characters are lost",
            """
            Known issue in the current checkout, carried into v2: a delivery note longer than 50 characters is dropped without a warning, so the driver never sees it. Customer services have logged a dozen complaints since the summer.

            The new delivery step should keep notes up to 250 characters.
            """, "liam");
        await board.LinkAsync(p, notes, BoardLinkKind.TestCase, l.DeliveryNotes, Id("priya"));
        var promo = await BoardItemAsync("2026-10-20 09:30", p, BoardItemType.Story, "aisha", "Promo codes at checkout",
            "One code per order. Percentage, fixed and free-delivery codes, checked against their start and end dates.", "aisha");
        var collect = await BoardItemAsync("2026-10-21 10:15", p, BoardItemType.Story, "priya", "Click & collect store picker",
            "Pick a store at the delivery step, with today's opening hours. Next sprint — not in this test round.", null);

        // ---- tasks, ticked off over the fortnight ------------------------------------------------
        var flowTasks = new[]
        {
            await TaskAsync("2026-10-19 11:05", flow, "Merge the basket, delivery and payment steps", "marcus"),
            await TaskAsync("2026-10-19 11:06", flow, "Keep the order summary in view while scrolling", "marcus"),
            await TaskAsync("2026-10-19 11:07", flow, "Redirect the old multi-step routes", "liam"),
        };
        var cardTasks = new[]
        {
            await TaskAsync("2026-10-19 11:25", cards, "Store the provider's card token, brand and last four", "aisha"),
            await TaskAsync("2026-10-19 11:26", cards, "Saved cards list at the payment step", "aisha"),
            await TaskAsync("2026-10-19 11:27", cards, "Ask for the CVC when paying with a saved card", "aisha"),
            await TaskAsync("2026-10-19 11:28", cards, "Hide expired cards", "aisha"),
        };
        var walletTasks = new[]
        {
            await TaskAsync("2026-10-19 11:45", wallets, "Google Pay on Chrome", "liam"),
            await TaskAsync("2026-10-19 11:46", wallets, "Apple Pay button on Safari", "liam"),
            await TaskAsync("2026-10-19 11:47", wallets, "Register the Apple Pay merchant ID", "marcus"),
        };
        var promoTasks = new[]
        {
            await TaskAsync("2026-10-20 09:35", promo, "Promo code service with start and end dates", "aisha"),
            await TaskAsync("2026-10-20 09:36", promo, "Promo box and messages at checkout", "aisha"),
        };

        await MoveCardAsync("2026-10-20 09:10", flow, BoardColumn.InProgress, "marcus");
        await MoveCardAsync("2026-10-20 09:40", cards, BoardColumn.InProgress, "aisha");
        await MoveCardAsync("2026-10-21 10:00", notes, BoardColumn.InProgress, "liam");
        await MoveCardAsync("2026-10-23 14:30", notes, BoardColumn.Testing, "liam");
        await DoneAsync("2026-10-22 16:30", flowTasks[0], "marcus");
        await DoneAsync("2026-10-23 12:10", flowTasks[1], "marcus");
        await DoneAsync("2026-10-26 15:00", cardTasks[0], "aisha");
        await MoveCardAsync("2026-10-26 15:20", promo, BoardColumn.InProgress, "aisha");
        await DoneAsync("2026-10-27 11:40", flowTasks[2], "liam");
        await MoveCardAsync("2026-10-27 11:45", flow, BoardColumn.Testing, "marcus");
        await DoneAsync("2026-10-28 14:05", cardTasks[1], "aisha");
        await DoneAsync("2026-10-29 10:30", cardTasks[2], "aisha");
        await MoveCardAsync("2026-10-29 11:00", wallets, BoardColumn.InProgress, "liam");
        await DoneAsync("2026-10-30 16:20", promoTasks[0], "aisha");
        await DoneAsync("2026-11-02 12:45", walletTasks[0], "liam");
        await DoneAsync("2026-11-03 10:10", promoTasks[1], "aisha");
        await MoveCardAsync("2026-11-03 10:15", promo, BoardColumn.Testing, "aisha");
        await DoneAsync("2026-11-04 15:30", cardTasks[3], "aisha");
        await MoveCardAsync("2026-11-04 15:35", cards, BoardColumn.Testing, "aisha");
        await DoneAsync("2026-11-05 11:00", walletTasks[1], "liam");
        await MoveCardAsync("2026-11-06 17:00", flow, BoardColumn.Closed, "priya");

        // ---- what each story covers ----------------------------------------------------------------
        clock.At("2026-11-06 14:00");
        await board.LinkAsync(p, flow, BoardLinkKind.Scope, l.Basket, Id("priya"));
        await board.LinkAsync(p, flow, BoardLinkKind.Scope, l.Confirmation, Id("priya"));
        await board.LinkAsync(p, cards, BoardLinkKind.TestCase, l.SavedCard, Id("priya"));
        await board.LinkAsync(p, cards, BoardLinkKind.TestCase, l.NewCard, Id("priya"));
        await board.LinkAsync(p, cards, BoardLinkKind.TestCase, l.ExpiredCards, Id("priya"));
        await board.LinkAsync(p, wallets, BoardLinkKind.TestCase, l.ApplePay, Id("tom"));
        await board.LinkAsync(p, wallets, BoardLinkKind.TestCase, l.GooglePay, Id("tom"));
        await board.LinkAsync(p, promo, BoardLinkKind.Scope, l.Promos, Id("priya"));

        var walletNote = await BoardCommentAsync("2026-11-05 11:10", p, wallets, "liam",
            "Google Pay and the Apple Pay button are on staging. Apple Pay can't take a payment until the merchant ID is registered — @Marcus Chen any news from Harbourline's finance team?",
            "marcus");
        await BoardCommentAsync("2026-11-05 11:32", p, wallets, "marcus",
            "Request went in on Monday; they're saying end of next week. I'll keep this In progress until it lands.");
        await ReactAsync("2026-11-05 11:40", CommentKind.BoardItem, walletNote, "tom");
        await BoardCommentAsync("2026-11-04 15:40", p, cards, "aisha",
            "Ready for testing on build 2026.11.04.3. #T-11 and #T-12 cover it; expired cards are hidden rather than greyed out.");

        // ---- bugs made from the week's defects -------------------------------------------------------
        // An Advanced bug, like the defect it came from: repro steps, expected and actual results.
        var threeDs = await BugFromDefectAsync("2026-11-09 15:20", p, l.ThreeDsDefect, "marcus",
            "3-D Secure challenge loops back to the payment step in Safari",
            "1. In Safari 18, check out with the 3-D Secure test card 4000 0000 0000 3220\n2. Complete the bank's challenge with 1234\n\nSafari drops the session cookie on the bank's POST back (SameSite=Lax), so the return looks like a new checkout. Raised from #D-3.",
            "marcus", Defects.DefectTemplate.Advanced,
            expected: "Back on the order confirmation page, with the order marked as paid.",
            actual: "Back on the payment step with an empty card form; the payment sandbox shows the charge as authorised.");
        await board.LinkAsync(p, threeDs, BoardLinkKind.TestCase, l.ThreeDs, Id("marcus"));
        var threeDsTasks = new[]
        {
            await TaskAsync("2026-11-09 15:22", threeDs, "Reproduce on Safari 18 against staging", "marcus"),
            await TaskAsync("2026-11-09 15:23", threeDs, "Return from the bank with a one-time token instead of the cookie", "marcus"),
        };
        await MoveCardAsync("2026-11-09 15:25", threeDs, BoardColumn.InProgress, "marcus");
        await DoneAsync("2026-11-09 16:05", threeDsTasks[0], "marcus");
        await BoardCommentAsync("2026-11-09 16:10", p, threeDs, "marcus",
            "Reproduced: the cookie is there on the way out and gone on the way back. Fix is the token approach in #TK-14; should be on staging tomorrow.");

        var vat = await BugFromDefectAsync("2026-11-10 15:45", p, l.VatDefect, "marcus",
            "VAT is rounded down on discounted orders",
            "The order summary uses the new basket calculator, but the confirmation email and the invoice still use the old one.\n\nRaised from #D-6.",
            "marcus");
        var vatTasks = new[]
        {
            await TaskAsync("2026-11-10 15:46", vat, "Confirmation email uses the new basket calculator", "marcus"),
            await TaskAsync("2026-11-10 15:47", vat, "Invoice PDF uses the new basket calculator", "marcus"),
            await TaskAsync("2026-11-10 15:48", vat, "Unit tests for half-up rounding on discounted lines", "aisha"),
        };
        await MoveCardAsync("2026-11-10 15:50", vat, BoardColumn.InProgress, "marcus");
        await DoneAsync("2026-11-10 16:55", vatTasks[2], "aisha");

        await BugFromDefectAsync("2026-11-10 16:12", p, l.BasketDefect, "liam",
            "Basket isn't merged after signing in on a second device",
            "The signed-in basket replaces the guest one instead of merging with it.\n\nRaised from #D-8.",
            "liam");
        await board.LinkAsync(p, cards, BoardLinkKind.Defect, l.ExpiredCardsDefect, Id("aisha"));

        // The old problem, checked in test week and closed.
        await BoardCommentAsync("2026-11-10 16:18", p, notes, "tom",
            "Checked on staging as part of the delivery tests: a 240-character note comes through whole in the admin tool.");
        await MoveCardAsync("2026-11-10 16:30", notes, BoardColumn.Closed, "priya");
        _ = collect; // waits in New for next sprint
    }

    private async Task<Guid> BoardItemAsync(string when, Guid projectId, BoardItemType type, string who, string title, string? body,
        string? assignee)
    {
        clock.At(when);
        return (await board.CreateAsync(projectId, type, new BoardItemInput(title, body, assignee is null ? null : Id(assignee)), Id(who))).Id;
    }

    private async Task<Guid> BugFromDefectAsync(string when, Guid projectId, Guid defectId, string who, string title, string body,
        string assignee, Defects.DefectTemplate template = Defects.DefectTemplate.Simple, string? expected = null, string? actual = null)
    {
        clock.At(when);
        return (await board.CreateAsync(projectId, BoardItemType.Bug,
            new BoardItemInput(title, body, Id(assignee), template, expected, actual), Id(who), fromDefectId: defectId)).Id;
    }

    private async Task<Guid> TaskAsync(string when, Guid parentId, string title, string assignee)
    {
        clock.At(when);
        var parent = await BoardProjectOfAsync(parentId);
        return (await board.CreateAsync(parent, BoardItemType.Task, new BoardItemInput(title, null, Id(assignee)), Id(assignee),
            parentId: parentId)).Id;
    }

    private async Task MoveCardAsync(string when, Guid itemId, BoardColumn column, string who)
    {
        clock.At(when);
        await board.MoveAsync(await BoardProjectOfAsync(itemId), itemId, column, int.MaxValue, Id(who));
    }

    private async Task DoneAsync(string when, Guid taskId, string who)
    {
        clock.At(when);
        await board.SetDoneAsync(await BoardProjectOfAsync(taskId), taskId, true, Id(who));
    }

    private async Task<Guid> BoardCommentAsync(string when, Guid projectId, Guid itemId, string who, string body, params string[] mentions)
    {
        clock.At(when);
        return await board.AddCommentAsync(projectId, itemId, Id(who), body, Ids(mentions));
    }

    private async Task<Guid> BoardProjectOfAsync(Guid itemId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.BoardItems.Where(i => i.Id == itemId).Select(i => i.ProjectId).FirstAsync();
    }
}
