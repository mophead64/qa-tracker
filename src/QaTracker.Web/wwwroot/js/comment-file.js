// Optional files on a comment form (CommentFileField.razor). The real <input type="file" multiple>
// is visually hidden behind a styled label. Each pick is *added* to what's already chosen (the
// input's FileList is rebuilt with a DataTransfer), the chosen files are listed with a Remove
// button each, and a file over the size limit (data-max-bytes) or beyond the count limit
// (data-max-files) is rejected straight away — so the typed comment isn't lost to a server-side
// rejection. Document-level delegation so it survives enhanced navigation, like import-file.js.

(function () {
    function fmtSize(bytes) {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + " KB";
        return (bytes / (1024 * 1024)).toFixed(1) + " MB";
    }

    function setFiles(input, files) {
        var dt = new DataTransfer();
        files.forEach(function (f) { dt.items.add(f); });
        input.files = dt.files;
    }

    function render(field) {
        var input = field.querySelector("[data-comment-file-input]");
        var list = field.querySelector("[data-comment-file-list]");
        var hint = field.querySelector("[data-comment-file-hint]");
        var files = Array.from(input.files || []);

        list.innerHTML = "";
        files.forEach(function (file, i) {
            var li = document.createElement("li");
            li.className = "flex items-center gap-3 py-1.5 text-sm";

            var name = document.createElement("span");
            name.className = "min-w-0 flex-1 truncate text-gray-700 dark:text-gray-300";
            name.textContent = file.name;

            var size = document.createElement("span");
            size.className = "shrink-0 text-xs text-gray-400";
            size.textContent = fmtSize(file.size);

            var remove = document.createElement("button");
            remove.type = "button";
            remove.className = "btn-link shrink-0 text-xs text-red-600 dark:text-red-400";
            remove.setAttribute("data-comment-file-remove", String(i));
            remove.textContent = "Remove";

            li.append(name, size, remove);
            list.appendChild(li);
        });

        list.hidden = files.length === 0;
        if (hint) hint.hidden = files.length > 0;
    }

    function showError(field, message) {
        var error = field.querySelector("[data-comment-file-error]");
        error.textContent = message;
        error.hidden = !message;
    }

    document.addEventListener("change", function (e) {
        if (!(e.target.matches && e.target.matches("[data-comment-file-input]"))) return;

        var input = e.target;
        var field = input.closest("[data-comment-file]");
        var maxBytes = parseInt(field.getAttribute("data-max-bytes") || "0", 10);
        var maxFiles = parseInt(field.getAttribute("data-max-files") || "0", 10);

        // The input now holds only the newest pick; the earlier ones are kept in field._kept.
        var kept = field._kept || [];
        var errors = [];
        Array.from(input.files).forEach(function (file) {
            if (maxBytes > 0 && file.size > maxBytes) {
                errors.push(file.name + " is larger than the " + Math.floor(maxBytes / (1024 * 1024)) + " MB limit.");
            } else if (maxFiles > 0 && kept.length >= maxFiles) {
                errors[0] = errors[0] || "A comment can have at most " + maxFiles + " attachments.";
            } else {
                kept.push(file);
            }
        });

        field._kept = kept;
        setFiles(input, kept);
        showError(field, errors.join(" "));
        render(field);
    });

    document.addEventListener("click", function (e) {
        var remove = e.target.closest("[data-comment-file-remove]");
        if (!remove) return;

        var field = remove.closest("[data-comment-file]");
        var input = field.querySelector("[data-comment-file-input]");
        var index = parseInt(remove.getAttribute("data-comment-file-remove"), 10);
        var kept = Array.from(input.files);
        kept.splice(index, 1);

        field._kept = kept;
        setFiles(input, kept);
        showError(field, "");
        render(field);
    });
})();
