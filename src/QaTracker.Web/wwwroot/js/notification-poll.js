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

    function playSound(key) {
        if (!key) return;
        try {
            new Audio("/sounds/" + key + ".mp3").play().catch(function () {
                // Blocked by the browser's autoplay policy (no user gesture yet) — fine,
                // the badge/toast still landed.
            });
        } catch (_) {
            // Audio unsupported — nothing useful to do.
        }
    }

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

    // The `toggle` event doesn't bubble — listen in the capture phase. Opening the bell
    // pulls a fresh list *and* a fresh count/toast check.
    document.addEventListener("toggle", function (e) {
        if (e.target?.id === "notification-bell" && e.target.open) {
            loadPanel();
            pollFeed();
        }
    }, true);

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

    async function pollFeed() {
        if (!bell()) return; // signed out, or a page without the bell — nothing to do

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

    // Start a little after load so the poll doesn't compete with first render.
    setTimeout(tick, 4000);
})();
