// Anti-autofill for sensitive fields. Inputs marked data-no-autofill are rendered readonly
// (browsers and password managers skip readonly fields when autofilling) and only become
// editable once the user actually focuses them. Document-level listener so it survives
// enhanced navigation, matching dialog.js.

document.addEventListener("focusin", function (e) {
    var field = e.target;
    if (field instanceof HTMLInputElement && field.hasAttribute("data-no-autofill") && field.readOnly) {
        field.readOnly = false;
    }
});
