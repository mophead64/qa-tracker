// Live pages: in-place form posts and polling for other people's changes, on static SSR pages.
//
// A page opts in by marking its content <… data-live-page data-live-region id="…">. Then:
//
//  * Forms inside it post with fetch instead of navigating. The endpoints still answer with
//    their usual redirect back to the page, so the response *is* the updated page; its
//    [data-live-region] elements are swapped in for the current ones. One request, as before,
//    but the scroll position stays put and nothing else on the page is disturbed. A response
//    that lands on a different page (or fails) falls back to a normal navigation / submit.
//
//  * [data-live-keep] elements (with an id) are carried across a swap as they are — the comment
//    box keeps its half-typed text, chosen files and caret while a result changes or someone
//    else's comment arrives. Only a post from inside a kept element replaces it (so posting a
//    comment clears the box) — unless the new copy reports an error ([data-live-error]), when
//    the typed text is put back.
//
//  * With data-live-version-src + data-live-version (a stamp from LiveVersion.cs), the page
//    polls that endpoint every data-live-poll-seconds and re-fetches itself only when the stamp
//    has changed. Polling pauses while the tab is hidden, and a swap waits while someone is mid-
//    interaction: a menu or dialog open, typing outside a kept element, or something marked
//    [data-live-busy].
//
// Regions are matched by id, so the "On this page" rail (rendered outside the page body) can be
// a region too. After a swap, "enhancedload" is dispatched on document, which the other scripts
// already treat as "the page's DOM was replaced" (busy buttons reset, shortcut labels, …).

(function () {
    var inflight = 0;
    var refreshing = false;
    var timer = null;

    function root() {
        return document.querySelector("[data-live-page]");
    }

    function isTextField(el) {
        return el && (el.tagName === "TEXTAREA" || el.tagName === "SELECT" || el.isContentEditable
            || (el.tagName === "INPUT" && !/^(button|submit|reset|checkbox|radio|hidden|file)$/i.test(el.type)));
    }

    // True while swapping the page would get in someone's way.
    function interacting() {
        var page = root();
        if (!page || inflight > 0) return true;
        if (document.querySelector("dialog[open], [data-live-region] details[open], [data-live-busy]")) return true;
        var active = document.activeElement;
        return isTextField(active) && !!active.closest("[data-live-region]") && !active.closest("[data-live-keep]");
    }

    // ---- swapping --------------------------------------------------------------------------

    function fieldKey(el) {
        return el.name || el.id || "";
    }

    function swap(html, fromForm) {
        var doc = new DOMParser().parseFromString(html, "text/html");
        var active = document.activeElement;
        var caret = isTextField(active) && "selectionStart" in active
            ? [active.selectionStart, active.selectionEnd] : null;
        var swapped = false;

        document.querySelectorAll("[data-live-region][id]").forEach(function (region) {
            var fresh = doc.getElementById(region.id);
            if (!fresh || !fresh.hasAttribute("data-live-region")) return;

            region.querySelectorAll("[data-live-keep][id]").forEach(function (keep) {
                var slot = fresh.querySelector("#" + CSS.escape(keep.id));
                if (!slot) return;
                if (fromForm && keep.contains(fromForm)) {
                    // Posted from here: take the new copy, but don't lose what was typed if the
                    // server turned it down.
                    if (slot.querySelector("[data-live-error]")) {
                        keep.querySelectorAll("textarea, input[type=text]").forEach(function (old) {
                            var twin = slot.querySelector((old.tagName === "TEXTAREA" ? "textarea" : "input") + "[name='" + CSS.escape(fieldKey(old)) + "']");
                            if (twin) twin.value = old.value;
                        });
                    }
                    return;
                }
                slot.replaceWith(keep);
            });

            region.replaceWith(fresh);
            swapped = true;
        });

        if (!swapped) return false;

        // Moving a focused element blurs it; put the caret back where it was.
        if (active && active !== document.activeElement && document.contains(active)) {
            active.focus({ preventScroll: true });
            if (caret) {
                try { active.setSelectionRange(caret[0], caret[1]); } catch (_) { /* not a text input */ }
            }
        }

        document.dispatchEvent(new Event("enhancedload"));
        start();
        return true;
    }

    // ---- in-place form posts ----------------------------------------------------------------

    function nativeSubmit(form, submitter) {
        if (submitter && submitter.name) {
            var carry = document.createElement("input");
            carry.type = "hidden";
            carry.name = submitter.name;
            carry.value = submitter.value;
            form.appendChild(carry);
        }
        HTMLFormElement.prototype.submit.call(form);
    }

    // Capture phase: decide before busy-buttons.js and Blazor's own form handling see the event.
    document.addEventListener("submit", function (e) {
        var form = e.target;
        if (!(form instanceof HTMLFormElement) || e.defaultPrevented) return;
        if (!form.closest("[data-live-page]") || form.closest("[data-upload-form]") || form.hasAttribute("data-live-off")) return;
        if ((form.getAttribute("method") || "get").toLowerCase() !== "post") return;

        e.preventDefault();
        var submitter = e.submitter;
        var body = new FormData(form);
        if (submitter && submitter.name) body.append(submitter.name, submitter.value);

        inflight++;
        fetch(form.action, { method: "POST", body: body, credentials: "same-origin", headers: { "X-Live-Request": "1" } })
            .then(function (res) {
                if (!res.ok) throw new Error(String(res.status));
                var landed = new URL(res.url);
                if (landed.pathname !== location.pathname) {
                    location.assign(res.url); // the action moved us on (e.g. somewhere else entirely)
                    return;
                }
                return res.text().then(function (html) {
                    // A dialog the form lived in closes with the swap; close it first so the
                    // page isn't left inert.
                    var dialog = form.closest("dialog");
                    if (dialog && dialog.open) dialog.close();
                    if (!swap(html, form)) {
                        location.assign(res.url);
                        return;
                    }
                    history.replaceState(history.state, "", landed.pathname + landed.search + location.hash);
                });
            })
            .catch(function () {
                nativeSubmit(form, submitter);
            })
            .finally(function () {
                inflight--;
            });
    }, true);

    // ---- polling ----------------------------------------------------------------------------

    function pollMs() {
        var page = root();
        return (Number(page && page.dataset.livePollSeconds) || 10) * 1000;
    }

    function start() {
        clearTimeout(timer);
        var page = root();
        if (!page || !page.dataset.liveVersionSrc) return;
        timer = setTimeout(poll, pollMs());
    }

    function refresh(force) {
        if (refreshing || (!force && interacting())) return Promise.resolve(false);
        refreshing = true;
        return fetch(location.href, { headers: { Accept: "text/html" }, cache: "no-store", credentials: "same-origin" })
            .then(function (res) {
                return res.ok && new URL(res.url).pathname === location.pathname ? res.text() : null;
            })
            .then(function (html) {
                // Someone may have opened a menu or started typing while that was in flight.
                return !!html && (force || !interacting()) && swap(html, null);
            })
            .catch(function () { return false; })
            .finally(function () { refreshing = false; });
    }

    function poll() {
        var page = root();
        if (!page || !page.dataset.liveVersionSrc) return;
        if (document.hidden || interacting()) {
            start();
            return;
        }

        fetch(page.dataset.liveVersionSrc, { headers: { Accept: "application/json" }, cache: "no-store", credentials: "same-origin" })
            .then(function (res) {
                if (res.status === 404) {
                    location.reload(); // the item's gone — show the page's "not found"
                    return null;
                }
                return res.ok ? res.json() : null;
            })
            .then(function (data) {
                var current = root();
                if (data && current && data.version !== current.dataset.liveVersion) {
                    return refresh(false);
                }
            })
            .catch(function () { /* signed out, offline… try again next time */ })
            .finally(start);
    }

    document.addEventListener("visibilitychange", function () {
        if (!document.hidden && root()) {
            clearTimeout(timer);
            poll();
        }
    });

    document.addEventListener("DOMContentLoaded", start);
    if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
        window.Blazor.addEventListener("enhancedload", start);
    }
    if (document.readyState !== "loading") start();

    // For other scripts (attachment-upload.js): bring a live page up to date in place.
    window.qatLive = {
        isLive: function () { return !!root(); },
        refresh: function () { return refresh(true); },
    };
})();
