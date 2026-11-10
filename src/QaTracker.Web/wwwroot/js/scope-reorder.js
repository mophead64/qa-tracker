// Drag-to-reorder for [data-reorder-list] lists of [data-reorder-item] rows (the "Reorder
// scopes" modal on the test cases page). Rows are moved in the DOM as you drag; each row
// carries its own hidden input, so the surrounding form posts them in the new order.
//
// Pointer events, not HTML5 drag-and-drop, so it works with touch too: a mouse can drag a
// row from anywhere, a finger only from its grip ([data-reorder-handle], touch-action: none)
// so the list still scrolls normally. Each row's ↑/↓ buttons ([data-reorder-move]) do the
// same from the keyboard. Closing the dialog without saving restores the original order
// (rows carry data-original-index). Document-level listeners so this survives enhanced
// navigation, like dialog.js.
(function () {
    var drag = null;

    function items(list) {
        return Array.prototype.filter.call(list.children, function (el) {
            return el.hasAttribute("data-reorder-item");
        });
    }

    // Disable ↑ on the first row and ↓ on the last.
    function refresh(list) {
        var rows = items(list);
        rows.forEach(function (row, i) {
            var up = row.querySelector("[data-reorder-move='up']");
            var down = row.querySelector("[data-reorder-move='down']");
            if (up) up.disabled = i === 0;
            if (down) down.disabled = i === rows.length - 1;
        });
    }

    function refreshAll() {
        document.querySelectorAll("[data-reorder-list]").forEach(refresh);
    }

    document.addEventListener("pointerdown", function (e) {
        var row = e.target.closest("[data-reorder-item]");
        if (!row || e.button !== 0 || e.target.closest("button, a, input")) return;
        if (e.pointerType !== "mouse" && !e.target.closest("[data-reorder-handle]")) return;

        var list = row.closest("[data-reorder-list]");
        if (!list) return;

        e.preventDefault();
        // Capture on the list, not the row: the row is re-inserted as it moves, which would
        // drop a capture held by the row itself.
        list.setPointerCapture(e.pointerId);
        drag = { row: row, list: list, pointerId: e.pointerId, startY: e.clientY, moved: false };
    });

    document.addEventListener("pointermove", function (e) {
        if (!drag || e.pointerId !== drag.pointerId) return;
        if (!drag.moved) {
            if (Math.abs(e.clientY - drag.startY) < 4) return;
            drag.moved = true;
            drag.row.classList.add("reorder-dragging");
        }

        // Nudge the scrollable list when dragging near its top or bottom edge.
        var bounds = drag.list.getBoundingClientRect();
        if (e.clientY < bounds.top + 24) drag.list.scrollTop -= 8;
        else if (e.clientY > bounds.bottom - 24) drag.list.scrollTop += 8;

        // Drop before the first other row whose midpoint is below the pointer; else at the end.
        var before = null;
        var others = items(drag.list).filter(function (el) { return el !== drag.row; });
        for (var i = 0; i < others.length; i++) {
            var r = others[i].getBoundingClientRect();
            if (e.clientY < r.top + r.height / 2) {
                before = others[i];
                break;
            }
        }

        if (before) {
            if (drag.row.nextElementSibling !== before) drag.list.insertBefore(drag.row, before);
        } else if (drag.list.lastElementChild !== drag.row) {
            drag.list.appendChild(drag.row);
        }
    });

    function endDrag(e) {
        if (!drag || e.pointerId !== drag.pointerId) return;
        var finished = drag;
        drag = null;
        finished.row.classList.remove("reorder-dragging");
        if (finished.list.hasPointerCapture(e.pointerId)) {
            finished.list.releasePointerCapture(e.pointerId);
        }
        if (finished.moved) {
            refresh(finished.list);
            // Swallow the click that follows a drag so it can't land on the dialog's backdrop
            // handler (dialog.js) and close the modal mid-reorder.
            // The click (if any) arrives in the same task as pointerup, so disarm on the next
            // tick — otherwise a drag released off-page would eat the next real click (Save).
            var swallow = function (ev) {
                ev.stopPropagation();
                ev.preventDefault();
            };
            document.addEventListener("click", swallow, { capture: true, once: true });
            setTimeout(function () {
                document.removeEventListener("click", swallow, { capture: true });
            }, 0);
        }
    }

    document.addEventListener("pointerup", endDrag);
    document.addEventListener("pointercancel", endDrag);

    document.addEventListener("click", function (e) {
        var btn = e.target.closest("[data-reorder-move]");
        if (!btn) return;
        var row = btn.closest("[data-reorder-item]");
        var list = row && row.closest("[data-reorder-list]");
        if (!list) return;

        if (btn.getAttribute("data-reorder-move") === "up" && row.previousElementSibling) {
            list.insertBefore(row, row.previousElementSibling);
        } else if (btn.getAttribute("data-reorder-move") === "down" && row.nextElementSibling) {
            list.insertBefore(row.nextElementSibling, row);
        }
        refresh(list);
        // Keep focus on the same arrow so repeated presses keep moving the row; fall back to
        // its sibling when it just became disabled at an end.
        var keep = btn.disabled ? row.querySelector("[data-reorder-move]:not([disabled])") : btn;
        if (keep) keep.focus();
    });

    // A dialog closed without saving: put its rows back as they were. ("close" doesn't bubble.)
    document.addEventListener("close", function (e) {
        if (e.target.tagName !== "DIALOG") return;
        e.target.querySelectorAll("[data-reorder-list]").forEach(function (list) {
            items(list)
                .sort(function (a, b) {
                    return Number(a.dataset.originalIndex) - Number(b.dataset.originalIndex);
                })
                .forEach(function (row) { list.appendChild(row); });
            refresh(list);
        });
    }, true);

    document.addEventListener("DOMContentLoaded", refreshAll);
    document.addEventListener("enhancedload", refreshAll);
})();
