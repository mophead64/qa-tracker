// Behaviour for <FormActions> (the project / scope / test case / defect form footer) and
// <ConfirmDeleteDialog>. Document-level listeners so it survives enhanced navigation.
//
// - Save is disabled until a field changes ([data-require-changes]): every input / change compares
//   the fields with what the server rendered (FormSelect fires change too, and carries
//   data-initial-value). [data-start-dirty] (a post-back re-render) counts as changed from the start.
// - Unsaved changes: leaving the page (reload / close) asks via beforeunload; clicking any link —
//   Cancel included, and in-app links that enhanced navigation would otherwise follow silently —
//   asks with confirm(). Submitting any form on the page clears the warning.
// - ⌘/Ctrl+Enter inside the form saves; Esc cancels (not while a dialog or dropdown is open).
// - ConfirmDeleteDialog's typed-name box keeps its Delete button disabled until it matches.
(function () {
    var submitting = false;

    function actionsForms() {
        return Array.prototype.map.call(document.querySelectorAll("[data-form-actions]"), function (a) {
            return a.closest("form");
        }).filter(Boolean);
    }

    // Whether a field differs from what the server rendered — its default value / checked state,
    // not a snapshot taken when this script ran (a fast edit could land before that). Hidden inputs
    // (FormSelect & co.) only count when they carry data-initial-value, as JS sets them.
    function fieldChanged(el) {
        if (!el.name || el.disabled || el.name === "__RequestVerificationToken" || el.name === "_handler") return false;
        switch (el.type) {
            case "hidden":
                return el.hasAttribute("data-initial-value") && el.value !== el.getAttribute("data-initial-value");
            case "checkbox":
            case "radio":
                return el.checked !== el.defaultChecked;
            case "file":
                return !!(el.files && el.files.length);
            case "submit":
            case "button":
            case "reset":
                return false;
        }
        if (el.tagName === "SELECT") {
            return Array.prototype.some.call(el.options, function (o) { return o.selected !== o.defaultSelected; });
        }
        return el.value !== el.defaultValue;
    }

    function isDirty(form) {
        var actions = form.querySelector("[data-form-actions]");
        if (!actions) return false;
        if (actions.hasAttribute("data-start-dirty")) return true;
        return Array.prototype.some.call(form.elements, fieldChanged);
    }

    function sync(form) {
        var actions = form.querySelector("[data-form-actions]");
        if (!actions || !actions.hasAttribute("data-require-changes")) return;
        var save = actions.querySelector("[data-form-save]");
        if (save) save.disabled = !isDirty(form);
    }

    function anyDirty() {
        return !submitting && actionsForms().some(isDirty);
    }

    function syncTypedNames() {
        document.querySelectorAll("[data-confirm-name]").forEach(function (input) {
            var button = input.form && input.form.querySelector("[data-confirm-delete]");
            if (button) button.disabled = input.value.trim() !== input.dataset.confirmName;
        });
    }

    function init() {
        submitting = false;
        actionsForms().forEach(sync);
        syncTypedNames();
    }

    function onEdit(e) {
        var t = e.target;
        if (t.matches && t.matches("[data-confirm-name]")) {
            syncTypedNames();
            return;
        }
        var form = t.form || (t.closest && t.closest("form"));
        if (form && form.querySelector("[data-form-actions]")) sync(form);
    }

    document.addEventListener("input", onEdit);
    document.addEventListener("change", onEdit);

    // Any deliberate submit on the page — saving, a delete confirmation, the project form's
    // add/remove-link round trips — is an intended leave, not an accident to warn about.
    document.addEventListener("submit", function () {
        if (actionsForms().length > 0) submitting = true;
    }, true);

    window.addEventListener("beforeunload", function (e) {
        if (anyDirty()) {
            e.preventDefault();
            e.returnValue = "";
        }
    });

    // Capture phase, so it runs before Blazor's enhanced-navigation click handler.
    document.addEventListener("click", function (e) {
        var link = e.target.closest && e.target.closest("a[href]");
        if (!link || link.hasAttribute("download") || link.target === "_blank" || e.defaultPrevented) return;
        if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return; // new tab/window
        if (!anyDirty()) return;
        if (window.confirm("You have unsaved changes. Leave without saving them?")) {
            submitting = true; // leaving on purpose: don't ask again on unload
        } else {
            e.preventDefault();
            e.stopImmediatePropagation();
        }
    }, true);

    document.addEventListener("keydown", function (e) {
        if (e.defaultPrevented || e.isComposing) return;
        var form = e.target.closest && e.target.closest("form");
        var actions = form && form.querySelector("[data-form-actions]");

        if (e.key === "Enter" && (e.metaKey || e.ctrlKey) && actions) {
            e.preventDefault();
            var save = actions.querySelector("[data-form-save]");
            if (save && !save.disabled) form.requestSubmit(save);
            return;
        }

        if (e.key === "Escape") {
            if (document.querySelector("dialog[open], details[data-dropdown][open], [data-mention-list]:not([hidden])")) return;
            var target = actions || (actionsForms().length === 1 ? actionsForms()[0].querySelector("[data-form-actions]") : null);
            var cancel = target && target.querySelector("[data-form-cancel]");
            if (cancel) {
                e.preventDefault();
                cancel.click(); // goes through the unsaved-changes check above
            }
        }
    }, true); // capture: see whether a dropdown / dialog is open *before* their own Esc handlers close it

    document.addEventListener("DOMContentLoaded", init);
    document.addEventListener("enhancedload", init);
    init();
})();
