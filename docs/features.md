[QA Tracker](../README.md) › [Docs](README.md) › Features

# Features

Everything QA Tracker does, area by area. For the same ground as a walkthrough with
screenshots, see the [user guide](user-guide.md).

- [Projects](#projects) · [Test cases](#test-cases) · [Defects](#defects) ·
  [Project dashboard](#project-dashboard) · [Comments](#comments) ·
  [Attachments](#attachments) · [Notifications](#notifications) · [Team](#team) ·
  [Sign-in and accounts](#sign-in-and-accounts) · [Settings](#personal-settings) ·
  [System settings](#system-settings-qa-only) · [Navigation](#navigation-and-layout) ·
  [Operations](#operations)

---

## Projects

- A project has a name, a one-line brief, free-text notes, and any number of **custom
  link buttons** (`http`, `https` or `mailto`) with copy-to-clipboard.
- **Status:** *Not Started* → *Upcoming* → *Active* → *Completed*, or *Cancelled*. A project
  becomes Upcoming automatically once it has testing dates (and goes back to Not Started if
  they're removed), and Active when its first scope or defect is added. Completed and
  Cancelled are set by hand. The status shows next to each project in the project switcher.
- **Testing schedule and estimates:** testing start and end dates (weekdays only, with
  half days for starting or ending at midday), estimated dev days, and a QA estimate,
  either typed in or worked out as a percentage of the dev days (a system-wide default,
  25% out of the box, which each project can override). The result shows as "x days"
  next to the project's status.
- **All projects** page: *Your projects* first, then by status, with each project's test
  results and how many actions need you. Filter to active projects, active and upcoming,
  or everything; search by name. The choice is remembered.
- A **project switcher** in the top bar sets your current project, which the side
  navigation follows.
- Deleting a project is in a **Danger zone** on its edit page, listing what goes with it
  and asking for the project's name.

## Test cases

- **Project → scope → test case.** A scope is an area of testing, marked
  **Functional** or **Non-functional**. Each scope has its own page.
- Scopes stay in the order they were created; **Reorder scopes** rearranges them by drag
  and drop or with arrow buttons.
- A test case has a scenario and steps, and can move to another scope without losing its
  results, comments, files or defect links.
- **Results:** *Not run*, *Running* (being tested now), *Passed*, *Failed*, *Blocked* (can't be
  run yet) and *Inconclusive* (needs a business decision), set from the test case page or with
  Pass / Fail / Reset on the plan. Each result records who set it and when.
- **Live updates:** on a test case page and the plan / scope pages (and the defect pages, below),
  setting results, commenting, reacting and linking happen in place, with no reload or jump back
  to the top, and a comment you're part-way through typing is kept. Other people's results and
  comments appear on their own within a few seconds.
- **Previous / Next** step through the cases still to run, skipping blocked and
  inconclusive ones, with counts.
- **Filter** the plan to all cases, outstanding cases (not run, running, failed or blocked) or
  one result. Switch between **Expanded** and **Collapsed** layouts; collapsed shows one row
  per scope with its counts and a progress bar. Both choices are remembered across
  projects.
- **History** on every test case: created, edited, results, moves, files added or
  removed, defects linked or unlinked, with who and when.
- **CSV export** of the plan, optionally **with results** (result, tester, date). The
  export can be re-imported.
- **IDs** for every scope (**S-1**) and test case (**T-1**, unique across the project's scopes),
  shown in the plan and on each test case, and included in exports.
- **CSV import** (with a downloadable template) of scopes, test cases, steps and defect
  links. Rows match by ID first, then by name, so a spreadsheet can rename scopes and test cases
  or move cases between scopes. The preview compares the file with the project and shows only
  what will change, with steps as a line-by-line diff; errors (duplicate names in a scope, bad or
  repeated IDs) block the import and warnings (missing type, the same name in two scopes, moves)
  are shown on their rows before anything is saved.

## Defects

- **Live updates**, like test cases: status, severity, assignment, the fix workflow, test case
  links, comments and reactions update in place, and changes other people make (on the defect,
  or new and moved-on defects on the list) appear on their own.
- Raised from a failed test case (steps pre-filled, linked automatically) or on their
  own, and linked to any number of test cases. A test case can also be created from a
  defect. Linking from a test case lists defects newest first, filtered by status (open
  by default, with counts per filter).
- **Templates:** *Simple* (one description) or *Advanced* (repro steps, expected and
  actual results), chosen per defect, with a system default and a per-project default.
- **Severity:** Low, Medium, High or Critical, colour-coded and chosen when it's raised.
- **Status:** Not fixed, Fixing, To check, Fixed, or Not a defect.
- **The fix workflow:** a developer selects *Start fixing*, then *Mark as fixed*. That
  moves the defect to *To check* and assigns a QA from the project team. The QA marks it
  *Fixed* (recording who tested it) or *Not fixed* (back to the developer who fixed it).
- **Assignee** picker limited to the project's team, plus *Assign to me* and *Unassign
  from me*.
- **Previous / Next** through the defects assigned to you that still need work, most
  severe first, with counts.
- The **defects page** groups defects into *To verify* / *To fix* (yours), *Unassigned*,
  *Other open* and *Closed*, most severe first. Filter to open, closed or one status; the
  choice is remembered.
- Linked test cases show their current result on the defect.
- **History** on every defect: status changes, assignments, severity, edits, template
  changes, linked test cases and files, with who and when.

## Project dashboard

- Test result tally and an **open defects** count. Defects agreed to be "not a defect"
  don't count.
- Testing window and estimates, links, notes, team, files and comments.
- **Actions needed**, shown only when something is waiting: defects for you to verify or
  fix, failed test cases with no defect raised, test cases ready to re-test because their
  defects are closed, and passed test cases that still have an open defect.
- **Recent activity**: the latest five things that happened in the project, loaded once the
  card scrolls into view, with **View all activity** for the full feed.

## Project activity

- One feed per project bringing together changes to the project itself (created, edited,
  status, team members, scopes created/renamed/deleted, files, CSV imports) and the headline
  moments of its test cases and defects: created, results recorded, defect status changes
  and assignments.
- Deleting a test case, defect or scope is recorded on the project, so it stays visible after
  the item and its own history are gone.
- Filter to **Project**, **Test cases** or **Defects**; **Load more** pages back through older
  activity in place.
- Each entry links to the test case, defect or scope it's about. The full detail (edits,
  files, links) stays in each item's own **History**.

## Comments

- Comments on projects, test cases and defects, each with its own thread.
- **@mentions** with a picker of the project team; mentioned people are notified.
- **Reactions:** thumbs up or down, with counts and who reacted on hover. The comment's
  author is notified.
- Files on comments, threads paged five at a time (newest first), **Ctrl+Enter** /
  **⌘+Enter** to post, and a confirmation before deleting.

## Attachments

- Files on projects, test cases, defects and comments, picked several at a time with an
  optional description, and searchable.
- **Preview** images, video, audio and PDFs in the app; other files download.
- Stored in **S3-compatible storage** (AWS S3, MinIO, Garage) or **Azure Blob Storage**.
  Every download goes through the app, so the bucket or container stays private.
- A maximum file size, set by the server and adjustable (lower) in System settings.

## Notifications

- A bell with an unread count, and a pop-up when something new arrives. Developers hear
  about new defects on their projects. People hear when a defect is assigned to them,
  handed to them to check, or sent back as not fixed; when a defect assigned to them gets
  a new comment; and about @mentions and reactions to their comments.
- An optional sound (four to choose from, with a preview and a volume setting). With QA
  Tracker open in several tabs, each notification plays and pops up once.
- Clear notifications one at a time or all at once.
- Delivered by light browser polling, with no WebSockets. It works across multiple app
  instances because they all read the same database.

## Team

- Add developers and QAs to a project from its dashboard. The team drives the assignee
  picker, @mentions, notifications and *Your projects*.

## Sign-in and accounts

- Every page needs a signed-in user; there's no self-service sign-up.
- **Local accounts**, created by a QA. The first QA can be created from environment
  variables.
- **Single sign-on** with Microsoft Entra ID or Keycloak (or another OpenID Connect
  provider) alongside local accounts, or as the only way in. Roles come from the
  provider's token, accounts are created on first sign-in, and provider-managed accounts
  are read-only in the app. See [Single sign-on](sso.md).
- Two roles, **QA** and **Dev**. Each person has exactly one.

## Personal settings

- Appearance: System, Light or Dark, applied before the page draws (no flash).
- Notification sound: on or off, which sound, and volume.
- Account details, plus changing email and password for local accounts (with the password rules,
  a strength meter and a password generator).

## System settings (QA only)

- **Users:** create, edit and delete accounts, with each person's role, sign-in method
  and last sign-in.
- **Passwords:** every new-password field lists the rules (from the server's own settings),
  ticks them off and rates strength as you type, blocks a password that breaks a rule before
  it's sent, and can generate a strong random one. New accounts show their sign-in details
  once, to copy.
- **User activity:** per user, across every project or one: results recorded, defects raised,
  fixed and verified, fixes sent back, comments and uploads, a per-project breakdown, and a
  paged drill-down for each plus a full timeline of what they did.
- **Usage:** projects, test cases, defects, attachments and storage used.
- **System status:** storage backend, sign-in setup, telemetry, and the running version
  linked to its release, with a check for updates.
- **Developer permissions:** whether developers can create, edit and delete projects,
  test cases and defects (which includes reassigning defects). Results and defect status stay
  QA-only.
- **Defaults:** the maximum upload size, the QA estimate percentage, and the defect
  template.

## Navigation and layout

- A side navigation that collapses to icons and remembers it.
- **On this page** links on dashboards, lists and detail pages.
- Edit forms share one set of buttons. *Save changes* stays disabled until something
  changes, you're warned before leaving with unsaved changes, **Ctrl+Enter** saves and
  **Esc** cancels.
- Short tips on the forms for new projects, scopes, test cases and users.

## Operations

- One container image; PostgreSQL is the only required dependency.
- Applies its own database migrations on start-up (safe with several instances starting
  at once), or as a separate step.
- Scales horizontally with no sticky sessions or Redis; health endpoints for
  orchestrators.
- Optional OpenTelemetry export to any OTLP endpoint or to Azure Monitor.
- One log line per request, with UTC timestamps.

See [Deployment](deployment.md) and [Configuration](configuration.md).

---

Next: [Configuration](configuration.md) · Back to the [documentation index](README.md)
