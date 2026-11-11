// Desktop side-nav collapse (the top bar's burger, [data-nav-collapse-toggle]). Toggles
// .nav-collapsed on the layout root ([data-nav-root]) and remembers it in the qat-nav cookie, which
// MainLayout reads so the server renders the same state — no flash, and enhanced navigation (which
// re-renders the layout from the server) keeps it. On small screens the nav is the slide-in overlay
// (#sidebar-toggle) instead and this button is hidden.
(function () {
    function root() {
        return document.querySelector("[data-nav-root]");
    }

    function sync() {
        var r = root();
        var collapsed = !!r && r.classList.contains("nav-collapsed");
        document.querySelectorAll("[data-nav-collapse-toggle]").forEach(function (b) {
            b.setAttribute("aria-expanded", collapsed ? "false" : "true");
            b.setAttribute("aria-label", collapsed ? "Expand navigation" : "Collapse navigation");
        });
    }

    document.addEventListener("click", function (e) {
        if (!e.target.closest("[data-nav-collapse-toggle]")) return;
        var r = root();
        if (!r) return;
        var collapsed = r.classList.toggle("nav-collapsed");
        document.cookie = "qat-nav=" + (collapsed ? "collapsed" : "expanded") + "; path=/; max-age=31536000; samesite=lax";
        sync();
    });

    document.addEventListener("DOMContentLoaded", sync);
    document.addEventListener("enhancedload", sync);
    sync();
})();
