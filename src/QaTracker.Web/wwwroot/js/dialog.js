// Generic <dialog> opener for static-SSR pages (plus lazy bodies — see loadLazyContent). A button with
// data-dialog-open="<id>" opens that dialog modally; data-dialog-close (anywhere inside a
// dialog) closes it, as does a click on the backdrop. Document-level listeners so this
// survives enhanced navigation, matching notification-poll.js.
//
// A <dialog data-dialog-autoopen> is opened modally as soon as it renders — used when a
// form inside the dialog posts, fails validation server-side, and the page re-renders with
// the dialog markup still present (so the operator sees the error without reopening it).

document.addEventListener("click", function (e) {
    var opener = e.target.closest("[data-dialog-open]");
    if (opener && !opener.disabled) {
        var dialog = document.getElementById(opener.getAttribute("data-dialog-open"));
        if (dialog && !dialog.open && typeof dialog.showModal === "function") {
            dialog.showModal();
            loadLazyContent(dialog);
        }
        return;
    }

    var closer = e.target.closest("[data-dialog-close]");
    if (closer) {
        var d = closer.closest("dialog");
        if (d?.open) d.close();
        return;
    }

    // Click outside the dialog's content (i.e. on the ::backdrop) closes it.
    var openDialog = e.target.closest("dialog");
    if (e.target.tagName === "DIALOG" && openDialog?.open) {
        openDialog.close();
    }
});

// A dialog with data-lazy-src fetches its body (an HTML fragment) into its [data-lazy-content]
// element every time it opens — e.g. the History modals, which would otherwise put a long hidden
// timeline into every page. Relative URLs resolve against <base href>, like links.
function loadLazyContent(dialog) {
    var src = dialog.dataset.lazySrc;
    var target = dialog.querySelector("[data-lazy-content]");
    if (!src || !target) return;
    target.setAttribute("aria-busy", "true");
    fetch(src, { headers: { Accept: "text/html" } })
        .then(function (r) {
            if (!r.ok) throw new Error(r.status);
            return r.text();
        })
        .then(function (html) { target.innerHTML = html; })
        .catch(function () {
            target.innerHTML = '<p class="text-sm text-red-600 dark:text-red-400">Couldn\'t load this. Close and try again.</p>';
        })
        .finally(function () { target.removeAttribute("aria-busy"); });
}

function autoOpenDialogs() {
    document.querySelectorAll("dialog[data-dialog-autoopen]").forEach(function (d) {
        // Minimised attribute ("") or "true" means open; an explicit "false" does not.
        if (d.dataset.dialogAutoopen === "false") {
            return;
        }
        if (!d.open && typeof d.showModal === "function") {
            d.showModal();
        }
    });
}

document.addEventListener("DOMContentLoaded", autoOpenDialogs);
// Blazor enhanced navigation patches the DOM without a full load.
document.addEventListener("enhancedload", autoOpenDialogs);
