// Drives the "Add files" modal on AttachmentPanel (shared by project resources, test-case
// resources, and defect files). Static SSR: the page has no circuit, so the modal is a
// plain <dialog> and the upload is done here.
//
// Files are *staged* client-side — picking files adds them to a list (picking again appends
// rather than replaces), each row has a Remove button, and files over the size limit are
// flagged and block the upload until removed. On Upload each staged file is POSTed to
// /attachments/upload in turn (the endpoint takes one IFormFile per request) with a running
// "x of N" progress bar and a spinner on the button; on success the page reloads so the
// server re-renders the file list. Document-level listeners so this survives enhanced
// navigation, matching notification-bell.js.

(function () {
    var STAGE = new WeakMap();

    function staged(panel) {
        var list = STAGE.get(panel);
        if (!list) {
            list = [];
            STAGE.set(panel, list);
        }
        return list;
    }

    function fmtSize(bytes) {
        var units = ["B", "KB", "MB", "GB"];
        var size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.length - 1) {
            size /= 1024;
            unit++;
        }
        return (Math.round(size * 10) / 10) + " " + units[unit];
    }

    function panelFor(el) {
        return el.closest("[data-upload-panel]");
    }

    function maxBytesFor(panel) {
        return Number(panel.dataset.maxBytes) || 0;
    }

    function showError(panel, message) {
        var err = panel.querySelector("[data-upload-error]");
        err.textContent = message || "";
        err.hidden = !message;
    }

    function render(panel) {
        var files = staged(panel);
        var list = panel.querySelector("[data-upload-list]");
        var empty = panel.querySelector("[data-upload-empty]");
        var total = panel.querySelector("[data-upload-total]");
        var submit = panel.querySelector("[data-upload-submit]");
        var maxBytes = maxBytesFor(panel);

        list.innerHTML = "";
        var anyTooLarge = false;
        var totalBytes = 0;

        files.forEach(function (file, i) {
            totalBytes += file.size;
            var tooLarge = maxBytes > 0 && file.size > maxBytes;
            anyTooLarge = anyTooLarge || tooLarge;

            var li = document.createElement("li");
            li.className = "flex items-center gap-3 py-2 text-sm";

            var name = document.createElement("span");
            name.className = "min-w-0 flex-1 truncate "
                + (tooLarge ? "text-red-600 dark:text-red-400" : "text-gray-700 dark:text-gray-300");
            name.textContent = file.name;

            var size = document.createElement("span");
            size.className = "shrink-0 text-xs "
                + (tooLarge ? "text-red-600 dark:text-red-400" : "text-gray-400");
            size.textContent = fmtSize(file.size) + (tooLarge ? " · too large" : "");

            var remove = document.createElement("button");
            remove.type = "button";
            remove.className = "btn-link shrink-0 text-xs text-red-600 dark:text-red-400";
            remove.textContent = "Remove";
            remove.dataset.removeIndex = String(i);

            li.appendChild(name);
            li.appendChild(size);
            li.appendChild(remove);
            list.appendChild(li);
        });

        empty.hidden = files.length > 0;
        total.hidden = files.length === 0;
        total.textContent = "Total: " + fmtSize(totalBytes);
        submit.disabled = files.length === 0 || anyTooLarge;
    }

    function addFiles(panel, fileList) {
        var list = staged(panel);
        Array.from(fileList).forEach(function (file) {
            var dup = list.some(function (f) {
                return f.name === file.name && f.size === file.size && f.lastModified === file.lastModified;
            });
            if (!dup) {
                list.push(file);
            }
        });
        render(panel);
    }

    function resetStage(panel) {
        STAGE.set(panel, []);
        var input = panel.querySelector("[data-upload-input]");
        if (input) input.value = "";
        var description = panel.querySelector("[data-upload-description]");
        if (description) description.value = "";
        panel.querySelector("[data-upload-progress]").hidden = true;
        showError(panel, "");
        render(panel);
    }

    function setBusy(panel, busy) {
        panel.querySelectorAll(
            "[data-upload-submit], [data-upload-cancel], [data-upload-input], [data-upload-description], [data-remove-index]"
        ).forEach(function (el) {
            el.disabled = busy;
        });
        var choose = panel.querySelector("[data-upload-choose]");
        if (choose) choose.classList.toggle("pointer-events-none", busy);
        if (choose) choose.classList.toggle("opacity-60", busy);
        var spinner = panel.querySelector("[data-upload-spinner]");
        if (spinner) spinner.hidden = !busy;
    }

    async function uploadOne(panel, file, token) {
        var body = new FormData();
        body.append("Owner", panel.dataset.owner);
        body.append("OwnerId", panel.dataset.ownerId);
        body.append("ReturnUrl", panel.dataset.returnUrl);
        var description = panel.querySelector("[data-upload-description]");
        // Always send Description (even empty) — the Blazor form-data mapper treats the
        // record's positional parameter as required and 400s if the field is absent.
        body.append("Description", description ? description.value : "");
        if (token) body.append("__RequestVerificationToken", token);
        body.append("file", file, file.name);

        var res = await fetch("/attachments/upload", {
            method: "POST",
            headers: { "X-Requested-With": "fetch" },
            body: body,
        });

        if (!res.ok) {
            var message = "Upload failed for " + file.name + ".";
            try {
                var data = await res.json();
                if (data?.error) message = data.error;
            } catch (_) { /* keep the generic message */ }
            throw new Error(message);
        }
    }

    async function runUpload(panel) {
        var files = staged(panel).slice();
        if (files.length === 0) return;

        var maxBytes = maxBytesFor(panel);
        if (files.some(function (f) { return maxBytes > 0 && f.size > maxBytes; })) {
            showError(panel, "Remove the files marked “too large” before uploading.");
            return;
        }

        var progress = panel.querySelector("[data-upload-progress]");
        var bar = panel.querySelector("[data-upload-bar]");
        var status = panel.querySelector("[data-upload-status]");
        var form = panel.querySelector("[data-upload-form]");
        var tokenInput = form.querySelector("input[name='__RequestVerificationToken']");
        var token = tokenInput ? tokenInput.value : null;

        showError(panel, "");
        setBusy(panel, true);
        progress.hidden = false;
        bar.max = files.length;
        bar.value = 0;

        var done = 0;
        for (var i = 0; i < files.length; i++) {
            status.textContent = "Uploading " + (i + 1) + " of " + files.length + "…";
            try {
                await uploadOne(panel, files[i], token);
            } catch (e) {
                staged(panel).splice(0, done); // drop the ones that already went up
                setBusy(panel, false);
                progress.hidden = true;
                render(panel);
                showError(panel, e.message + (done > 0 ? " (" + done + " already uploaded)" : ""));
                return;
            }
            done++;
            bar.value = done;
        }

        status.textContent = "Done. Refreshing…";
        window.location.reload();
    }

    document.addEventListener("click", function (e) {
        var opener = e.target.closest("[data-upload-open]");
        if (opener) {
            var panel = panelFor(opener);
            var dialog = panel.querySelector("[data-upload-dialog]");
            if (dialog && !dialog.open && typeof dialog.showModal === "function") {
                resetStage(panel);
                dialog.showModal();
            }
            return;
        }

        var removeBtn = e.target.closest("[data-remove-index]");
        if (removeBtn && !removeBtn.disabled) {
            var p = panelFor(removeBtn);
            staged(p).splice(Number(removeBtn.dataset.removeIndex), 1);
            showError(p, "");
            render(p);
            return;
        }

        var cancel = e.target.closest("[data-upload-cancel]");
        if (cancel) {
            var d = panelFor(cancel).querySelector("[data-upload-dialog]");
            if (d?.open) d.close();
        }
    });

    document.addEventListener("change", function (e) {
        var input = e.target.closest("[data-upload-input]");
        if (!input) return;
        var panel = panelFor(input);
        addFiles(panel, input.files);
        input.value = ""; // let the same file be re-picked, and the next pick append
    });

    document.addEventListener("submit", function (e) {
        var form = e.target.closest("[data-upload-form]");
        if (!form) return;
        e.preventDefault();
        runUpload(panelFor(form));
    });

    // Per-file delete confirmation is a plain <details> popover; dismiss it on an
    // outside click or Escape (there's a no-JS "Cancel" link inside it as the fallback).
    function closeDeleteConfirms(except) {
        document.querySelectorAll("details[data-delete-confirm][open]").forEach(function (d) {
            if (d !== except) d.open = false;
        });
    }

    document.addEventListener("click", function (e) {
        if (e.target.closest("[data-delete-cancel]")) {
            closeDeleteConfirms(null);
            return;
        }
        var open = e.target.closest("details[data-delete-confirm]");
        closeDeleteConfirms(open);
    });

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") closeDeleteConfirms(null);
    });
})();
