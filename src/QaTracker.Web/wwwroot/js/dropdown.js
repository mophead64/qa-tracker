// Outside-click / Escape dismissal for the app's dropdown menus (the project switcher and
// user menu in the top bar, the notification bell, the status/assignee pickers, the
// per-file delete confirmation). A native <details> only toggles from its own <summary>,
// so any <details data-dropdown> is closed here when a click lands outside it, when Escape
// is pressed, or when something inside it carries data-dropdown-close (e.g. a Cancel
// button). Document-level listeners so this survives enhanced navigation, matching
// notification-bell.js. Modal <dialog>s already dismiss on a backdrop click via dialog.js.

(function () {
    function closeAll(except) {
        document.querySelectorAll("details[data-dropdown][open]").forEach(function (d) {
            if (d !== except) d.open = false;
        });
    }

    document.addEventListener("click", function (e) {
        if (e.target.closest("[data-dropdown-close]")) {
            closeAll(null);
            return;
        }
        // A summary click toggles its <details> *after* this bubble-phase listener runs,
        // so `open` here still reflects the pre-click state — that keeps the menu being
        // opened out of the sweep while every other open menu closes.
        closeAll(e.target.closest("details[data-dropdown]"));
    });

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") closeAll(null);
    });
})();
