// Notifications for static-SSR pages — the bell badge, the dropdown's contents, and
// near-real-time toasts — with no circuit, WebSocket or extra service.
//
//   * The dropdown's list comes from GET /notifications/panel, fetched every time the bell
//     is opened (and refreshed while it stays open) — so notification text never sits in
//     every page's DOM.
//   * GET /notifications/feed is polled for a live badge count and toast payloads — interval
//     from QATRACKER_NOTIFICATIONS_POLL_SECONDS (default 45s). Polling keeps running while
//     the tab is backgrounded/minimized too (just less often — see pollMs()), so the sound
//     still fires without the tab having focus; only a closed tab stops it. Coming back into
//     view triggers an immediate catch-up poll on top of that.
//   * A sound plays for notifications a poll finds that the tab hasn't seen yet — never just
//     for opening the bell, which only replays already-seen ones. The sound prefs
//     (data-notif-sound / data-notif-sound-enabled on the bell) come from claims, so no extra
//     request is needed to read them. The Settings page's sound pickers use the same
//     [data-play-sound] handler to preview a choice regardless of the enabled toggle.
//
// Cross-instance safe with zero extra infrastructure: every app instance reads the same
// Notifications table in Postgres, so it doesn't matter which instance answers a request.
// Document-level listeners so this survives enhanced navigation.

(function () {
    var TOAST_MS = 9000;
    var seenIds = null; // notification ids already toasted this session; null until the first poll
    var failStreak = 0;
    var timer = null;
    var panelBusy = false;
    var feedBusy = false;
    var unreadCount = 0; // last known count, kept so the title prefix survives an enhanced nav

    function bell() {
        return document.getElementById("notification-bell");
    }

    // Interval between polls — server-set from QATRACKER_NOTIFICATIONS_POLL_SECONDS
    // (data-poll-seconds on the bell), default 45s. Backgrounded/minimized tabs poll far less
    // often (a floor of 2 minutes, or 4x the visible interval if that's longer) rather than
    // not at all — enough to still notice a new notification and play its sound without
    // hammering the server for a tab nobody's looking at.
    function pollMs() {
        var base = (Number(bell()?.dataset.pollSeconds) || 45) * 1000;
        return document.hidden ? Math.max(base * 4, 120000) : base;
    }

    // --- Sound ------------------------------------------------------------------

    // One <audio> element per sound, created once and reused — not a fresh `new Audio()` on
    // every play. A throwaway, unreferenced Audio element is more likely to have its playback
    // silently dropped (nothing keeps it preloaded, nothing keeps a strong reference to it)
    // exactly in the case that matters most here: a poll firing while the tab is backgrounded.
    // Reusing a warmed-up, already-loaded element is the more reliable pattern cross-browser.
    var audioCache = Object.create(null);

    function audioFor(key) {
        var audio = audioCache[key];
        if (!audio) {
            audio = new Audio("/sounds/" + key + ".mp3");
            audio.preload = "auto";
            audioCache[key] = audio;
        }
        return audio;
    }

    function playSound(key) {
        if (!key) return;
        try {
            var audio = audioFor(key);
            audio.currentTime = 0;
            audio.play().catch(function (err) {
                // Most commonly the browser's autoplay policy blocking playback because this
                // page hasn't seen a user gesture yet (see unlock() below) — logged so it's at
                // least visible in the console instead of silently vanishing; the badge/toast
                // still landed either way.
                console.warn("QA Tracker: notification sound blocked —", err?.message);
            });
        } catch (_) {
            // Audio unsupported — nothing useful to do.
        }
    }

    // Browsers only allow programmatic audio playback once the page has registered a genuine
    // user gesture (click/key/tap) — before that, a poll firing while the tab is backgrounded
    // plays nothing at all. Priming on the very first gesture, rather than waiting for
    // whichever gesture happens to first trigger a real play, means a user who glances at the
    // app and switches away almost immediately still has audio unlocked for the rest of the
    // session. Deliberately a throwaway, muted Audio instance of its own — NEVER one of the
    // audioFor() elements playSound() reuses — so this can't race a real, concurrently-playing
    // notification sound on shared currentTime/muted/paused state.
    var unlocked = false;
    function unlock() {
        if (unlocked) return;
        var key = bell()?.dataset.notifSound;
        if (!key) return; // signed out, or the bell hasn't rendered yet — try again next gesture
        unlocked = true;
        document.removeEventListener("pointerdown", unlock);
        document.removeEventListener("keydown", unlock);

        var primer = new Audio("/sounds/" + key + ".mp3");
        primer.muted = true;
        primer.play().catch(function () {
            // Nothing to clean up — a muted clip that never played leaves no state behind.
        });
    }
    document.addEventListener("pointerdown", unlock);
    document.addEventListener("keydown", unlock);

    // Settings page preview buttons: [data-play-sound="<key>"] plays that sound on click,
    // independent of the current enabled/disabled preference.
    document.addEventListener("click", function (e) {
        var btn = e.target.closest("[data-play-sound]");
        if (btn) playSound(btn.dataset.playSound);
    });

    // --- Dropdown contents ------------------------------------------------------

    async function loadPanel() {
        var panel = bell()?.querySelector("[data-notif-panel]");
        if (!panel || panelBusy) return;
        panelBusy = true;
        try {
            var url = "/notifications/panel?returnUrl=" + encodeURIComponent(location.pathname + location.search);
            var r = await fetch(url, { headers: { Accept: "text/html" } });
            if (r.ok) panel.innerHTML = await r.text();
        } catch (_) {
            // Leave whatever's already rendered in place.
        } finally {
            panelBusy = false;
        }
    }

    // The `toggle` event doesn't bubble — listen in the capture phase. Opening the bell marks
    // everything read server-side as part of loadPanel() (see NotificationPanel) — awaited
    // before pollFeed() so the badge reflects that straight away instead of on the next poll.
    document.addEventListener("toggle", async function (e) {
        if (e.target?.id === "notification-bell" && e.target.open) {
            await loadPanel();
            pollFeed();
        }
    }, true);

    // The panel's own "×" and "Clear all" forms (see NotificationPanel) are plain HTML forms
    // so they still work with JS off — a real full-page POST + redirect. With JS on, submit
    // them as a background fetch instead and refresh just the panel + badge in place, so
    // clearing a notification doesn't navigate the page the bell happens to be open on.
    document.addEventListener("submit", function (e) {
        var form = e.target.closest("[data-notif-panel] form");
        if (!form) return;
        e.preventDefault();

        fetch(form.action, { method: "POST", body: new FormData(form), redirect: "manual" })
            .catch(function () {
                // Best effort — the refresh below just shows whatever the server still has.
            })
            .then(function () {
                loadPanel();
                pollFeed();
            });
    });

    // --- Badge + toasts -------------------------------------------------------

    function toastRoot() {
        var el = document.getElementById("notif-toast-root");
        if (!el) {
            el = document.createElement("div");
            el.id = "notif-toast-root";
            document.body.appendChild(el);
        }
        return el;
    }

    function setBadge(count) {
        unreadCount = count;
        setTitleCount(count);

        var summary = bell()?.querySelector("summary");
        if (!summary) return;
        var badge = summary.querySelector("[data-notif-badge]");
        if (count > 0) {
            if (!badge) {
                badge = document.createElement("span");
                badge.dataset.notifBadge = "";
                badge.className = "absolute right-0.5 top-0.5 flex h-4 min-w-4 items-center justify-center " +
                    "rounded-full bg-red-600 px-1 text-[10px] font-semibold leading-none text-white";
                summary.appendChild(badge);
            }
            badge.textContent = count > 9 ? "9+" : String(count);
        } else if (badge) {
            badge.remove();
        }
    }

    // Tab-title prefix, e.g. "(2) Defects · Project · QA Tracker" — mirrors the badge, so an
    // unread count is visible even when the tab isn't focused. Strips any prefix already there
    // before re-adding one, so this is safe to call repeatedly with the same base title.
    function setTitleCount(count) {
        var base = document.title.replace(/^\(\d+\)\s*/, "");
        document.title = count > 0 ? "(" + count + ") " + base : base;
    }

    // Enhanced navigation swaps in the new page's own <PageTitle> — which knows nothing about
    // the unread count — so reapply the last known count once that settles.
    document.addEventListener("enhancedload", function () {
        setTitleCount(unreadCount);
    });

    function showToast(item) {
        var tpl = document.getElementById("notif-toast-template");
        if (!tpl) return;

        var toast = tpl.content.firstElementChild.cloneNode(true);
        toast.href = item.url;
        toast.querySelector("[data-toast-message]").textContent = item.message;
        var project = toast.querySelector("[data-toast-project]");
        if (item.projectName) {
            project.textContent = item.projectName;
            project.hidden = false;
        }

        toastRoot().appendChild(toast);
        setTimeout(function () {
            toast.style.opacity = "0";
            setTimeout(function () { toast.remove(); }, 300);
        }, TOAST_MS);
    }

    // Guarded against overlap (mirrors loadPanel()'s panelBusy) — the toggle handler, the
    // background timer, and visibilitychange's catch-up tick can all trigger this within a
    // few hundred ms of each other, and two overlapping calls racing to read-then-write the
    // shared seenIds baseline is exactly the kind of thing that could misfire the sound.
    async function pollFeed() {
        if (!bell() || feedBusy) return; // signed out, no bell, or a poll already in flight
        feedBusy = true;

        try {
            var res = await fetch("/notifications/feed", { headers: { Accept: "application/json" } });
            if (!res.ok) throw new Error(String(res.status));
            var data = await res.json();
            failStreak = 0;

            setBadge(data.count || 0);

            var items = data.items || [];
            if (seenIds !== null) {
                var fresh = items.filter(function (item) { return !seenIds.has(item.id); });
                fresh.forEach(showToast);
                if (fresh.length > 0 && bell().dataset.notifSoundEnabled === "true") {
                    playSound(bell().dataset.notifSound);
                }
            }
            seenIds = new Set(items.map(function (item) { return item.id; }));
        } catch (_) {
            // Network blip or a deploy rolling — back off, don't spam the console.
            failStreak = Math.min(failStreak + 1, 4); // 45s → 90 → 3m → 6m
        } finally {
            feedBusy = false;
        }
    }

    // --- Background poll loop ------------------------------------------------

    async function tick() {
        timer = null;
        await pollFeed();
        if (bell()?.open) loadPanel(); // keep an already-open dropdown live
        schedule();
    }

    function schedule() {
        if (timer) clearTimeout(timer);
        timer = null;
        if (!bell()) return; // signed out, or a page without the bell — nothing to poll for
        timer = setTimeout(tick, pollMs() * (failStreak + 1));
    }

    // Going hidden doesn't stop the loop (schedule() above just switches to the slower
    // backgrounded interval on its own next run) — only coming back into view forces an
    // immediate catch-up, replacing whatever backgrounded-interval timer was still pending.
    document.addEventListener("visibilitychange", function () {
        if (!document.hidden) {
            if (timer) clearTimeout(timer);
            timer = null;
            tick();
        }
    });

    // Seed the title prefix from the server-rendered count immediately — don't wait for the
    // first poll below, which is deliberately delayed. The badge itself is already correct
    // from the server render, so only the title needs touching here.
    unreadCount = Number(bell()?.dataset.notifCount) || 0;
    setTitleCount(unreadCount);

    // Start a little after load so the poll doesn't compete with first render.
    setTimeout(tick, 4000);
})();
