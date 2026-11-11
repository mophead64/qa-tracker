using QaTracker.Web.Attachments;
using QaTracker.Web.Comments;
using QaTracker.Web.Defects;
using QaTracker.Web.Projects;
using QaTracker.Web.TestCases;
using static QaTracker.Web.Defects.DefectSeverity;
using static QaTracker.Web.TestCases.TestCaseKind;
using static QaTracker.Web.TestCases.TestResult;

namespace DocsScreenshots;

/// <summary>What's happening now (the story's "now" is the end of Tuesday 10 November 2026):
/// Harbourline is two days into a five-day test window, Meadowbrook is finishing up, and two
/// more projects are booked in.</summary>
internal sealed partial class DemoSeeder
{
    private async Task<Guid> SeedHarbourlineAsync()
    {
        var p = await CreateProjectAsync("2026-10-19 10:00", "priya", "Harbourline Checkout v2",
            "Rebuild of the web checkout: one-page flow, saved cards, wallets, promo codes and click & collect.",
            """
            Testing against staging — build numbers are posted in the comments below. Test accounts and sandbox cards are in test-accounts.csv under Files; never use a real card.

            Out of scope this round: gift cards, PayPal and the mobile apps.
            Browsers: Safari 18, Chrome and Firefox (latest), iOS 18 and Android 15.
            """,
            [
                new("Staging", "https://staging.harbourline.example/checkout"),
                new("Repository", "https://git.example.com/harbourline/checkout-web"),
                new("Design spec", "https://design.example.com/harbourline/checkout-v2"),
                new("Email the team", "mailto:checkout-team@harbourline.example"),
            ],
            ["priya", "tom", "marcus", "aisha", "liam"],
            new ProjectSchedule(new(2026, 11, 9), new(2026, 11, 13), false, false, 20, null, null));

        // ---- the test plan, written over the fortnight before testing --------------------
        clock.At("2026-10-26 10:00");
        var basket = await ScopeAsync(p, Functional, "Basket", "priya");
        var c1 = await CaseAsync(basket, "Add an item to the basket from the product page",
            "1. Open any in-stock product page\n2. Choose a size and select Add to basket\n3. Open the basket\nExpected: the item is listed with the chosen size, and the basket badge shows 1", "priya");
        var c2 = await CaseAsync(basket, "Change the quantity of an item in the basket",
            "1. Add an item to the basket\n2. Change its quantity to 3\nExpected: the line total and the basket total update straight away", "priya");
        var c3 = await CaseAsync(basket, "Remove the last item and see the empty basket message",
            "1. With one item in the basket, select Remove\nExpected: 'Your basket is empty', with a link back to the shop", "priya");
        var c4 = await CaseAsync(basket, "Basket is kept after signing in on a second device",
            "1. Signed out, add two items to the basket on a laptop\n2. Sign in on a phone with an account that already has one item in its basket\nExpected: the phone shows all three items", "priya");
        var c5 = await CaseAsync(basket, "Out-of-stock items are flagged before checkout",
            "1. Add an item, then set its stock to 0 in the admin tool\n2. Go to checkout\nExpected: the item is flagged, and checkout is blocked until it's removed", "priya");

        clock.At("2026-10-26 14:00");
        var promos = await ScopeAsync(p, Functional, "Promo codes", "priya");
        var c6 = await CaseAsync(promos, "Apply a valid percentage promo code",
            "1. Fill the basket to over £50\n2. Apply WELCOME10\nExpected: 10% off the items; delivery isn't discounted", "priya");
        var c7 = await CaseAsync(promos, "An expired promo code is rejected",
            "1. Add any item to the basket and go to checkout\n2. Enter the promo code SUMMER25 (expired 31 October)\n3. Select Apply\nExpected: 'This code has expired', and no discount is applied", "priya");
        var c8 = await CaseAsync(promos, "Two promo codes can't be combined",
            "1. Apply WELCOME10\n2. Try to apply FREEDEL as well\nExpected: the second code replaces the first, with a message saying so", "priya");
        var c9 = await CaseAsync(promos, "Promo codes aren't case-sensitive",
            "1. Apply welcome10, in lowercase\nExpected: accepted, the same as WELCOME10", "priya");
        var c10 = await CaseAsync(promos, "A free-delivery code removes the delivery charge",
            "1. Choose standard delivery (£4.95)\n2. Apply FREEDEL\nExpected: delivery shows £0.00", "priya");

        clock.At("2026-10-27 10:00");
        var payment = await ScopeAsync(p, Functional, "Payment", "tom");
        var c11 = await CaseAsync(payment, "Pay with a saved card",
            "1. Sign in as the saved card customer (see test accounts)\n2. Check out and pick the saved card\n3. Enter the CVC and pay\nExpected: the order confirmation page appears", "tom");
        var c12 = await CaseAsync(payment, "Pay with a new card and save it for next time",
            "1. Check out with test card 4000 0000 0000 0077\n2. Tick 'Save this card'\nExpected: the payment goes through and the card appears under Saved cards", "tom");
        var c13 = await CaseAsync(payment, "3-D Secure challenge returns to the order confirmation",
            "1. Check out with the 3-D Secure test card 4000 0000 0000 3220\n2. Complete the bank's challenge screen with the code 1234\nExpected: you land on the order confirmation page, and the order is marked as paid", "tom");
        var c14 = await CaseAsync(payment, "A declined card shows a clear error",
            "1. Pay with the decline test card 4000 0000 0000 0002\nExpected: 'Your card was declined', with the basket and address kept", "tom");
        var c15 = await CaseAsync(payment, "Apple Pay is offered on supported browsers only",
            "1. Open checkout in Safari on a Mac with Apple Pay set up\n2. Open checkout in Firefox\nExpected: the Apple Pay button shows in Safari only", "tom");
        var c16 = await CaseAsync(payment, "Google Pay completes a payment",
            "1. Open checkout in Chrome with a Google Pay test card\n2. Pay with Google Pay\nExpected: the order confirmation page appears", "tom");
        var c17 = await CaseAsync(payment, "VAT is rounded correctly on discounted orders",
            "1. Add an item at £19.99\n2. Apply WELCOME10\n3. Check the VAT line in the order summary, the confirmation email and the invoice\nExpected: VAT of £3.00 everywhere (£17.99 ÷ 6, rounded half up)", "tom");
        var c18 = await CaseAsync(payment, "Expired saved cards aren't offered",
            "1. Sign in as the expired card customer (see test accounts)\n2. Go to the payment step\nExpected: the expired card is hidden, or greyed out and marked Expired", "tom");
        var c19 = await CaseAsync(payment, "Refreshing during payment doesn't charge twice",
            "1. Pay with a new card\n2. Refresh while 'Processing payment' is showing\nExpected: one charge in the payment sandbox and one order", "tom");

        clock.At("2026-10-28 11:00");
        var delivery = await ScopeAsync(p, Functional, "Delivery & collection", "tom");
        var c20 = await CaseAsync(delivery, "Choose a delivery slot",
            "1. At the delivery step, pick tomorrow 10am–12pm\nExpected: the slot is shown in the order summary", "tom");
        var c21 = await CaseAsync(delivery, "Same-day slots disappear after the 2pm cut-off",
            "1. After 2pm, open the delivery step\nExpected: the earliest slot offered is tomorrow's", "tom");
        var c22 = await CaseAsync(delivery, "Change the delivery address at checkout",
            "1. At the delivery step, select Change address\n2. Add a new address\nExpected: the new address is used and the delivery charge is recalculated", "tom");
        var c23 = await CaseAsync(delivery, "Postcode lookup accepts lowercase postcodes",
            "1. Type the postcode 'bs1 4dj' into the address finder\nExpected: the address list appears, the same as for 'BS1 4DJ'", "tom");
        var c25 = await CaseAsync(delivery, "Delivery notes are saved with the order",
            "1. Add the note 'Leave with the neighbour at no. 12'\n2. Place the order\nExpected: the note appears on the order in the admin tool", "tom");

        clock.At("2026-10-29 15:00");
        var confirmation = await ScopeAsync(p, Functional, "Order confirmation", "priya");
        var c26 = await CaseAsync(confirmation, "Confirmation page shows the order number and summary",
            "1. Place any order\nExpected: the order number, items, delivery slot and total are shown", "priya");
        var c27 = await CaseAsync(confirmation, "Confirmation email arrives within two minutes",
            "1. Place any order\nExpected: the confirmation email arrives within two minutes and matches the confirmation page", "priya");
        var c28 = await CaseAsync(confirmation, "The order appears in the account's order history",
            "1. Place an order while signed in\n2. Open Account → Orders\nExpected: the new order is at the top", "priya");
        var c24 = await CaseAsync(confirmation, "Click & collect shows store opening hours",
            "1. Choose Click & collect\n2. Pick a store\nExpected: today's opening hours are shown next to each store", "priya");

        clock.At("2026-10-30 10:00");
        var a11y = await ScopeAsync(p, NonFunctional, "Accessibility", "priya");
        var c29 = await CaseAsync(a11y, "Checkout can be completed using only the keyboard",
            "1. Using only Tab, Shift+Tab, Enter and Space, go from the basket to the confirmation page\nExpected: every control can be reached, and the focus is always visible", "priya");
        var c30 = await CaseAsync(a11y, "Screen readers announce form errors",
            "1. With VoiceOver on, submit the address form with the postcode missing\nExpected: the error is announced once, and focus moves to the postcode field", "priya");
        var c31 = await CaseAsync(a11y, "Payment form colour contrast meets WCAG 2.2 AA",
            "1. Check the payment form's text, borders and error states with a contrast checker\nExpected: at least 4.5:1 for text and 3:1 for borders", "priya");

        clock.At("2026-11-02 10:00");
        var perf = await ScopeAsync(p, NonFunctional, "Performance & security", "tom");
        var c32 = await CaseAsync(perf, "Checkout page loads in under 2 seconds on a 4G connection",
            "1. Run the page test against staging with 4G throttling, five times\nExpected: median largest contentful paint under 2 seconds", "tom");
        var c33 = await CaseAsync(perf, "Payment API p95 stays under 800 ms at 50 requests per second",
            "1. Run the payments load script for 10 minutes at 50 requests per second\nExpected: p95 under 800 ms and no errors", "tom");
        var c34 = await CaseAsync(perf, "Card numbers never appear in logs or analytics",
            "1. Pay with each test card\n2. Search the staging logs and analytics events for the card numbers\nExpected: no matches", "tom");

        // A little life in the plan's history before testing starts.
        clock.At("2026-11-03 09:40");
        await cases.MoveAsync(c24, delivery, Id("tom"));
        clock.At("2026-11-05 16:10");
        await cases.UpdateAsync(c7, new TestCaseInput("An expired promo code is rejected",
            "1. Add any item to the basket and go to checkout\n2. Enter the promo code SUMMER25 (expired 31 October)\n3. Select Apply\n4. Repeat with AUTUMN15 on the day it expires\nExpected: 'This code has expired', and no discount is applied"), Id("priya"));

        await AttachAsync("2026-11-06 15:00", AttachmentOwner.Project, p, "harbourline-test-plan.pdf", "priya", "Test plan v1.2");
        await AttachAsync("2026-11-06 15:01", AttachmentOwner.Project, p, "checkout-flow.png", "priya", "The flow under test");
        await AttachAsync("2026-11-06 15:02", AttachmentOwner.Project, p, "test-accounts.csv", "priya", "Test accounts and sandbox cards");

        // ---- Monday 9 November: day 1 ---------------------------------------------------------
        await ProjectCommentAsync("2026-11-09 09:05", p, "priya",
            "Kick-off: testing runs Monday to Friday this week against staging build 1.4.0. Test accounts and sandbox cards are under Files. I'll post a summary at the end of each day.");
        await ResultAsync("2026-11-09 09:40", c1, Passed, "priya");
        await ResultAsync("2026-11-09 09:48", c2, Passed, "priya");
        await ResultAsync("2026-11-09 09:55", c3, Passed, "priya");
        await ResultAsync("2026-11-09 10:20", c5, Passed, "tom");

        await ResultAsync("2026-11-09 11:00", c23, Failed, "tom");
        var d1 = await DefectAsync("2026-11-09 11:03", p, "tom", "Postcode lookup rejects lowercase postcodes", Low,
            "1. Go to the delivery step\n2. Type 'bs1 4dj' into the address finder", "The address list appears",
            "'We couldn't find that postcode' — the same postcode works in capitals", assignee: "liam", linkedCases: [c23]);

        await ResultAsync("2026-11-09 11:20", c6, Passed, "priya");
        await ResultAsync("2026-11-09 11:40", c7, Failed, "priya");
        var d2 = await DefectAsync("2026-11-09 11:42", p, "priya", "Expired promo code still applies its discount", High,
            "1. Add any item to the basket\n2. At checkout, apply SUMMER25 (expired 31 October)",
            "'This code has expired', and no discount", "The code is accepted and 25% is taken off the order",
            assignee: "aisha", linkedCases: [c7]);
        await AttachAsync("2026-11-09 11:44", AttachmentOwner.Defect, d2, "promo-code-expired.png", "priya", "SUMMER25 accepted on 9 Nov");
        await DefectCommentAsync("2026-11-09 11:46", d2, "priya",
            "Also happens with AUTUMN15, which expired yesterday. Screenshot attached — the code shows as applied and the total is discounted.");
        await AttachAsync("2026-11-09 11:47", AttachmentOwner.TestCase, c7, "promo-codes.csv", "priya", "Codes used in this test, with their expiry dates");
        await ResultAsync("2026-11-09 11:50", c9, Passed, "priya");

        await StartFixingAsync("2026-11-09 13:10", d1, "liam");
        await ResultAsync("2026-11-09 13:30", c11, Passed, "tom");
        await ResultAsync("2026-11-09 13:50", c12, Passed, "tom");

        await ResultAsync("2026-11-09 14:20", c13, Failed, "tom");
        var d3 = await DefectAsync("2026-11-09 14:22", p, "tom", "3-D Secure challenge loops back to the payment step in Safari", Critical,
            "1. In Safari 18, check out with test card 4000 0000 0000 3220\n2. Complete the bank's challenge with the code 1234",
            "The order confirmation page",
            "Back on the payment step with the card details cleared. No order is created, but the payment is authorised.",
            linkedCases: [c13]);
        await AttachAsync("2026-11-09 14:24", AttachmentOwner.Defect, d3, "3ds-challenge-loop.webm", "tom", "Screen recording, Safari 18");
        await AttachAsync("2026-11-09 14:24", AttachmentOwner.Defect, d3, "safari-console.log", "tom", "Console output");
        var d3c1 = await DefectCommentAsync("2026-11-09 14:26", d3, "tom",
            "@Marcus Chen this only happens in Safari — Chrome and Firefox are fine. Recording and console log attached. It also hits saved cards, so I've linked that test case too.",
            "marcus");
        clock.At("2026-11-09 14:31");
        await defects.LinkTestCaseAsync(d3, c11, Id("tom"));
        await ResultAsync("2026-11-09 14:40", c14, Passed, "tom");
        await StartFixingAsync("2026-11-09 14:50", d3, "marcus");
        var d3c2 = await DefectCommentAsync("2026-11-09 15:10", d3, "marcus",
            "Thanks — Safari drops our session cookie on the cross-site POST back from the bank (SameSite=Lax), so we restart the checkout. Moving the return step to a GET with a one-time token.");
        await ReactAsync("2026-11-09 15:12", CommentKind.Defect, d3c2, "tom");
        await ReactAsync("2026-11-09 15:13", CommentKind.Defect, d3c1, "marcus");
        await ResultAsync("2026-11-09 15:10", c26, Passed, "priya");
        await ResultAsync("2026-11-09 15:25", c27, Passed, "priya");

        await DefectCommentAsync("2026-11-09 15:44", d1, "liam", "Postcodes are now upper-cased and trimmed before the lookup. Going out in 1.4.1.");
        await MarkFixedAsync("2026-11-09 15:45", p, d1, "liam", "tom");
        await VerifyAsync("2026-11-09 16:30", d1, "tom");
        await ProjectCommentAsync("2026-11-09 16:40", p, "marcus", "Build 1.4.1 is on staging — includes the postcode fix (#1).");

        await ResultAsync("2026-11-09 16:55", c15, Blocked, "tom");
        await CaseCommentAsync("2026-11-09 16:56", c15, "tom", "Blocked: the Apple Pay sandbox merchant ID isn't set up on staging yet.");
        await ResultAsync("2026-11-09 16:58", c16, Blocked, "tom");
        await CaseCommentAsync("2026-11-09 16:59", c16, "tom", "Blocked for the same reason as Apple Pay — no wallet merchant ID on staging.");

        await ProjectCommentAsync("2026-11-09 17:20", p, "tom",
            "Day 1: 16 run — 11 passed, 3 failed, 2 blocked. Apple Pay and Google Pay are blocked until the sandbox merchant ID is set up on staging. @Liam Doyle could you chase it?",
            "liam");
        var liamReply = await ProjectCommentAsync("2026-11-09 17:35", p, "liam", "Requested it this afternoon — should land tomorrow morning.");
        await ReactAsync("2026-11-09 17:40", CommentKind.Project, liamReply, "tom");

        // ---- Tuesday 10 November: day 2 ------------------------------------------------------
        await ResultAsync("2026-11-10 09:10", c23, Passed, "tom");
        await StartFixingAsync("2026-11-10 09:20", d2, "aisha");

        await ResultAsync("2026-11-10 10:15", c29, Failed, "priya");
        var d4 = await DefectAsync("2026-11-10 10:17", p, "priya", "Focus is lost after closing the address dialog", Medium,
            "1. At the delivery step, open 'Add a new address' with the keyboard\n2. Press Escape to close it\n3. Press Tab",
            "Focus goes back to the 'Add a new address' button",
            "Focus jumps to the top of the page, and the next Tab goes to the site logo", assignee: "liam", linkedCases: [c29]);
        await AttachAsync("2026-11-10 10:18", AttachmentOwner.Defect, d4, "keyboard-focus-lost.png", "priya");
        await ResultAsync("2026-11-10 10:20", c20, Passed, "tom");

        await ResultAsync("2026-11-10 10:40", c21, Failed, "tom");
        var d5 = await DefectAsync("2026-11-10 10:42", p, "tom", "Same-day delivery slots are still offered after the 2pm cut-off", Medium,
            "1. After 2pm, go to the delivery step", "The earliest slot offered is tomorrow's",
            "Today's 4–6pm and 6–8pm slots are still offered (screenshot taken at 14:37 on a device set to 10 Nov)",
            assignee: "aisha", linkedCases: [c21]);
        await AttachAsync("2026-11-10 10:43", AttachmentOwner.Defect, d5, "delivery-slots-after-cutoff.png", "tom");
        await ResultAsync("2026-11-10 10:55", c22, Passed, "tom");

        var d2c = await DefectCommentAsync("2026-11-10 11:02", d2, "aisha",
            "The expiry check compared the end date at midnight UTC, so codes stayed valid for most of their last day — and the basket cached them for another 24 hours. Both fixed in 1.4.2.");
        await MarkFixedAsync("2026-11-10 11:05", p, d2, "aisha", "priya");
        var c7c = await CaseCommentAsync("2026-11-10 11:07", c7, "aisha",
            "The fix is in 1.4.2 — #2 is with @Priya Raman to verify. AUTUMN15 is a good one to re-test with, as it expired on Sunday.",
            "priya");
        await ProjectCommentAsync("2026-11-10 11:10", p, "aisha", "Build 1.4.2 is on staging with the promo code fix (#2).");
        await ReactAsync("2026-11-10 11:33", CommentKind.TestCase, c7c, "priya");
        await ReactAsync("2026-11-10 11:31", CommentKind.Defect, d2c, "priya");

        await ResultAsync("2026-11-10 11:30", c17, Failed, "priya");
        var d6 = await DefectAsync("2026-11-10 11:32", p, "priya", "VAT is rounded down on discounted orders", High,
            "1. Add an item at £19.99\n2. Apply WELCOME10\n3. Check the VAT line in the order summary",
            "VAT of £3.00 (£17.99 ÷ 6, rounded half up)", "VAT of £2.99", assignee: "marcus", linkedCases: [c17]);
        await ResultAsync("2026-11-10 11:40", c32, Passed, "tom");
        await AttachAsync("2026-11-10 11:41", AttachmentOwner.TestCase, c32, "checkout-performance-report.pdf", "tom", "Median of five runs");
        await StartFixingAsync("2026-11-10 12:00", d6, "marcus");

        var d7 = await DefectAsync("2026-11-10 12:15", p, "tom", "Promo code box accepts more than 500 characters", Low,
            "The promo code box lets you paste in a 500+ character string. Nothing breaks, but it looks odd and the field scrolls sideways.",
            template: DefectTemplate.Simple);
        await DefectCommentAsync("2026-11-10 12:40", d7, "aisha",
            "The API rejects anything over 32 characters, and the box gets a 32-character limit with the design system update next month. Happy to close this one?");
        await DefectCommentAsync("2026-11-10 12:48", d7, "priya", "Agreed — closing as not a defect.");
        clock.At("2026-11-10 12:50");
        await defects.SetStatusAsync(d7, DefectStatus.NotADefect, Id("priya"));

        await ResultAsync("2026-11-10 13:00", c4, Failed, "priya");
        var d8 = await DefectAsync("2026-11-10 13:02", p, "priya", "Basket isn't merged after signing in on a second device", Medium,
            "1. Signed out, add two items to the basket on a laptop\n2. Sign in on a phone where the account already has one item",
            "Three items in the basket", "Only the phone's item — the two from the laptop are gone", assignee: "liam", linkedCases: [c4]);
        await StartFixingAsync("2026-11-10 13:30", d4, "liam");
        await MarkFixedAsync("2026-11-10 13:40", p, d6, "marcus", "tom");

        await ResultAsync("2026-11-10 14:00", c18, Failed, "tom");
        var d9 = await DefectAsync("2026-11-10 14:02", p, "tom", "Expired saved cards are still offered at checkout", Medium,
            "1. Sign in as the expired card customer\n2. Go to the payment step",
            "The expired card is hidden or marked Expired", "The card is offered as normal and the payment fails at the bank",
            assignee: "marcus", linkedCases: [c18]);
        await StartFixingAsync("2026-11-10 14:20", d9, "marcus");
        await DefectCommentAsync("2026-11-10 14:29", d4, "liam", "The dialog now returns focus to whatever opened it. In 1.4.3, deploying now.");
        await MarkFixedAsync("2026-11-10 14:30", p, d4, "liam", "priya");

        await RejectAsync("2026-11-10 15:10", d6, "tom");
        var d6c = await DefectCommentAsync("2026-11-10 15:11", d6, "tom",
            "The order summary is right now, but the confirmation email and the invoice PDF still say £2.99 — see the screenshot. Sending it back.");
        await AttachAsync("2026-11-10 15:11", AttachmentOwner.DefectComment, d6c, "invoice-vat.png", "tom");
        await MarkFixedAsync("2026-11-10 15:20", p, d9, "marcus", "tom");
        await ResultAsync("2026-11-10 15:30", c30, Failed, "priya");
        await CaseCommentAsync("2026-11-10 15:32", c30, "priya",
            "VoiceOver reads each error twice: once from the field and once from the summary. @Liam Doyle is that the component library? I'll raise a defect once we know where it lives.",
            "liam");
        await DefectCommentAsync("2026-11-10 15:40", d6, "marcus",
            "Good catch — the email and the invoice still use the old basket calculator. Moving both onto the new one.");
        await ResultAsync("2026-11-10 15:45", c31, Passed, "priya");

        await StartFixingAsync("2026-11-10 16:10", d8, "liam");
        await ResultAsync("2026-11-10 16:15", c25, Passed, "tom");
        await DefectAsync("2026-11-10 16:20", p, "tom", "Delivery note placeholder is cut off on small screens", Low,
            "On a 375px-wide screen the placeholder reads 'e.g. Leave with a neighb…'. A shorter placeholder would fit.",
            template: DefectTemplate.Simple);
        await ResultAsync("2026-11-10 16:30", c34, Passed, "priya");
        await ResultAsync("2026-11-10 16:45", c8, Inconclusive, "tom");
        await CaseCommentAsync("2026-11-10 16:47", c8, "tom",
            "@Priya Raman the spec says one code per order, but WELCOME10 and FREEDEL are both applied and the basket shows both. Is a delivery code allowed on top of a discount code? Marking this inconclusive until product confirms.",
            "priya");
        await VerifyAsync("2026-11-10 16:50", d9, "tom");
        await CaseCommentAsync("2026-11-10 17:15", c8, "priya", "Checking with the product owner tomorrow morning — leave it as inconclusive for now.");

        var day2 = await ProjectCommentAsync("2026-11-10 17:30", p, "priya",
            "Day 2: 29 of 34 run — 18 passed, 8 failed, 2 blocked, 1 inconclusive. #2 and #4 are back with me to verify first thing tomorrow; the wallet tests are still waiting on the merchant ID.");
        await ReactAsync("2026-11-10 17:36", CommentKind.Project, day2, "marcus");
        await DefectCommentAsync("2026-11-10 17:40", d5, "aisha",
            "@Priya Raman is the 2pm cut-off the same for every store? The store data has its own cut-off field, but it's empty for most of them.",
            "priya");

        _ = (c10, c19, c28, c33); // left to run later in the week
        return p;
    }

    private async Task SeedMeadowbrookAsync()
    {
        var p = await CreateProjectAsync("2026-10-12 10:00", "elena", "Meadowbrook Click & Collect",
            "Click & collect for 140 grocery stores: slot booking, the picking app and order-ready texts.",
            "Picking app builds come from the internal app store — ask Diego for an invite.",
            [new("Staging", "https://staging.meadowbrook.example"), new("Picking app builds", "https://apps.example.com/meadowbrook-picking")],
            ["elena", "priya", "diego", "hana"],
            new ProjectSchedule(new(2026, 11, 4), new(2026, 11, 12), true, true, 25, null, 30));

        clock.At("2026-10-20 10:00");
        var finder = await ScopeAsync(p, Functional, "Store finder", "elena");
        var m1 = await CaseAsync(finder, "Find the nearest store by postcode", null, "elena");
        var m2 = await CaseAsync(finder, "Store page shows collection hours", null, "elena");
        var slots = await ScopeAsync(p, Functional, "Collection slots", "elena");
        var m3 = await CaseAsync(slots, "Book a collection slot", null, "elena");
        var m4 = await CaseAsync(slots, "Full slots are no longer offered", null, "elena");
        var m5 = await CaseAsync(slots, "Change the collection slot after ordering", null, "elena");
        var texts = await ScopeAsync(p, Functional, "Customer notifications", "priya");
        var m6 = await CaseAsync(texts, "Customer is texted when the order is ready", null, "priya");
        var m7 = await CaseAsync(texts, "Reminder is sent an hour before the slot", null, "priya");
        var picking = await ScopeAsync(p, Functional, "Picking app", "priya");
        var m8 = await CaseAsync(picking, "Pickers see orders in slot order", null, "priya");
        var m9 = await CaseAsync(picking, "Substitutions are offered to the customer", null, "priya");
        var m10 = await CaseAsync(picking, "Customer can reject a substitution", null, "priya");
        var load = await ScopeAsync(p, NonFunctional, "Load testing", "elena");
        var m11 = await CaseAsync(load, "Store finder responds in under 500 ms", null, "elena");
        var m12 = await CaseAsync(load, "Slot booking holds up with 500 customers at once", null, "elena");

        await ProjectCommentAsync("2026-11-04 13:05", p, "elena", "Testing starts this afternoon on picking app build 3.2.0 and web build 2.8.1.");
        await ResultAsync("2026-11-04 13:40", m1, Passed, "elena");
        await ResultAsync("2026-11-04 14:05", m2, Failed, "elena");
        var dc = await DefectAsync("2026-11-04 14:07", p, "elena", "24-hour stores show as 'Closed' after midnight", Low,
            "1. After midnight, open a 24-hour store's page", "'Open 24 hours'", "'Closed — opens at 00:00'", assignee: "diego", linkedCases: [m2]);
        await ResultAsync("2026-11-04 15:00", m3, Passed, "priya");
        await ResultAsync("2026-11-04 15:30", m4, Failed, "priya");
        var dd = await DefectAsync("2026-11-04 15:32", p, "priya", "Slot picker still offers slots that have already started", Medium,
            "1. At 10:15, open the slot picker", "The first slot is 11:00–12:00", "10:00–11:00 is still offered", assignee: "hana", linkedCases: [m4]);
        await StartFixingAsync("2026-11-05 09:40", dd, "hana");
        await StartFixingAsync("2026-11-05 10:10", dc, "diego");
        await ResultAsync("2026-11-05 10:30", m5, Passed, "elena");
        await ResultAsync("2026-11-05 11:00", m6, Passed, "elena");
        await MarkFixedAsync("2026-11-05 11:30", p, dc, "diego", "priya");
        await VerifyAsync("2026-11-05 14:00", dc, "priya");
        await ResultAsync("2026-11-05 14:05", m2, Passed, "priya");
        await MarkFixedAsync("2026-11-05 15:00", p, dd, "hana", "elena");
        await VerifyAsync("2026-11-06 10:00", dd, "elena");
        await ResultAsync("2026-11-06 10:05", m4, Passed, "elena");
        await ResultAsync("2026-11-06 11:30", m7, Failed, "elena");
        var db = await DefectAsync("2026-11-06 11:32", p, "elena", "Collection reminder arrives two hours early since the clocks went back", Medium,
            "1. Book a 15:00–16:00 slot\n2. Wait for the reminder", "Reminder at 14:00", "Reminder at 12:00", assignee: "hana", linkedCases: [m7]);
        await ResultAsync("2026-11-06 14:00", m8, Passed, "priya");
        await ResultAsync("2026-11-06 14:30", m9, Passed, "priya");
        await ProjectCommentAsync("2026-11-06 16:00", p, "priya", "Store finder and slots look solid. The reminder timing (#3) is the only defect still open.");
        await ResultAsync("2026-11-09 10:00", m10, Failed, "priya");
        var da = await DefectAsync("2026-11-09 10:02", p, "priya", "Rejected substitutions are still charged", High,
            "1. Picker substitutes semi-skimmed milk for skimmed\n2. Customer rejects the substitution in the app",
            "The substitution is removed from the bill", "The customer is charged for it", assignee: "diego", linkedCases: [m10]);
        await DefectCommentAsync("2026-11-09 09:15", db, "hana",
            "The reminder job runs in UTC and adds the summer-time offset by hand. Moving it to each store's time zone.");
        await StartFixingAsync("2026-11-09 11:00", da, "diego");
        await ResultAsync("2026-11-09 14:00", m11, Passed, "elena");
        await MarkFixedAsync("2026-11-10 15:00", p, da, "diego", "elena");
        _ = m12; // load test booked for Thursday
    }

    private async Task SeedUpcomingAsync()
    {
        var wrenfield = await CreateProjectAsync("2026-11-03 10:00", "priya", "Wrenfield Quote Engine",
            "Instant car and home insurance quotes for brokers, replacing the overnight pricing batch.",
            null, [new("Staging", "https://quotes.wrenfield.example")],
            ["priya", "tom", "noah", "hana"],
            new ProjectSchedule(new(2026, 11, 23), new(2026, 11, 27), false, false, 18, null, null));
        await ProjectCommentAsync("2026-11-06 10:40", wrenfield, "noah", "The pricing API will be on staging by 18 November, and the broker portal by the 20th.");

        await CreateProjectAsync("2026-11-06 14:00", "elena", "Ashcombe Smart Meter Portal",
            "Customer portal for smart meter readings, usage charts and tariff switching.",
            null, [], ["elena", "diego", "grace"],
            new ProjectSchedule(null, null, false, false, 12, null, null));
    }
}
