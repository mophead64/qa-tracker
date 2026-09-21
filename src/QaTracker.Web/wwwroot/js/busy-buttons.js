// Busy state for primary (green) buttons. When a .btn-primary submit button is used to submit
// a form, or a .btn-primary link is followed, the control disables itself and shows a spinner
// laid over its (hidden but still present) label, so it keeps exactly the same size. The
// spinner itself is pure CSS — see [data-busy] in Styles/app.css.
//
// The state is cleared when the page is restored from the back/forward cache and after an
// enhanced navigation, so a button can never be left spinning. Document-level listeners so
// this survives enhanced navigation, matching dialog.js. The attachment upload form
// (data-upload-form) posts via fetch and has its own progress UI, so it's skipped.

(function () {
    function setBusy(el) {
        if (el.hasAttribute("data-busy")) return;
        el.setAttribute("data-busy", "");
        el.setAttribute("aria-busy", "true");
        if (el.tagName === "BUTTON") {
            // Disable after the current task: a submit button that's disabled while the submit
            // event is still being processed would drop its own name/value from the form data.
            setTimeout(function () {
                if (el.hasAttribute("data-busy") && !el.disabled) {
                    el.disabled = true;
                    el.setAttribute("data-busy-disabled", "");
                }
            }, 0);
        } else {
            el.setAttribute("aria-disabled", "true");
        }
    }

    function clearAll() {
        document.querySelectorAll("[data-busy]").forEach(function (el) {
            el.removeAttribute("data-busy");
            el.removeAttribute("aria-busy");
            el.removeAttribute("aria-disabled");
            if (el.hasAttribute("data-busy-disabled")) {
                el.removeAttribute("data-busy-disabled");
                el.disabled = false;
            }
        });
    }

    document.addEventListener("submit", function (e) {
        var form = e.target;
        if (!(form instanceof HTMLFormElement) || form.closest("[data-upload-form]")) return;

        var button = e.submitter || form.querySelector(".btn-primary[type=submit]");
        if (button && button.classList.contains("btn-primary") && !button.disabled) {
            setBusy(button);
        }
    });

    document.addEventListener("click", function (e) {
        var link = e.target.closest("a.btn-primary[href]");
        if (!link || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        if (link.target === "_blank" || link.hasAttribute("download") || link.hasAttribute("data-dialog-open")) return;

        var href = link.getAttribute("href");
        if (!href || href.charAt(0) === "#") return;

        setBusy(link);
    });

    window.addEventListener("pageshow", function (e) {
        if (e.persisted) clearAll();
    });
    document.addEventListener("enhancedload", clearAll);
})();
