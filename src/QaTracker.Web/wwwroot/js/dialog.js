// Generic <dialog> opener for static-SSR pages. A button with
// data-dialog-open="<id>" opens that dialog modally; data-dialog-close (anywhere inside a
// dialog) closes it, as does a click on the backdrop. Document-level listeners so this
// survives enhanced navigation, matching notification-bell.js.

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
