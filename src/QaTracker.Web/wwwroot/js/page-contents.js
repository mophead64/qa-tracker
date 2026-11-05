// "On this page" contents rail (PageContents.razor): smooth-scrolls to a section instead of
// the instant jump the plain href gives, and highlights the section currently in view.
// Document-level listeners so this survives enhanced navigation, matching dropdown.js /
// copy-link.js.
//
// The click handler runs in the capture phase and calls preventDefault(): Blazor's own
// enhanced-navigation click handler skips defaultPrevented clicks, so it never sees these.

(function () {
    // How far below the top of the scroll area a section's heading counts as "the one being
    // read" — roughly the height of a card heading plus some breathing room.
    var ACTIVE_OFFSET = 96;

    function nav() {
        return document.querySelector("[data-page-contents]");
    }

    // The page scrolls inside <main> (MainLayout), not the window.
    function scroller() {
        return document.querySelector("main");
    }

    function behavior() {
        return window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth";
    }

    function links() {
        var el = nav();
        return el ? Array.prototype.slice.call(el.querySelectorAll("a[data-jump]:not([data-jump=''])")) : [];
    }

    function setActive(id) {
        links().forEach(function (link) {
            var on = link.dataset.jump === id;
            link.classList.toggle("page-contents-active", on);
            if (on) link.setAttribute("aria-current", "location");
            else link.removeAttribute("aria-current");
        });
    }

    // The last section whose top has scrolled past the offset line — the first section while
    // none has yet (still up in the page header), and the last section outright once the page
    // is scrolled to the bottom, since a short final section (often Comments) may never reach
    // the line.
    function currentId() {
        var main = scroller();
        var all = links();
        if (!main || all.length === 0) return null;

        if (main.scrollTop + main.clientHeight >= main.scrollHeight - 4) {
            return all[all.length - 1].dataset.jump;
        }

        var line = main.getBoundingClientRect().top + ACTIVE_OFFSET;
        var id = all[0].dataset.jump;
        all.forEach(function (link) {
            var target = document.getElementById(link.dataset.jump);
            if (target && target.getBoundingClientRect().top <= line) id = link.dataset.jump;
        });
        return id;
    }

    var frame = 0;
    function update() {
        if (frame) return;
        frame = window.requestAnimationFrame(function () {
            frame = 0;
            if (nav()) setActive(currentId());
        });
    }

    document.addEventListener("click", function (e) {
        var link = e.target.closest("[data-page-contents] a[data-jump]");
        if (!link || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;

        var id = link.dataset.jump;
        var target = id ? document.getElementById(id) : null;
        if (id && !target) return; // section not on the page — let the href do its thing

        e.preventDefault();
        if (target) {
            target.scrollIntoView({ behavior: behavior(), block: "start" });
        } else {
            scroller()?.scrollTo({ top: 0, behavior: behavior() });
        }
        // Keep the address bar shareable (and the Back button where it was) without
        // triggering another navigation. Built from the full path: a bare "#id" would
        // resolve against <base href="/">.
        history.replaceState(history.state, "", location.pathname + location.search + (id ? "#" + id : ""));
        if (id) setActive(id);
    }, true);

    // `scroll` doesn't bubble — listen in the capture phase to hear <main>'s.
    document.addEventListener("scroll", update, true);
    window.addEventListener("resize", update);
    document.addEventListener("enhancedload", update);
    update();
})();
