// Progressive enhancement for the CSV import file picker (TestCaseImport.razor). The real
// <input type="file"> is visually hidden; a styled <label> opens it. Once a file is picked
// we show its name with a Remove button and enable the form's [data-file-submit] button
// (disabled until then). Document-level delegation so it survives enhanced navigation,
// matching dropdown.js / notification-poll.js.

(function () {
    function pickerOf(el) {
        return el.closest("[data-file-picker]");
    }

    function sync(picker) {
        var input = picker.querySelector("input[type=file]");
        if (!input) return;

        var hasFile = !!(input.files && input.files.length);
        var chosen = picker.querySelector("[data-file-chosen]");
        var nameEl = picker.querySelector("[data-file-name]");

        if (nameEl) nameEl.textContent = hasFile ? input.files[0].name : "";
        if (chosen) chosen.hidden = !hasFile;

        var form = input.closest("form");
        var submit = form && form.querySelector("[data-file-submit]");
        if (submit) submit.disabled = !hasFile;
    }

    function syncAll() {
        document.querySelectorAll("[data-file-picker]").forEach(sync);
    }

    document.addEventListener("change", function (e) {
        var picker = e.target.matches && e.target.matches("[data-file-picker] input[type=file]")
            ? pickerOf(e.target) : null;
        if (picker) sync(picker);
    });

    document.addEventListener("click", function (e) {
        var btn = e.target.closest("[data-file-remove]");
        if (!btn) return;
        var picker = pickerOf(btn);
        var input = picker && picker.querySelector("input[type=file]");
        if (input) {
            input.value = "";
            sync(picker);
        }
    });

    document.addEventListener("DOMContentLoaded", syncAll);
    document.addEventListener("enhancedload", syncAll);
    syncAll();
})();
