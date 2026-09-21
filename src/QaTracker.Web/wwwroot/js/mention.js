// @-mentions in a comment box (MentionTextarea.razor). Typing "@" (at the start or after
// whitespace) opens a picker of the project's team, filtered as more is typed; ArrowUp/Down
// move, Enter/Tab or a click pick, Escape closes. Picking replaces "@partial" with "@Full Name "
// and adds a hidden name="mentions" input holding the user id. Each picked "@Name" is highlighted
// (a mirror layer behind the transparent textarea) and left out of the picker while it's in the
// text; delete it and the tag, the hidden input and the picker entry all come back. The server
// re-validates each id and that "@Name" is still in the text.
// Document-level delegation so it survives enhanced navigation, like comment-file.js.

(function () {
    var MAX_QUERY = 30;
    var MAX_ITEMS = 6;

    function users(field) {
        if (!field._users) {
            try { field._users = JSON.parse(field.getAttribute("data-mention-users") || "[]"); }
            catch (e) { field._users = []; }
        }
        return field._users;
    }

    // The "@query" being typed at the caret, or null.
    function activeMention(textarea) {
        var caret = textarea.selectionStart;
        var before = textarea.value.slice(0, caret);
        var at = before.lastIndexOf("@");
        if (at < 0) return null;
        if (at > 0 && !/\s/.test(before.charAt(at - 1))) return null;
        var query = before.slice(at + 1);
        if (query.length > MAX_QUERY || /[\r\n]/.test(query)) return null;
        return { start: at, end: caret, query: query };
    }

    // Picked users still tagged in the text, keyed by id (field._picked). A tag counts while
    // "@Name" appears in the text, not glued to a preceding word or a following letter/digit.
    function tagRegex(picked) {
        var names = Object.keys(picked).map(function (id) { return picked[id].name; })
            .sort(function (a, b) { return b.length - a.length; })
            .map(function (n) { return n.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"); });
        if (names.length === 0) return null;
        return new RegExp("(?<![^\\s])@(?:" + names.join("|") + ")(?![\\p{L}\\p{N}])", "giu");
    }

    // Drops picked users whose tag was deleted, rebuilds the hidden "mentions" inputs, and
    // repaints the highlight mirror behind the textarea.
    function sync(field) {
        var textarea = field.querySelector("[data-mention-input]");
        var picked = field._picked || (field._picked = {});
        var text = textarea.value;

        Object.keys(picked).forEach(function (id) {
            var only = {};
            only[id] = picked[id];
            var re = tagRegex(only);
            if (!re || !re.test(text)) delete picked[id];
        });

        var ids = field.querySelector("[data-mention-ids]");
        ids.innerHTML = "";
        Object.keys(picked).forEach(function (id) {
            var hidden = document.createElement("input");
            hidden.type = "hidden";
            hidden.name = "mentions";
            hidden.value = id;
            ids.appendChild(hidden);
        });

        var mirror = field.querySelector("[data-mention-mirror]");
        mirror.textContent = "";
        var re = tagRegex(picked);
        var last = 0;
        if (re) {
            var m;
            while ((m = re.exec(text)) !== null) {
                mirror.appendChild(document.createTextNode(text.slice(last, m.index)));
                var mark = document.createElement("mark");
                mark.className = "rounded bg-brand-200 text-transparent ring-1 ring-brand-300 dark:bg-brand-700/50 dark:ring-brand-600";
                mark.textContent = m[0];
                mirror.appendChild(mark);
                last = m.index + m[0].length;
            }
        }
        // Zero-width space so a trailing newline still takes up its line, like the textarea.
        mirror.appendChild(document.createTextNode(text.slice(last) + "\u200b"));
        mirror.scrollTop = textarea.scrollTop;
    }

    function matches(field, query) {
        var q = query.toLowerCase();
        var picked = field._picked || {};
        return users(field).filter(function (u) {
            if (picked[u.id]) return false;
            var name = u.name.toLowerCase();
            return name.indexOf(q) === 0 || name.split(/\s+/).some(function (w) { return w.indexOf(q) === 0; });
        }).slice(0, MAX_ITEMS);
    }

    function close(field) {
        var list = field.querySelector("[data-mention-list]");
        list.hidden = true;
        list.innerHTML = "";
        field._items = [];
        field._active = null;
    }

    function highlight(field) {
        var list = field.querySelector("[data-mention-list]");
        Array.from(list.children).forEach(function (li, i) {
            var on = i === field._index;
            li.setAttribute("aria-selected", on ? "true" : "false");
            li.classList.toggle("bg-brand-50", on);
            li.classList.toggle("dark:bg-gray-800", on);
        });
    }

    // Puts the picker just under the line holding the "@" being typed (over the textarea if need
    // be), by laying the text before the "@" out in the invisible probe and reading where an
    // "@" marker lands. Kept inside the field's width; the "@" is used (not the caret) so the
    // list doesn't slide sideways while the name is typed.
    function position(field, mention) {
        var textarea = field.querySelector("[data-mention-input]");
        var probe = field.querySelector("[data-mention-probe]");
        var list = field.querySelector("[data-mention-list]");

        probe.textContent = textarea.value.slice(0, mention.start);
        var marker = document.createElement("span");
        marker.textContent = "@";
        probe.appendChild(marker);
        probe.appendChild(document.createTextNode(textarea.value.slice(mention.start + 1) + "\u200b"));

        var top = marker.offsetTop - textarea.scrollTop + marker.offsetHeight + 2;
        var left = marker.offsetLeft;
        list.style.top = Math.max(top, 0) + "px";
        list.style.left = Math.max(0, Math.min(left, field.clientWidth - list.offsetWidth)) + "px";
    }

    function open(field, mention, items) {
        var list = field.querySelector("[data-mention-list]");
        list.innerHTML = "";
        items.forEach(function (u, i) {
            var li = document.createElement("li");
            li.setAttribute("role", "option");
            li.setAttribute("data-mention-option", String(i));
            li.className = "flex cursor-pointer items-center justify-between gap-2 px-3 py-1.5 text-sm text-gray-700 dark:text-gray-200";
            var name = document.createElement("span");
            name.className = "truncate";
            name.textContent = u.name;
            li.appendChild(name);
            if (u.role) {
                var role = document.createElement("span");
                role.className = "shrink-0 text-xs text-gray-400";
                role.textContent = u.role;
                li.appendChild(role);
            }
            list.appendChild(li);
        });
        field._items = items;
        field._active = mention;
        field._index = 0;
        list.hidden = false;
        list.style.bottom = "auto";
        position(field, mention);
        highlight(field);
    }

    function refresh(field) {
        var textarea = field.querySelector("[data-mention-input]");
        var mention = activeMention(textarea);
        var items = mention ? matches(field, mention.query) : [];
        if (items.length === 0) { close(field); return; }
        open(field, mention, items);
    }

    function pick(field, user) {
        var textarea = field.querySelector("[data-mention-input]");
        var m = field._active;
        if (!m) return;
        var insert = "@" + user.name + " ";
        textarea.value = textarea.value.slice(0, m.start) + insert + textarea.value.slice(m.end);
        var caret = m.start + insert.length;
        textarea.setSelectionRange(caret, caret);

        (field._picked || (field._picked = {}))[user.id] = user;
        sync(field);
        close(field);
        textarea.focus();
    }

    document.addEventListener("input", function (e) {
        var t = e.target;
        if (!(t.matches && t.matches("[data-mention-input]"))) return;
        var field = t.closest("[data-mention-field]");
        sync(field);
        refresh(field);
    });

    // scroll doesn't bubble, so listen in the capture phase to keep the mirror aligned.
    document.addEventListener("scroll", function (e) {
        var t = e.target;
        if (!(t.matches && t.matches("[data-mention-input]"))) return;
        var field = t.closest("[data-mention-field]");
        field.querySelector("[data-mention-mirror]").scrollTop = t.scrollTop;
        if (field._active) position(field, field._active);
    }, true);

    // Moving the caret with the mouse or Home/End/Left/Right can enter or leave a mention.
    document.addEventListener("click", function (e) {
        var t = e.target;
        if (t.matches && t.matches("[data-mention-input]")) refresh(t.closest("[data-mention-field]"));
    });

    document.addEventListener("keydown", function (e) {
        var t = e.target;
        if (!(t.matches && t.matches("[data-mention-input]"))) return;
        var field = t.closest("[data-mention-field]");
        var items = field._items || [];
        if (items.length === 0) return;

        if (e.key === "ArrowDown") {
            e.preventDefault();
            field._index = (field._index + 1) % items.length;
            highlight(field);
        } else if (e.key === "ArrowUp") {
            e.preventDefault();
            field._index = (field._index - 1 + items.length) % items.length;
            highlight(field);
        } else if (e.key === "Enter" || e.key === "Tab") {
            e.preventDefault();
            pick(field, items[field._index]);
        } else if (e.key === "Escape") {
            e.preventDefault();
            e.stopPropagation();
            close(field);
        }
    });

    // mousedown (not click) so the textarea keeps focus and its caret position.
    document.addEventListener("mousedown", function (e) {
        var option = e.target.closest && e.target.closest("[data-mention-option]");
        if (option) {
            e.preventDefault();
            var field = option.closest("[data-mention-field]");
            pick(field, field._items[parseInt(option.getAttribute("data-mention-option"), 10)]);
            return;
        }
        document.querySelectorAll("[data-mention-field]").forEach(function (f) {
            if (!f.contains(e.target)) close(f);
        });
    });

    document.addEventListener("focusout", function (e) {
        var t = e.target;
        if (!(t.matches && t.matches("[data-mention-input]"))) return;
        // Deferred so a mousedown on an option (which preventDefaults) still wins.
        var field = t.closest("[data-mention-field]");
        setTimeout(function () { if (document.activeElement !== t) close(field); }, 100);
    });
})();
