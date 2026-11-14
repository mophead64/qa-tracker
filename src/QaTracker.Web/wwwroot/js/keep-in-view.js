// Keeps pop-out panels on screen on narrow viewports. A dropdown panel (the absolutely positioned
// child of a <details data-dropdown>) or a link-group tooltip (.link-group-tip) is anchored to one
// edge of its trigger; near the other side of a phone screen that runs it past the edge, where
// the page clips it. When one opens, it's shifted back (by a transform) to sit 8px inside the
// viewport, and the shift is cleared when it closes. Document-level, so it survives enhanced
// navigation and live-page swaps.

(function () {
    var MARGIN = 8;

    function fit(panel) {
        if (!panel) return;
        panel.style.transform = "";
        var rect = panel.getBoundingClientRect();
        var vw = document.documentElement.clientWidth;
        var shift = 0;
        if (rect.right > vw - MARGIN) shift = vw - MARGIN - rect.right;
        if (rect.left + shift < MARGIN) shift = MARGIN - rect.left;
        if (shift !== 0) panel.style.transform = "translateX(" + Math.round(shift) + "px)";
    }

    function panelOf(details) {
        for (var i = 0; i < details.children.length; i++) {
            var child = details.children[i];
            if (child.tagName !== "SUMMARY" && getComputedStyle(child).position === "absolute") return child;
        }
        return null;
    }

    // toggle doesn't bubble: listen in the capture phase.
    document.addEventListener("toggle", function (e) {
        var details = e.target;
        if (!(details instanceof HTMLDetailsElement) || !details.hasAttribute("data-dropdown")) return;
        var panel = panelOf(details);
        if (!panel) return;
        if (details.open) fit(panel);
        else panel.style.transform = "";
    }, true);

    function fitTipsIn(target) {
        var group = target.closest && target.closest(".group");
        if (!group) return;
        group.querySelectorAll(".link-group-tip").forEach(fit);
    }

    document.addEventListener("pointerover", function (e) { fitTipsIn(e.target); });
    document.addEventListener("focusin", function (e) { fitTipsIn(e.target); });
})();
