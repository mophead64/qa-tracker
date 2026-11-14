// @-mentions in a comment box (MentionTextarea.razor). Typing "@" (at the start or after
// whitespace) opens a picker of the project's team, filtered as more is typed; ArrowUp/Down
// move, Enter/Tab or a click pick, Escape closes. Picking replaces "@partial" with "@Full Name "
// and adds a hidden name="mentions" input holding the user id. Each picked "@Name" is highlighted
// (a mirror layer behind the transparent textarea) and left out of the picker while it's in the
// text; delete it and the tag, the hidden input and the picker entry all come back. The server
// re-validates each id and that "@Name" is still in the text.
//
// "#" works the same way over the project's defects and test cases (data-reference-src, fetched
// the first time "#" is typed and again once it's a minute old): "#log" finds "Login broken",
// "#d" / "#d-1" / "#12" narrow by ID. Picking writes "#D-3 "; references to items that exist are
// highlighted while typing and become links once posted (CommentBody / CommentReference.cs).
// Document-level delegation so it survives enhanced navigation, like comment-file.js.

(function () {
    var MAX_QUERY = 30;
    var MAX_ITEMS = 6;
    var MAX_REFS = 8;
    var REFS_TTL_MS = 60000;
    // Same rule as CommentReference.Pattern() on the server.
    var REF_RE = /(?<![\p{L}\p{N}_#&])#([DT])-([0-9]{1,9})(?![\p{L}\p{N}_])/giu;

    function users(field) {
        if (!field._users) {
            try { field._users = JSON.parse(field.getAttribute("data-mention-users") || "[]"); }
            catch (e) { field._users = []; }
        }
        return field._users;
    }

    // The project's defects and test cases for "#", or null until they've arrived. Fetching
    // calls back (to reopen the picker) once they land.
    function refs(field, onLoad) {
        var src = field.getAttribute("data-reference-src");
        if (!src) return [];
        var fresh = field._refs && Date.now() - field._refsAt < REFS_TTL_MS;
        if (!fresh && !field._refsLoading) {
            field._refsLoading = true;
            fetch(src, { headers: { Accept: "application/json" } })
                .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
                .then(function (list) {
                    field._refs = Array.isArray(list) ? list : [];
                    field._refsAt = Date.now();
                    sync(field);
                    if (onLoad) onLoad();
                })
                .catch(function () { /* no picker this time; plain text still posts */ })
                .finally(function () { field._refsLoading = false; });
        }
        return field._refs || null;
    }

    // The "@query" / "#query" being typed at the caret, or null. The trigger has to start the
    // text or follow whitespace ("#" may also follow "(").
    function activeMention(field, textarea) {
        var caret = textarea.selectionStart;
        var before = textarea.value.slice(0, caret);
        var at = before.lastIndexOf("@");
        var hash = field.hasAttribute("data-reference-src") ? before.lastIndexOf("#") : -1;
        var start = Math.max(at, hash);
        if (start < 0) return null;
        var trigger = before.charAt(start);
        var prev = start > 0 ? before.charAt(start - 1) : "";
        if (prev && !(/\s/.test(prev) || (trigger === "#" && prev === "("))) return null;
        var query = before.slice(start + 1);
        if (query.length > MAX_QUERY || /[\r\n]/.test(query)) return null;
        // "#D-3 " — a whole ID and a space: that reference is done, the rest is just text.
        if (trigger === "#" && /^[dt]-?[0-9]+\s/i.test(query)) return null;
        return { trigger: trigger, start: start, end: caret, query: query };
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

    // Where the text's highlights go: picked "@Name" tags and "#D-3" references to items that
    // exist, in order, without overlaps.
    function highlights(field, text) {
        var marks = [];
        var re = tagRegex(field._picked || {});
        var m;
        if (re) {
            while ((m = re.exec(text)) !== null) marks.push({ start: m.index, end: m.index + m[0].length, ref: false });
        }
        var known = field._refs;
        if (known && known.length) {
            var keys = {};
            known.forEach(function (r) { keys[r.ref.toUpperCase()] = true; });
            REF_RE.lastIndex = 0;
            while ((m = REF_RE.exec(text)) !== null) {
                if (keys[(m[1] + "-" + parseInt(m[2], 10)).toUpperCase()]) {
                    marks.push({ start: m.index, end: m.index + m[0].length, ref: true });
                }
            }
        }
        marks.sort(function (a, b) { return a.start - b.start; });
        var last = -1;
        return marks.filter(function (mark) {
            if (mark.start < last) return false;
            last = mark.end;
            return true;
        });
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
        var last = 0;
        highlights(field, text).forEach(function (h) {
            mirror.appendChild(document.createTextNode(text.slice(last, h.start)));
            var mark = document.createElement("mark");
            mark.className = h.ref
                ? "rounded bg-sky-100 text-transparent ring-1 ring-sky-300 dark:bg-sky-800/50 dark:ring-sky-600"
                : "rounded bg-brand-200 text-transparent ring-1 ring-brand-300 dark:bg-brand-700/50 dark:ring-brand-600";
            mark.textContent = text.slice(h.start, h.end);
            mirror.appendChild(mark);
            last = h.end;
        });
        // Zero-width space so a trailing newline still takes up its line, like the textarea.
        mirror.appendChild(document.createTextNode(text.slice(last) + "\u200b"));
        mirror.scrollTop = textarea.scrollTop;
    }

    // "#" picker matches: "d" / "t" / "d-1" / "12" narrow by ID, anything else matches the title
    // (every word, anywhere in it). An exact ID comes first.
    function refMatches(list, query) {
        var q = query.trim().toLowerCase();
        var byId = /^(?:([dt])-?)?([0-9]*)$/.exec(q);
        var found;
        if (q === "") {
            found = list.slice();
        } else if (byId) {
            found = list.filter(function (r) {
                var parts = r.ref.toLowerCase().split("-");
                return (!byId[1] || parts[0] === byId[1]) && parts[1].indexOf(byId[2]) === 0;
            });
            found.sort(function (a, b) {
                var exact = function (r) { return r.ref.toLowerCase().replace("-", "") === q.replace("-", "") ? 0 : 1; };
                return exact(a) - exact(b);
            });
        } else {
            var words = q.split(/\s+/);
            found = list.filter(function (r) {
                var hay = (r.ref + " " + r.title).toLowerCase();
                return words.every(function (w) { return hay.indexOf(w) >= 0; });
            });
        }
        return found.slice(0, MAX_REFS);
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
        list.setAttribute("aria-label", mention.trigger === "#" ? "Link a defect or test case" : "Mention a team member");
        items.forEach(function (u, i) {
            var li = document.createElement("li");
            li.setAttribute("role", "option");
            li.setAttribute("data-mention-option", String(i));
            li.className = "flex cursor-pointer items-center justify-between gap-2 px-3 py-1.5 text-sm text-gray-700 dark:text-gray-200";
            if (mention.trigger === "#") {
                // "D-3  Login broken ........ Fixing", like an ID tag elsewhere.
                // No title tooltip: the list opens under the pointer, and tooltip.js would cover it.
                li.setAttribute("data-reference-option", u.ref);
                var id = document.createElement("span");
                id.className = "shrink-0 font-mono text-xs text-gray-500 dark:text-gray-400";
                id.textContent = u.ref;
                li.appendChild(id);
                var title = document.createElement("span");
                title.className = "min-w-0 flex-1 truncate";
                title.textContent = u.title;
                li.appendChild(title);
                var status = document.createElement("span");
                status.className = "shrink-0 text-xs text-gray-400";
                status.textContent = u.status;
                li.appendChild(status);
            } else {
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
        var mention = activeMention(field, textarea);
        var items = [];
        if (mention && mention.trigger === "#") {
            // Not loaded yet: try again when the list arrives (if "#…" is still being typed).
            var list = refs(field, function () { if (document.activeElement === textarea) refresh(field); });
            items = list ? refMatches(list, mention.query) : [];
        } else if (mention) {
            items = matches(field, mention.query);
        }
        if (items.length === 0) { close(field); return; }
        open(field, mention, items);
    }

    function pick(field, user) {
        var textarea = field.querySelector("[data-mention-input]");
        var m = field._active;
        if (!m) return;
        var insert = m.trigger === "#" ? "#" + user.ref + " " : "@" + user.name + " ";
        textarea.value = textarea.value.slice(0, m.start) + insert + textarea.value.slice(m.end);
        var caret = m.start + insert.length;
        textarea.setSelectionRange(caret, caret);

        if (m.trigger !== "#") (field._picked || (field._picked = {}))[user.id] = user;
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
