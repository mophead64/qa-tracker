// Small helpers for groups of collapsible sections, e.g. the defect page's "Link test cases" picker:
//
//  * [data-details-expand] / [data-details-collapse] open or close every <details> inside the
//    nearest [data-details-group].
//  * [data-check-all] (inside a [data-check-group]) ticks every checkbox in that group, or clears
//    them all when they're already ticked; its label follows ("Select all" / "Clear"). Placed in a
//    <summary>, it doesn't open or close the section.
//
// Document-level listeners, so they survive enhanced navigation and live-page swaps.

(function () {
    function boxes(group) {
        return Array.prototype.slice.call(group.querySelectorAll("input[type=checkbox]"));
    }

    function relabel(group) {
        var button = group.querySelector("[data-check-all]");
        if (!button) return;
        var all = boxes(group);
        button.textContent = all.length > 0 && all.every(function (b) { return b.checked; }) ? "Clear" : "Select all";
    }

    document.addEventListener("click", function (e) {
        var toggle = e.target.closest("[data-details-expand], [data-details-collapse]");
        if (toggle) {
            var open = toggle.hasAttribute("data-details-expand");
            var scope = toggle.closest("[data-details-group]") || document;
            scope.querySelectorAll("details").forEach(function (d) { d.open = open; });
            return;
        }

        var checkAll = e.target.closest("[data-check-all]");
        if (checkAll) {
            e.preventDefault(); // inside a <summary>: don't toggle the section
            var group = checkAll.closest("[data-check-group]");
            if (!group) return;
            var all = boxes(group);
            var tick = !all.every(function (b) { return b.checked; });
            all.forEach(function (b) { b.checked = tick; });
            if (tick && group instanceof HTMLDetailsElement) group.open = true;
            relabel(group);
        }
    });

    document.addEventListener("change", function (e) {
        if (!(e.target instanceof HTMLInputElement) || e.target.type !== "checkbox") return;
        var group = e.target.closest("[data-check-group]");
        if (group) relabel(group);
    });
})();
