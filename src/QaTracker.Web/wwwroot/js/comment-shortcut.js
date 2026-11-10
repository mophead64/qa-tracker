// ⌘+Enter (Mac) / Ctrl+Enter (elsewhere) in a comment box posts it — CommentComposer's
// [data-comment-form]. Skipped when another handler already took the key (the @-mention picker
// uses Enter to pick a name). requestSubmit() runs the browser's validation (the box is
// required) and the normal submit handlers (comment-file.js, busy-buttons.js) like a click.
// Also relabels the button's hint "Ctrl ↵" → "⌘↵" on a Mac. Document-level listeners so this
// survives enhanced navigation.
(function () {
    var isMac = /mac|iphone|ipad/i.test(
        (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || "");

    document.addEventListener("keydown", function (e) {
        if (e.key !== "Enter" || !(e.metaKey || e.ctrlKey) || e.defaultPrevented || e.isComposing) return;
        var form = e.target.closest && e.target.closest("[data-comment-form]");
        if (!form || !e.target.matches("textarea")) return;

        e.preventDefault();
        var submit = form.querySelector("[data-comment-submit]");
        if (submit && submit.disabled) return;
        form.requestSubmit(submit || undefined);
    });

    function relabel() {
        if (!isMac) return;
        document.querySelectorAll("[data-comment-submit]").forEach(function (button) {
            var hint = button.querySelector("[data-shortcut-hint]");
            if (hint) hint.textContent = "⌘↵";
            button.title = "Add comment (⌘+Enter)";
        });
    }

    document.addEventListener("DOMContentLoaded", relabel);
    document.addEventListener("enhancedload", relabel);
    relabel();
})();
