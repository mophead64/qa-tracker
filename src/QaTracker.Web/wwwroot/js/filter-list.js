// Client-side filtering of a short list, e.g. the defects in the test case page's "Link or create
// defect" modal. Inside a [data-filter-list] container:
//
//   - a <FormSelect> (its hidden [data-select-input]) holds the chosen filter value;
//   - each row carries data-filter-tags="All Open NotFixed" — every value it matches, worked out
//     on the server — and is hidden unless the chosen value is one of them;
//   - [data-filter-empty] shows when no row is left.
//
// Rows are only hidden, never removed, so ticked checkboxes keep their state (and still submit)
// while the filter changes. Document-level listener, so it survives page swaps (live-page.js) and
// enhanced navigation.

(function () {
    function apply(list) {
        var input = list.querySelector("[data-select-input]");
        if (!input) return;
        var value = input.value;
        var shown = 0;
        list.querySelectorAll("[data-filter-tags]").forEach(function (row) {
            var match = (" " + row.getAttribute("data-filter-tags") + " ").indexOf(" " + value + " ") >= 0;
            row.hidden = !match;
            if (match) shown++;
        });
        var empty = list.querySelector("[data-filter-empty]");
        if (empty) empty.hidden = shown > 0;
        var scroller = list.querySelector("[data-filter-scroller]");
        if (scroller) scroller.scrollTop = 0;
    }

    document.addEventListener("change", function (e) {
        var input = e.target.closest("[data-select-input]");
        var list = input && input.closest("[data-filter-list]");
        if (list) apply(list);
    });
})();
