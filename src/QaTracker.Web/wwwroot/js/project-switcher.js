// The top-bar project switcher (TopBar.razor, [data-project-switcher] inside a <details data-dropdown>):
//
//  * Opening it focuses the "Find a project" box (when there is one) and scrolls the current project
//    into view.
//  * Typing filters the rows by name, brief and tags (each row's data-search; every word typed must
//    match somewhere), hiding status groups left empty and showing [data-switcher-empty] when
//    nothing matches.
//  * ↓ from the box moves into the matches, ↑/↓ move between them (↑ from the first goes back to the
//    box), Enter in the box opens the first match. Escape closes it (dropdown.js).
//
// Document-level listeners, so this survives enhanced navigation and live-page swaps (the switcher is
// a live region and gets re-rendered).

(function () {
    function panelOf(el) {
        return el && el.closest("[data-project-switcher]");
    }

    function visibleButtons(panel) {
        return Array.prototype.filter.call(
            panel.querySelectorAll("[data-switcher-item]"),
            function (item) { return !item.hidden; }
        ).map(function (item) { return item.querySelector("button"); });
    }

    function apply(panel, text) {
        var words = text.toLowerCase().split(/\s+/).filter(Boolean);
        var shown = 0;
        panel.querySelectorAll("[data-switcher-group]").forEach(function (group) {
            var inGroup = 0;
            group.querySelectorAll("[data-switcher-item]").forEach(function (item) {
                var haystack = item.getAttribute("data-search") || "";
                var match = words.every(function (w) { return haystack.indexOf(w) !== -1; });
                item.hidden = !match;
                if (match) inGroup++;
            });
            group.hidden = inGroup === 0;
            shown += inGroup;
        });
        var empty = panel.querySelector("[data-switcher-empty]");
        if (empty) empty.hidden = shown > 0;
        var list = panel.querySelector("[data-switcher-list]");
        if (list) list.scrollTop = 0;
    }

    // <details> fires "toggle" (not bubbling) — listen in the capture phase.
    document.addEventListener("toggle", function (e) {
        var details = e.target;
        if (!(details instanceof HTMLDetailsElement) || !details.open) return;
        var panel = details.querySelector("[data-project-switcher]");
        if (!panel) return;

        var box = panel.querySelector("[data-switcher-filter]");
        if (box) {
            box.focus({ preventScroll: true });
            box.select();
        }
        if (!box || !box.value) {
            var current = panel.querySelector("[data-current]");
            if (current) current.scrollIntoView({ block: "nearest" });
        }
    }, true);

    document.addEventListener("input", function (e) {
        var box = e.target.closest && e.target.closest("[data-switcher-filter]");
        if (box) apply(panelOf(box), box.value);
    });

    document.addEventListener("keydown", function (e) {
        var panel = panelOf(e.target);
        if (!panel || e.isComposing) return;
        var box = panel.querySelector("[data-switcher-filter]");
        var buttons = visibleButtons(panel);

        if (e.target === box) {
            if (e.key === "ArrowDown" && buttons.length > 0) {
                e.preventDefault();
                buttons[0].focus();
            } else if (e.key === "Enter") {
                e.preventDefault(); // never submit a search box
                if (buttons.length > 0) buttons[0].click();
            }
            return;
        }

        var at = buttons.indexOf(e.target.closest("button"));
        if (at === -1) return;
        if (e.key === "ArrowDown" && at < buttons.length - 1) {
            e.preventDefault();
            buttons[at + 1].focus();
        } else if (e.key === "ArrowUp") {
            e.preventDefault();
            if (at > 0) buttons[at - 1].focus();
            else if (box) box.focus();
        }
    });
})();
