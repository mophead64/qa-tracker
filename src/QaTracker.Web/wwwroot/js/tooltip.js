// Replaces the browser's native `title` tooltip (slow to appear, unstyled) with the app's own
// instant tooltip, matching the look of <Tooltip> and the project link tooltips.
//
// Any element with a title="..." attribute gets it: on hover or keyboard focus the title is
// moved into data-tip-title — which stops the browser showing its own tooltip — and shown in
// a single floating .app-tip element; it's put back when the pointer/focus leaves. Delegated
// document-level listeners, so it covers server-rendered and enhanced-navigation content alike.

(function () {
    var tip = null;
    var current = null;

    function ensureTip() {
        if (!tip) {
            tip = document.createElement("div");
            tip.className = "app-tip";
            tip.setAttribute("role", "tooltip");
            tip.hidden = true;
            document.body.appendChild(tip);
        }
        return tip;
    }

    function show(el) {
        var text = el.getAttribute("title");
        if (!text || !text.trim()) return;

        if (current && current !== el) hide();
        current = el;
        el.setAttribute("data-tip-title", text);
        el.removeAttribute("title");

        var t = ensureTip();
        t.textContent = text;
        t.hidden = false;

        // Below the element, left-aligned, clamped to the viewport; flipped above if there's no room.
        var rect = el.getBoundingClientRect();
        var margin = 8;
        var left = Math.max(margin, Math.min(rect.left, window.innerWidth - t.offsetWidth - margin));
        var top = rect.bottom + margin;
        if (top + t.offsetHeight > window.innerHeight - margin && rect.top - margin - t.offsetHeight > 0) {
            top = rect.top - margin - t.offsetHeight;
        }
        t.style.left = left + "px";
        t.style.top = top + "px";
    }

    function hide() {
        if (current) {
            var saved = current.getAttribute("data-tip-title");
            if (saved !== null) {
                current.setAttribute("title", saved);
                current.removeAttribute("data-tip-title");
            }
            current = null;
        }
        if (tip) tip.hidden = true;
    }

    function target(e) {
        return e.target instanceof Element ? e.target.closest("[title]") : null;
    }

    document.addEventListener("mouseover", function (e) {
        var el = target(e);
        if (el && el !== current) show(el);
    });

    document.addEventListener("mouseout", function (e) {
        if (current && !current.contains(e.relatedTarget)) hide();
    });

    document.addEventListener("focusin", function (e) {
        var el = target(e);
        if (el) show(el);
    });

    document.addEventListener("focusout", hide);
    document.addEventListener("click", hide);
    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") hide();
    });
    window.addEventListener("scroll", hide, true);
    document.addEventListener("enhancedload", hide);
})();
