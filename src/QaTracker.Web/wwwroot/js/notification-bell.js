// Load the notification bell dropdown the first time it's opened on a page. The bell's
// contents are fetched from /notifications/panel rather than server-rendered inline, so
// notification text never sits in every page's DOM. The `toggle` event doesn't bubble, so
// this listens in the capture phase; a document-level listener survives enhanced navigation.
document.addEventListener("toggle", async function (e) {
    var d = e.target;
    if (!d || d.id !== "notification-bell" || !d.open) {
        return;
    }
    var panel = d.querySelector("[data-notif-panel]");
    if (!panel || panel.dataset.loaded) {
        return;
    }
    panel.dataset.loaded = "1";
    try {
        var url = "/notifications/panel?returnUrl=" + encodeURIComponent(location.pathname + location.search);
        var r = await fetch(url, { headers: { "Accept": "text/html" } });
        if (r.ok) {
            panel.innerHTML = await r.text();
        }
    } catch (_) {
        panel.dataset.loaded = "";
    }
}, true);
