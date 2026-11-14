// Project activity, loaded as it's needed (see ProjectActivityFeed.razor):
//
// - An element with data-lazy-load="<url>" fetches its contents (an HTML fragment) once it
//   scrolls near the viewport — the dashboard's Recent activity card, so the dashboard itself
//   never waits on the history queries. After a live-page.js swap the element arrives holding the
//   entries it already showed ([data-lazy-refresh]) and quietly re-fetches them at once.
// - A link with data-activity-more="<fragment url>" ("Load more" on the activity page) fetches the
//   next page and appends its entries to the timeline above, replacing itself with the next
//   page's link (if any). Without JS the link's href opens that older page instead.
//
// Document-level click listener (capture phase, like page-contents.js); lazy elements are
// re-scanned after Blazor enhanced navigation.

(function () {
    function fetchFragment(url) {
        return fetch(url, { headers: { Accept: "text/html" } }).then(function (r) {
            if (!r.ok) throw new Error(r.status);
            return r.text();
        });
    }

    function load(el) {
        if (el.dataset.lazyLoaded) return;
        el.dataset.lazyLoaded = "true";
        el.setAttribute("aria-busy", "true");
        fetchFragment(el.dataset.lazyLoad)
            .then(function (html) { el.innerHTML = html; })
            .catch(function () {
                // A live-page refresh already shows the previous entries; keep them.
                if (el.hasAttribute("data-lazy-refresh")) return;
                el.innerHTML = '<p class="text-sm text-red-600 dark:text-red-400">Couldn\'t load this. Refresh the page to try again.</p>';
            })
            .finally(function () { el.removeAttribute("aria-busy"); });
    }

    var observer = "IntersectionObserver" in window
        ? new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    observer.unobserve(entry.target);
                    load(entry.target);
                }
            });
        }, { rootMargin: "200px" })
        : null;

    function scan() {
        document.querySelectorAll("[data-lazy-load]:not([data-lazy-loaded])").forEach(function (el) {
            // A live-page refresh is already showing (now stale) entries: re-fetch straight away,
            // even off-screen, rather than leaving them stale until it's scrolled to.
            if (el.hasAttribute("data-lazy-refresh")) load(el);
            else if (observer) observer.observe(el);
            else load(el);
        });
    }

    document.addEventListener("click", function (e) {
        var more = e.target.closest("a[data-activity-more]");
        if (!more || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        var feed = more.closest("[data-activity-feed]");
        var list = feed && feed.querySelector("ol[data-history]");
        if (!list) return;

        // preventDefault (in the capture phase) stops Blazor enhanced navigation following the link.
        e.preventDefault();
        if (more.getAttribute("aria-busy") === "true") return;
        more.setAttribute("aria-busy", "true");
        more.textContent = "Loading…";

        var row = more.closest("[data-activity-more-row]") || more;
        fetchFragment(more.dataset.activityMore)
            .then(function (html) {
                var t = document.createElement("template");
                t.innerHTML = html;
                t.content.querySelectorAll("ol[data-history] > li").forEach(function (li) {
                    list.appendChild(li);
                });
                var next = t.content.querySelector("[data-activity-more-row]");
                if (next) row.replaceWith(next);
                else row.remove();
            })
            .catch(function () {
                more.removeAttribute("aria-busy");
                more.textContent = "Couldn't load — try again";
            });
    }, true); // capture phase: run before Blazor's enhanced-navigation click handler, which skips prevented clicks

    document.addEventListener("DOMContentLoaded", scan);
    document.addEventListener("enhancedload", scan);
    // Blazor raises enhancedload on its own event bus; the dashboard card has to load after an
    // enhanced navigation lands on the dashboard, not just on a full page load.
    if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
        window.Blazor.addEventListener("enhancedload", scan);
    }
    if (document.readyState !== "loading") scan();
})();
