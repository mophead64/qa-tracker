// In-app attachment previews. Any [data-preview] trigger (a file name or the eye icon in
// AttachmentPanel / CommentAttachments) carries data-preview-kind (image|video|audio|pdf),
// data-preview-src (/attachments/{id}/preview), data-preview-download (/attachments/{id}) and
// data-preview-name. Clicking one fills the shared #attachment-preview dialog
// (AttachmentPreviewDialog.razor) and opens it. Built with DOM calls, never innerHTML, so a file
// name can't inject markup. Closing clears the body, which stops audio / video. Files opened from a
// group ([data-preview-group]) can be stepped through with Previous / Next or the arrow keys.
// Document-level listeners so this survives enhanced navigation, like dialog.js.
(function () {
    function dialog() {
        return document.getElementById("attachment-preview");
    }

    function message(text) {
        var p = document.createElement("p");
        p.className = "text-sm text-gray-500 dark:text-gray-400";
        p.textContent = text;
        return p;
    }

    function failed(body) {
        body.replaceChildren(message("This file couldn't be previewed. Use Download instead."));
    }

    function build(kind, src, name, body) {
        var el;
        if (kind === "image") {
            el = document.createElement("img");
            el.alt = name;
            el.className = "max-h-[75vh] max-w-full rounded object-contain";
        } else if (kind === "video") {
            el = document.createElement("video");
            el.controls = true;
            el.preload = "metadata";
            el.className = "max-h-[75vh] max-w-full rounded bg-black";
        } else if (kind === "audio") {
            el = document.createElement("audio");
            el.controls = true;
            el.preload = "metadata";
            el.className = "w-full max-w-xl";
        } else if (kind === "pdf") {
            el = document.createElement("iframe");
            el.title = name;
            el.className = "h-[75vh] w-full rounded border-0 bg-white";
        } else {
            return message("No preview for this type of file.");
        }

        if (kind !== "pdf") {
            el.addEventListener("error", function () { failed(body); });
        }
        el.setAttribute("data-preview-media", kind);
        el.src = src;
        return el;
    }

    function entryOf(el) {
        var d = el.dataset;
        return {
            kind: d.previewKind,
            src: d.previewSrc,
            download: d.previewDownload,
            name: d.previewName || "",
            meta: d.previewMeta || null,
            commentAuthor: d.previewCommentAuthor || null,
            commentWhen: d.previewCommentWhen || null,
            commentBody: d.previewCommentBody || null,
            commentHref: d.previewCommentHref || null,
            description: d.previewDescription || null,
            commentRuns: parseRuns(d.previewCommentRuns),
        };
    }

    function parseRuns(json) {
        try {
            var runs = JSON.parse(json || "null");
            return Array.isArray(runs) ? runs : null;
        } catch (err) {
            return null;
        }
    }

    // The comment's text with its "@Name" mentions as green tags and "#D-3" references as links, the
    // same as under the comment (CommentBody.TagClass / ReferenceClass, passed on the dialog); plain
    // text when the runs weren't supplied.
    function fillComment(d, el, entry) {
        var runs = entry.commentRuns;
        if (!runs || !runs.length) {
            el.textContent = entry.commentBody || "";
            return;
        }
        var tagClass = d.getAttribute("data-mention-class") || "";
        var refClass = d.getAttribute("data-reference-class") || "";
        el.replaceChildren.apply(el, runs.map(function (run) {
            if (run.href) {
                // A "#D-3" / "#T-12" reference (CommentBody.Runs): a link to the item.
                var a = document.createElement("a");
                a.href = run.href;
                a.className = refClass;
                if (run.title) a.title = run.title;
                a.textContent = run.text;
                return a;
            }
            if (!run.mention) return document.createTextNode(run.text);
            var span = document.createElement("span");
            span.className = tagClass;
            span.textContent = run.text;
            return span;
        }));
    }

    function listed(group) {
        try {
            var parsed = JSON.parse(group.getAttribute("data-preview-sequence") || "null");
            return Array.isArray(parsed) ? parsed : null;
        } catch (err) {
            return null;
        }
    }

    // The files to step through: the clicked file's group — the whole list an attachments panel
    // holds (its data-preview-sequence JSON, which spans every page) or the triggers inside a group
    // without one (a comment's files) — de-duplicated, since a name and its eye icon both open the
    // same file. Without a group it's just the one file.
    function sequenceFor(trigger) {
        var group = trigger.closest("[data-preview-group]");
        var source = [];
        if (group) {
            source = listed(group) || Array.from(group.querySelectorAll("[data-preview]")).map(entryOf);
        }
        var seen = {};
        var entries = source.filter(function (e) {
            if (!e.src || seen[e.src]) return false;
            seen[e.src] = true;
            return true;
        });
        var clicked = entryOf(trigger);
        var index = entries.findIndex(function (e) { return e.src === clicked.src; });
        return index < 0 ? { entries: [clicked], index: 0 } : { entries: entries, index: index };
    }

    function show(d, index) {
        var seq = d._sequence;
        if (!seq || index < 0 || index >= seq.entries.length) return;
        seq.index = index;
        var entry = seq.entries[index];

        var body = d.querySelector("[data-preview-body]");
        d.querySelector("[data-preview-title]").textContent = entry.name;
        var download = d.querySelector("[data-preview-download]");
        download.href = entry.download;
        download.setAttribute("download", entry.name);
        download.setAttribute("aria-label", "Download " + entry.name);
        body.replaceChildren(build(entry.kind, entry.src, entry.name, body));
        d.querySelector("[data-preview-meta]").textContent = entry.meta || "";

        // The file's description, when it has one.
        var description = d.querySelector("[data-preview-description]");
        description.hidden = !entry.description;
        d.querySelector("[data-preview-description-text]").textContent = entry.description || "";

        // A file that came with a comment shows the comment, for context.
        var comment = d.querySelector("[data-preview-comment]");
        comment.hidden = !entry.commentAuthor;
        if (entry.commentAuthor) {
            d.querySelector("[data-preview-comment-author]").textContent = entry.commentAuthor;
            d.querySelector("[data-preview-comment-when]").textContent = entry.commentWhen || "";
            fillComment(d, d.querySelector("[data-preview-comment-body]"), entry);
            var link = d.querySelector("[data-preview-comment-link]");
            link.hidden = !entry.commentHref;
            link.href = entry.commentHref || "#";
        }

        var nav = d.querySelector("[data-preview-nav]");
        var many = seq.entries.length > 1;
        nav.hidden = !many;
        if (many) {
            d.querySelector("[data-preview-position]").textContent = (index + 1) + " of " + seq.entries.length;
            d.querySelector("[data-preview-prev]").disabled = index === 0;
            d.querySelector("[data-preview-next]").disabled = index === seq.entries.length - 1;
        }
    }

    function step(d, delta) {
        if (d && d.open && d._sequence) show(d, d._sequence.index + delta);
    }

    document.addEventListener("click", function (e) {
        var d = dialog();
        // "Go to comment": close first, then let the link take the reader there (its ?highlight=
        // makes the page render the comment outlined).
        if (d && e.target.closest("[data-preview-comment-link]")) { d.close(); return; }
        if (d && e.target.closest("[data-preview-prev]")) { step(d, -1); return; }
        if (d && e.target.closest("[data-preview-next]")) { step(d, 1); return; }

        var trigger = e.target.closest("[data-preview]");
        if (!trigger) return;
        if (!d || typeof d.showModal !== "function") return; // no modal: let a link fall through

        e.preventDefault();
        d._sequence = sequenceFor(trigger);
        show(d, d._sequence.index);
        if (!d.open) d.showModal();
    });

    // ← / → step through the files, unless a media player has focus (where they seek).
    document.addEventListener("keydown", function (e) {
        var d = dialog();
        if (!d || !d.open || e.altKey || e.ctrlKey || e.metaKey) return;
        if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
        if (e.target.closest && e.target.closest("video, audio, iframe, input, textarea")) return;
        e.preventDefault();
        step(d, e.key === "ArrowLeft" ? -1 : 1);
    });

    // "close" doesn't bubble — capture it on the way down.
    document.addEventListener("close", function (e) {
        if (e.target === dialog()) {
            e.target.querySelector("[data-preview-body]").replaceChildren();
            e.target._sequence = null;
        }
    }, true);
})();
