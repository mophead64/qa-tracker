[QA Tracker](../README.md) › [Docs](README.md) › Ways of working

# Ways of working: defects, bugs and the development board

QA Tracker is a testing tool first. A project's test plan, results, defects and the
fix-and-verify loop need nothing else, and that's how most projects run.

The **development board** is optional. It's switched off on every new project, and is
meant for **smaller projects** where the developers have nowhere else to plan the build. It
gives them stories, bugs and tasks next to the testing. If your team already works in Jira,
Azure DevOps or GitHub Issues, leave it off: defects are designed to work without it.

- [Do you need the board?](#do-you-need-the-board)
- [Bugs and defects: what's the difference?](#bugs-and-defects-whats-the-difference)
- [Example workflows](#example-workflows)
  1. [Testing only, no board](#1-testing-only-no-board)
  2. [A small project, start to finish, on the board](#2-a-small-project-start-to-finish-on-the-board)
  3. [Improving an existing system with known issues](#3-improving-an-existing-system-with-known-issues)
- [What happens automatically](#what-happens-automatically)

---

## Do you need the board?

| Leave it off when… | Switch it on when… |
|---|---|
| The developers already plan their work in another tool | A small team (a few developers, a QA or two) has nowhere else to track the build |
| You're testing someone else's build as a separate phase | You want the stories, the known bugs, the tests and the defects in one place |
| All you need is the test plan, the results and the defects | You want the development's progress next to the testing's, on the dashboard and the All projects page |

The board is set **per project**, under **Edit project → Development board**. Switch it on
partway through if a project grows into it. Switching it off hides the board but keeps its
items, so switching it back on brings them back. Projects without it look exactly as they
did before: no **Board** in the side navigation, no Board card on the dashboard, and no
board items on defect and test case pages.

## Bugs and defects: what's the difference?

Both describe something that's wrong, but they come from different places and do different
jobs.

**A bug (`BG-1`) is a problem you know about before testing starts.** It goes on the board
alongside the stories as work to plan. Bugs are typically:

- known issues in an existing system you're improving, such as the complaints customer
  services keeps forwarding;
- problems left over from a previous phase or release;
- fixes the team has already agreed to make, so they need planning like any other work.

**A defect (`D-1`) is a problem that testing finds in this project.** It's QA's record of
what went wrong. Defects are typically:

- a test case that failed;
- a problem with a feature that's being built or has just been built, found while testing it.

| | Bug | Defect |
|---|---|---|
| **Where it comes from** | Known going in: an existing system, an earlier phase, a backlog | Found in testing: a failed test case or a newly built feature |
| **Who usually raises it** | Whoever plans the work: a lead, a product owner, a developer | A QA, while testing |
| **Where it lives** | On the board, beside the stories (needs the board on) | On the **Defects** page (always there) |
| **How it moves** | New → In progress → Testing → Closed, with a checklist of tasks | Not fixed → Fixing → To check → Fixed (or Not a defect) |
| **What "done" means** | The work is finished and tested | A QA has re-tested it and it passed |

**A defect never needs a bug.** The fix-and-verify loop runs entirely on the defect: the
developer starts fixing, marks it fixed, and it lands with a QA to check. That's true with
or without the board.

**Create bug from this defect** is for the exceptions. Use it when a defect turns out to be
real development work worth planning, with tasks or several people, or when it's really a
problem that was there all along. The bug starts with the defect's write-up and is linked to
it. The developers' work is tracked on the bug, and the defect stays open, as QA's record,
until a QA checks the fix.

## Example workflows

The examples use the user guide's fictional cast: **Priya** and **Tom** test, and
**Marcus**, **Aisha** and **Liam** develop.

### 1. Testing only, no board

*An agency's developers plan their sprint in their own tracker. Priya's team is booked for a
week of testing before the release.*

1. **Set up.** Priya creates the project, gives it testing dates, adds the team, and leaves
   **Development board** off.
2. **Plan.** She writes the scopes and test cases, or [imports them from a
   spreadsheet](user-guide.md#import-a-plan-from-a-spreadsheet).
3. **Test.** Tom runs the cases. Each one that fails gets a defect: **Link or create defect**
   on the test case, so the two are linked.
4. **Fix.** Marcus sees the defects under *Actions needed*. **Start fixing**, then **Mark as
   fixed** hands each one to a QA.
5. **Verify.** Tom re-tests. **Fixed** closes the defect; **Not fixed** sends it back to
   Marcus. When a test case's defects are all closed, it shows as *Ready to retest*.
6. **Wrap up.** Every case has a result and the open defects are closed or agreed, so Priya
   marks the project **Completed** and exports the results.

The defects can still point at the developers' own tracker: paste the ticket link into a
comment, or add the tracker as one of the project's custom links.

### 2. A small project, start to finish, on the board

*Harbourline's checkout rebuild: three developers, two testers, and no other tracker.*

1. **Set up.** Priya creates the project with **Development board** on. **Board** appears in
   the side navigation, after **Dashboard**.
2. **Write the work.** The developers add a **story** for each feature (*Save a card for next
   time*, *Promo codes at checkout*) and break each into **tasks**.
3. **Add what's already known.** The old checkout drops delivery notes over 50 characters, and
   customer services has the complaints to prove it. Priya adds it as a **bug** on day one, so
   it's planned with the rest. A longer known-issues list can come in at once with **Import
   CSV**.
4. **Link the work to the tests.** As the test plan takes shape, each story and bug is linked
   to the test cases or whole scopes that cover it (**Link items**). The links show on both
   sides.
5. **Build.** Aisha drags *Save a card* to **In progress**. It's assigned to her, and she's
   recorded as its developer. She ticks the tasks off as she goes.
6. **Hand over.** She drags it to **Testing**, and it's assigned to the QA on the team with
   the least on. Tom runs the linked test cases.
7. **Problems found in testing are defects.** A linked test case fails, so Tom raises a
   defect from it and links the defect to the story. Aisha fixes it through the usual defect
   loop, while the story waits in Testing. If it needs more work first, moving the story back
   to **In progress** returns it to Aisha.
8. **Bigger problems get a bug.** The 3-D Secure defect turns out to need a change to how the
   bank's redirect comes back. Marcus uses **Create bug from this defect**, splits the work
   into tasks, and drags the bug through the board. The defect stays open until Tom checks
   the fix and marks it **Fixed**.
9. **Close.** When the story's tests pass and its defects are closed, Tom drags it to
   **Closed**. It's unassigned, and he's recorded as having tested it.

The dashboard's **Board** card shows how many items are in each column, and *Actions
needed* lists each person's open stories, bugs and tasks next to their defects.

### 3. Improving an existing system with known issues

*A "phase 2" for a site that's already live: a few new features, and a long list of known
problems from support.*

1. **Bring in the known issues.** Download the board's CSV template (**Import CSV → Download
   template**), fill it in from the support backlog, and import it. Each known problem
   becomes a **bug**, with its repro steps, its tasks and who it's assigned to. Add
   **stories** for the new features the same way.
2. **Write tests that prove the fixes.** For each bug, add a test case that fails today and
   should pass once it's fixed, and link the two. Add regression cases for the areas the
   work touches.
3. **Work the board.** The developers move bugs and stories through **In progress** and
   **Testing**. Each one is handed to a QA when it reaches Testing.
4. **Check each fix.** The QA runs the bug's linked test case. If it passes, the bug goes to
   **Closed**. If it fails, it's a defect like any other: raise one from the failed test case
   and link it to the bug, then move the bug back to **In progress**.
5. **Anything else testing turns up is a defect.** A problem that wasn't on the list going in,
   such as a regression or something wrong with a new feature, is raised as a defect. It's
   fixed through the defect loop, without touching the board.

The bug list shows how much of the known backlog is done, and the defects show what this
phase broke or missed.

## What happens automatically

Both workflows pass work along for you, and notify whoever it lands with.

| Board (stories and bugs) | Defects |
|---|---|
| **In progress**: assigned to whoever moved it, who becomes its developer. Back from Testing or Closed, it returns to that developer | **Start fixing**: *Fixing*, assigned to the developer who started it |
| **Testing**: to the QA who tested it before, otherwise the team's QA with the fewest open board items | **Mark as fixed**: *To check*, handed to a QA on the team |
| **Closed**: unassigned, recording who tested it (or, if nobody had it, who closed it) | **Fixed**: closed, recording the QA as the tester. **Not fixed**: back to the developer who fixed it |

Everything is recorded in each item's **History**, and each person chooses which of these
notifications they get in **Settings**.

---

Next: [Configuration](configuration.md) · Back to the [documentation index](README.md)
