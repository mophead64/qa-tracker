// Generic <dialog> opener for static-SSR pages. A button with
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
