// In-app attachment previews. Any [data-preview] trigger (a file name or the eye icon in
// AttachmentPanel / CommentAttachments) carries data-preview-kind (image|video|audio|pdf),
// data-preview-src (/attachments/{id}/preview), data-preview-download (/attachments/{id}) and
// data-preview-name. Clicking one fills the shared #attachment-preview dialog
// (AttachmentPreviewDialog.razor) and opens it. Built with DOM calls, never innerHTML, so a file
// name can't inject markup. Closing clears the body, which stops audio / video.
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

    document.addEventListener("click", function (e) {
        var trigger = e.target.closest("[data-preview]");
        if (!trigger) return;
        var d = dialog();
        if (!d || typeof d.showModal !== "function") return; // no modal: let a link fall through

        e.preventDefault();
        var name = trigger.dataset.previewName || "";
        var body = d.querySelector("[data-preview-body]");
        d.querySelector("[data-preview-title]").textContent = name;
        var download = d.querySelector("[data-preview-download]");
        download.href = trigger.dataset.previewDownload;
        download.setAttribute("download", name);
        download.setAttribute("aria-label", "Download " + name);
        body.replaceChildren(build(trigger.dataset.previewKind, trigger.dataset.previewSrc, name, body));
        if (!d.open) d.showModal();
    });

    // "close" doesn't bubble — capture it on the way down.
    document.addEventListener("close", function (e) {
        if (e.target === dialog()) {
            e.target.querySelector("[data-preview-body]").replaceChildren();
        }
    }, true);
})();
