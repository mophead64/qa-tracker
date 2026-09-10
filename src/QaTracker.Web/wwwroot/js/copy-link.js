// Copy-to-clipboard for the project dashboard's custom link buttons. Each link renders as a
// compound control: an <a> that opens the URL and a [data-copy-link] button that copies it.
// On copy we briefly swap the button's icon to a tick (toggling [data-copied]) so there is
// visible feedback. Document-level delegation so it survives enhanced navigation, matching
// dropdown.js / import-file.js.

(function () {
    var RESET_MS = 1500;

    function copy(text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(text);
        }
        // Fallback for non-secure contexts / older browsers.
        return new Promise(function (resolve, reject) {
            try {
                var ta = document.createElement("textarea");
                ta.value = text;
                ta.setAttribute("readonly", "");
                ta.style.position = "absolute";
                ta.style.left = "-9999px";
                document.body.appendChild(ta);
                ta.select();
                document.execCommand("copy");
                document.body.removeChild(ta);
                resolve();
            } catch (err) {
                reject(err);
            }
        });
    }

    document.addEventListener("click", function (e) {
        var btn = e.target.closest("[data-copy-link]");
        if (!btn) return;

        var url = btn.getAttribute("data-copy-link");
        if (!url) return;

        copy(url).then(function () {
            btn.setAttribute("data-copied", "");
            clearTimeout(btn._copyTimer);
            btn._copyTimer = setTimeout(function () {
                btn.removeAttribute("data-copied");
            }, RESET_MS);
        }).catch(function () {
            /* clipboard blocked — nothing useful to do */
        });
    });
})();
