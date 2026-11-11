[QA Tracker](../../README.md) › [Docs](../../docs/README.md) › Documentation screenshots

# Documentation screenshots

This tool produces every screenshot in `docs/screenshots/`. It starts a throwaway copy of
QA Tracker, fills it with a fictional consultancy's year of QA work, and captures each
screenshot with Playwright: dark mode, 1440×900, at 2× for sharp text.

```bash
tools/docs-screenshots/run.sh                   # everything (about a minute once the image is built)
tools/docs-screenshots/run.sh dashboard defect  # just these shots, on fresh data
```

You need Docker, the .NET 10 SDK and PowerShell (`pwsh`, which `run.sh` uses to install
Playwright's Chromium the first time).

## What run.sh does

1. Builds the app image from the working tree (`qatracker:docs`), stamped with today's date
   as the version.
2. Starts a separate stack ([`docker-compose.yml`](docker-compose.yml)): PostgreSQL,
   Garage and the app on **port 8090**, with its own compose project (`qat-docs`) and no
   volumes it keeps. Your own development database is never touched.
3. **Seeds** the demo data through the app's real services ([`DemoSeeder.cs`](DemoSeeder.cs)).
4. **Captures** the screenshots ([`Shots.List.cs`](Shots.List.cs)).
5. Restarts the app with local sign-in switched off for the SSO-only sign-in page.
6. Tears the stack down.

| Option | |
|---|---|
| `--keep` | Leave the stack running afterwards (handy for looking around or debugging a shot) |
| `--reuse name…` | Re-take shots against a stack left up by `--keep`, without rebuilding or reseeding |
| `--skip-build` | Reuse the existing `qatracker:docs` image |
| `--down` | Tear down a stack left up by `--keep` |

With the stack up, sign in at <http://localhost:8090> as `priya.raman@example.com` (QA) or
`marcus.chen@example.com` (Dev), password `Demo!Passw0rd`. The sign-in page shows
*Sign in with Microsoft Entra ID* because the stack is configured for Entra with
placeholder IDs. The button renders, but signing in with it can't complete.

Set `QATRACKER_DOCS_OUT=/some/dir` to write the images somewhere else while you experiment.

## The demo data

Everything is fictional: people at `example.com`, made-up clients, `.example` URLs. The
story's "now" is **the end of Tuesday 10 November 2026**:

- **Ten people**, three QAs and seven developers. Priya Raman (QA lead), Marcus Chen and
  Diego Alvarez have local passwords; the others look like accounts provisioned by
  Microsoft Entra ID (they carry the SSO badge), and Grace Mensah has never signed in.
- **Eight completed projects** from March to October, each fully tested, fixed, re-tested
  and signed off ([`DemoSeeder.History.cs`](DemoSeeder.History.cs)).
- **Harbourline Checkout v2**, the main showcase, is two days into a Monday-to-Friday test
  window. It has 34 test cases in every result, and 10 defects across the whole
  fix → verify → reject loop. It also has comments with @mentions and reactions, files of
  every previewable kind, and an inbox of unread notifications
  ([`DemoSeeder.Current.cs`](DemoSeeder.Current.cs)).
- **Meadowbrook Click & Collect**, finishing its testing, plus two upcoming projects.

The services run against a clock the seeder moves around ([`DemoClock`](Program.cs)), so
every record, history entry and notification has a believable timestamp. The bug
screenshots, screen recording, PDFs and logs that get attached are rendered from small
HTML mock-ups in [`SampleFiles.cs`](SampleFiles.cs).

## Adding to it

**A new feature with new data.** Add it to the story in `DemoSeeder.Current.cs` (or
`DemoSeeder.History.cs` for past projects), calling the same service methods the app's
endpoints use. Set the time first with `clock.At("2026-11-10 14:05")` or one of the helper
methods, which all take a time. Keep within the story: nothing after 10 November 17:45.

**A new screenshot.** Add a line to the `All` list in `Shots.List.cs` and a method for it.
The helpers do most of the work:

| Helper | Captures |
|---|---|
| `Crop.ViewportAsync` | The whole 1440×900 window, side navigation included |
| `Crop.ContentAsync` | The main column from the top of the page down to an element (the page is grown to fit; `fitWidth` for narrow forms) |
| `Crop.AroundAsync` | The box around one or more elements, plus padding |
| `ElementAsync` / `DialogAsync` | One card, or one open dialog on its own |

Then reference the image from the docs as `screenshots/<name>.png`.

**House rules:**

- **Never save anything in a shot.** Fill forms without submitting them, and cancel
  dialogs. Then the shots can run in any order, or one at a time.
- Each shot starts by marking the story's last-day notifications unread again (opening
  the bell marks them read), and sign-in times are put back to the story's after Playwright
  signs in.
- Blur the focus (`Crop.BlurAsync`) before capturing a form or dialog, so no focus ring
  shows.
- A full run adds about 6 MB of images. To keep the repository lean, re-take only the shots
  a change affects.

## Files

| File | |
|---|---|
| `run.sh` | The whole pipeline |
| `docker-compose.yml` | The throwaway stack |
| `Program.cs` | Commands (`samples`, `seed`, `shots`, `login-sso-only`), options and the clock |
| `DemoSeeder*.cs` | The demo data |
| `SampleFiles.cs` | The attachments the demo data uses |
| `Shots*.cs` | Sign-in, crop helpers, and the shot list |

The project isn't part of `QaTracker.slnx`, so CI doesn't build it.
