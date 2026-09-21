// Behaviour for <FormSelect>: picking an option inside a <details data-form-select> copies its
// value into the hidden input, updates the toggle's label and tick, and closes the menu.
// Document-level listener so it survives enhanced navigation, matching dropdown.js.

document.addEventListener("click", function (e) {
    var option = e.target.closest("[data-select-option]");
    if (!option) return;

    var root = option.closest("details[data-form-select]");
    if (!root) return;

    var input = root.querySelector("[data-select-input]");
    if (input) {
        input.value = option.getAttribute("data-select-option");
        input.dispatchEvent(new Event("change", { bubbles: true }));
    }

    var label = root.querySelector("[data-select-label]");
    if (label) label.textContent = option.getAttribute("data-select-text");

    root.querySelectorAll("[data-select-option]").forEach(function (o) {
        var check = o.querySelector("[data-select-check]");
        if (check) check.classList.toggle("hidden", o !== option);
    });

    root.open = false;
});
