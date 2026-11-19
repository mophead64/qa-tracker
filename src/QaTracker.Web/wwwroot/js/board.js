// Drag and drop on the development board: cards between and within the columns of the board
// page, and tasks up and down a story's or bug's checklist.
//
//  * [data-drag-list] holds [data-drag-item] rows. Lists sharing a data-drag-group (the board's
//    four columns) accept each other's items; a list's data-drag-field / data-drag-value (e.g.
//    column=Testing) are posted along with the item's new index.
//  * When a row lands somewhere new it's POSTed (fetch, X-Requested-With: fetch) to its
//    data-drag-url with that index, and the live page refreshes in place (live-page.js) so
//    counts and the stamp catch up. A failed post reloads the page, putting the row back.
//  * While dragging, a lifted copy of the card follows the pointer, the card's own spot becomes a
//    dashed outline where it will land, and the column under the pointer is highlighted.
//  * Pointer events, not HTML5 drag-and-drop, so touch works too: a mouse drags a row from
//    anywhere (a click that ends a drag is swallowed, so the title link doesn't open), a finger
//    only from its grip ([data-drag-handle], touch-action: none) so the page still scrolls.
//  * While dragging, the list's live region is marked data-live-busy so a poll can't swap it away.
//  * A list's "nothing here" row ([data-drag-empty]) must be its first child.
//  * Keyboard: each row's [data-drag-move] buttons (up / down, and left / right across a group)
//    do the same moves.
(function () {
    var drag = null;

    function rows(list) {
        return Array.prototype.filter.call(list.children, function (el) {
            return el.hasAttribute("data-drag-item");
        });
    }

    function listsLike(list) {
        var group = list.getAttribute("data-drag-group");
        return group
            ? Array.prototype.slice.call(document.querySelectorAll("[data-drag-list][data-drag-group='" + CSS.escape(group) + "']"))
            : [list];
    }

    function token() {
        var input = document.querySelector("input[name='__RequestVerificationToken']");
        return input ? input.value : "";
    }

    function place(row) {
        var list = row.parentElement;
        return { list: list, index: rows(list).indexOf(row) };
    }

    function post(row) {
        var where = place(row);
        var body = new FormData();
        body.append("__RequestVerificationToken", token());
        body.append("index", String(where.index));
        var field = where.list.getAttribute("data-drag-field");
        if (field) body.append(field, where.list.getAttribute("data-drag-value") || "");

        refreshEmpty();
        return fetch(row.getAttribute("data-drag-url"), {
            method: "POST",
            body: body,
            credentials: "same-origin",
            headers: { "X-Requested-With": "fetch" },
        }).then(function (res) {
            if (!res.ok) throw new Error("move failed");
            if (window.qatLive && window.qatLive.isLive()) window.qatLive.refresh();
        }).catch(function () {
            window.location.reload();
        });
    }

    // Show a list's "nothing here" row only when it has no items; refresh the column counts.
    function refreshEmpty() {
        document.querySelectorAll("[data-drag-list]").forEach(function (list) {
            var empty = list.querySelector("[data-drag-empty]");
            var count = rows(list).length;
            if (empty) empty.hidden = count > 0;
            var counter = list.id && document.querySelector("[data-drag-count='" + CSS.escape(list.id) + "']");
            if (counter) counter.textContent = String(count);
        });
    }

    function busy(list, on) {
        var region = list.closest("[data-live-region]") || list;
        if (on) region.setAttribute("data-live-busy", "");
        else region.removeAttribute("data-live-busy");
    }

    // The list under (x, y): the group's list whose box contains the point, else the nearest by x
    // (columns sit side by side on wide screens and stack on narrow ones).
    function listAt(candidates, x, y) {
        var best = null;
        var bestDistance = Infinity;
        candidates.forEach(function (list) {
            var r = (list.closest("[data-drag-column]") || list).getBoundingClientRect();
            var dx = x < r.left ? r.left - x : x > r.right ? x - r.right : 0;
            var dy = y < r.top ? r.top - y : y > r.bottom ? y - r.bottom : 0;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance) {
                bestDistance = distance;
                best = list;
            }
        });
        return best;
    }

    document.addEventListener("pointerdown", function (e) {
        var row = e.target.closest("[data-drag-item]");
        if (!row || e.button !== 0 || e.target.closest("button, input, select, textarea, form, summary, [data-drag-move]")) return;
        if (e.pointerType !== "mouse" && !e.target.closest("[data-drag-handle]")) return;
        var list = row.parentElement;
        if (!list || !list.hasAttribute("data-drag-list")) return;

        // No preventDefault yet for a mouse — a plain click on the title link must still work.
        if (e.pointerType !== "mouse") e.preventDefault();
        var box = row.getBoundingClientRect();
        drag = {
            row: row, from: place(row), lists: listsLike(list), pointerId: e.pointerId,
            startX: e.clientX, startY: e.clientY, moved: false,
            // Where in the card it was grabbed, so the lifted copy stays under the pointer.
            offsetX: e.clientX - box.left, offsetY: e.clientY - box.top,
            ghost: null, over: null,
        };
    });

    // The card "in hand": a copy that follows the pointer, lifted, while the card itself
    // stays in the list as a dashed outline showing where it will land. The copy carries no data-*
    // attributes, so nothing (tests, other scripts) mistakes it for the real card.
    function lift(state) {
        var box = state.row.getBoundingClientRect();
        var ghost = state.row.cloneNode(true);
        [ghost].concat(Array.prototype.slice.call(ghost.querySelectorAll("*"))).forEach(function (el) {
            Array.prototype.slice.call(el.attributes).forEach(function (a) {
                if (a.name.indexOf("data-") === 0 || a.name === "id") el.removeAttribute(a.name);
            });
        });
        ghost.classList.add("drag-ghost");
        ghost.setAttribute("aria-hidden", "true");
        ghost.style.width = box.width + "px";
        document.body.appendChild(ghost);
        state.ghost = ghost;
        state.row.classList.add("drag-placeholder");
        document.documentElement.classList.add("dragging");
    }

    function follow(state, x, y) {
        state.ghost.style.left = (x - state.offsetX) + "px";
        state.ghost.style.top = (y - state.offsetY) + "px";
    }

    // Light up the column (or list) the card would drop into.
    function highlight(state, list) {
        var target = list && (list.closest("[data-drag-column]") || list);
        if (state.over === target) return;
        if (state.over) state.over.classList.remove("drag-over");
        if (target && state.lists.length > 1) target.classList.add("drag-over");
        state.over = target;
    }

    // Letting go: the lifted copy glides into the slot, then the card takes its place.
    function drop(state) {
        var ghost = state.ghost;
        var row = state.row;
        if (state.over) state.over.classList.remove("drag-over");
        document.documentElement.classList.remove("dragging");
        if (!ghost) {
            row.classList.remove("drag-placeholder");
            return;
        }
        var from = ghost.getBoundingClientRect();
        var to = row.getBoundingClientRect();
        var settle = function () {
            ghost.remove();
            row.classList.remove("drag-placeholder");
        };
        if (!ghost.animate || reducedMotion()) {
            settle();
            return;
        }
        ghost.animate(
            [{ transform: "translate(0, 0) scale(1.02)" },
             { transform: "translate(" + (to.left - from.left) + "px, " + (to.top - from.top) + "px) scale(1)" }],
            { duration: 140, easing: "ease-out", fill: "forwards" }
        ).onfinish = settle;
    }

    function reducedMotion() {
        return window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    }

    // Moves the slot with the other cards sliding to their new places (FLIP) rather than jumping.
    function shift(lists, move) {
        var animate = !reducedMotion();
        var before = new Map();
        if (animate) {
            lists.forEach(function (list) {
                rows(list).forEach(function (el) { before.set(el, el.getBoundingClientRect()); });
            });
        }
        move();
        refreshEmpty();
        if (!animate) return;
        before.forEach(function (old, el) {
            if (!el.isConnected || !el.animate) return;
            var now = el.getBoundingClientRect();
            var dx = old.left - now.left;
            var dy = old.top - now.top;
            if (dx || dy) {
                el.animate([{ transform: "translate(" + dx + "px, " + dy + "px)" }, { transform: "none" }],
                    { duration: 150, easing: "ease-out" });
            }
        });
    }

    // A card's box where it's heading, not mid-slide: the running slide's offset taken off.
    function settled(el) {
        var r = el.getBoundingClientRect();
        var t = getComputedStyle(el).transform;
        var dy = t && t !== "none" && window.DOMMatrixReadOnly ? new DOMMatrixReadOnly(t).m42 : 0;
        return { top: r.top - dy, bottom: r.bottom - dy, height: r.height };
    }

    // Where the slot should go for a pointer at y in list, or null to leave it be. Over another card:
    // its top half puts the slot before it, its bottom half after it. Over the slot itself, or in the
    // gap between cards, nothing changes — so the slot doesn't flicker as cards of different heights
    // shift around it. In another list's empty space, it goes before the first card below the pointer.
    function slotFor(list, y) {
        var all = rows(list);
        for (var i = 0; i < all.length; i++) {
            var r = settled(all[i]);
            if (y >= r.top && y <= r.bottom) {
                if (all[i] === drag.row) return null;
                return y < r.top + r.height / 2 ? all[i] : (all[i].nextElementSibling || "end");
            }
        }
        if (drag.row.parentElement === list) return null;
        var others = all.filter(function (el) { return el !== drag.row; });
        for (var j = 0; j < others.length; j++) {
            if (y < settled(others[j]).top) return others[j];
        }
        return "end";
    }

    document.addEventListener("pointermove", function (e) {
        if (!drag || e.pointerId !== drag.pointerId) return;
        if (!drag.moved) {
            if (Math.abs(e.clientY - drag.startY) < 5 && Math.abs(e.clientX - drag.startX) < 5) return;
            drag.moved = true;
            lift(drag);
            document.documentElement.classList.add("select-none");
            busy(drag.from.list, true);
        }
        e.preventDefault();
        follow(drag, e.clientX, e.clientY);

        var list = listAt(drag.lists, e.clientX, e.clientY) || drag.from.list;
        highlight(drag, list);
        var slot = slotFor(list, e.clientY);
        if (slot === null || slot === drag.row) return;
        // Already there? ("end": the "nothing here" row is always first, so the end is appendChild.)
        if (slot === "end" ? list.lastElementChild === drag.row : drag.row.nextElementSibling === slot) return;
        var home = drag.row.parentElement;
        shift(home === list ? [list] : [home, list], function () {
            if (slot === "end") list.appendChild(drag.row);
            else list.insertBefore(drag.row, slot);
        });
    });

    function endDrag(e) {
        if (!drag || e.pointerId !== drag.pointerId) return;
        var finished = drag;
        drag = null;
        if (!finished.moved) return;

        drop(finished);
        document.documentElement.classList.remove("select-none");
        busy(finished.from.list, false);

        // The click that follows a drag would open the card's link — swallow it (this tick only).
        var swallow = function (ev) { ev.stopPropagation(); ev.preventDefault(); };
        document.addEventListener("click", swallow, { capture: true, once: true });
        setTimeout(function () { document.removeEventListener("click", swallow, { capture: true }); }, 0);

        var to = place(finished.row);
        if (to.list !== finished.from.list || to.index !== finished.from.index) post(finished.row);
    }

    // Links and images inside a row would otherwise start the browser's own drag.
    document.addEventListener("dragstart", function (e) {
        if (e.target.closest && e.target.closest("[data-drag-item]")) e.preventDefault();
    });

    document.addEventListener("pointerup", endDrag);
    document.addEventListener("pointercancel", endDrag);

    document.addEventListener("click", function (e) {
        var btn = e.target.closest("[data-drag-move]");
        if (!btn) return;
        var row = btn.closest("[data-drag-item]");
        var list = row && row.parentElement;
        if (!list || !list.hasAttribute("data-drag-list")) return;
        e.preventDefault();

        var direction = btn.getAttribute("data-drag-move");
        var siblings = rows(list);
        var index = siblings.indexOf(row);
        if (direction === "up" && index > 0) {
            list.insertBefore(row, siblings[index - 1]);
        } else if (direction === "down" && index < siblings.length - 1) {
            list.insertBefore(siblings[index + 1], row);
        } else if (direction === "left" || direction === "right") {
            var group = listsLike(list);
            var target = group[group.indexOf(list) + (direction === "left" ? -1 : 1)];
            if (!target) return;
            var first = rows(target)[0];
            target.insertBefore(row, first || null);
        } else {
            return;
        }
        post(row);
        btn.focus();
    });

    document.addEventListener("DOMContentLoaded", refreshEmpty);
    document.addEventListener("enhancedload", refreshEmpty);
})();
