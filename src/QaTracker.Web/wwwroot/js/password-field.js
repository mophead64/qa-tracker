// New-password fields (PasswordField.razor): ticks off Identity's password rules and rates the
// password as you type, blocks the form from posting a password that breaks a rule (and a
// confirm field that doesn't match), shows / hides it, copies it, and generates a strong one
// in the browser with crypto.getRandomValues. The rules come from data attributes the server
// renders from Identity's PasswordOptions, so they always match what the server enforces.
// Document-level delegation so it survives enhanced navigation, like dialog.js.

(function () {
    // Generated passwords leave out look-alikes (l / I / 1, O / 0) so they can be read out.
    var LOWER = "abcdefghijkmnpqrstuvwxyz";
    var UPPER = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    var DIGITS = "23456789";
    var COMMON = /(password|passw0rd|qwerty|letmein|welcome|admin|iloveyou|monkey|dragon|abc123|123456|qatracker)/;
    var LABELS = ["", "Weak", "Fair", "Good", "Strong"];

    function rulesOf(field) {
        var d = field.dataset;
        return {
            minLength: parseInt(d.minLength || "6", 10),
            lower: d.requireLower === "true",
            upper: d.requireUpper === "true",
            digit: d.requireDigit === "true",
            symbol: d.requireSymbol === "true",
            unique: parseInt(d.unique || "1", 10),
            symbols: d.symbols || "!@#$%^&*-_=+?",
            generateLength: parseInt(d.generateLength || "20", 10),
        };
    }

    // Same checks as Identity's PasswordValidator (ASCII classes; "symbol" = not a letter or digit).
    function unmet(pw, r) {
        var out = [];
        if (pw.length < r.minLength) out.push("length");
        if (r.lower && !/[a-z]/.test(pw)) out.push("lower");
        if (r.upper && !/[A-Z]/.test(pw)) out.push("upper");
        if (r.digit && !/[0-9]/.test(pw)) out.push("digit");
        if (r.symbol && !/[^a-zA-Z0-9]/.test(pw)) out.push("symbol");
        if (r.unique > 1 && new Set(pw).size < r.unique) out.push("unique");
        return out;
    }

    // A rough guess at how hard the password is to guess, led by length (the thing that matters
    // most), then marked down for using few kinds of character and for predictable shapes: a
    // capitalised word + digits + a symbol ("Summer2026!"), years, runs, keyboard / number
    // sequences and well-known passwords. 1 weak … 4 strong; never above 1 while a rule is unmet.
    function score(pw, failing) {
        if (!pw) return 0;
        if (failing) return 1;
        var s = pw.length >= 16 ? 4 : pw.length >= 12 ? 3 : pw.length >= 8 ? 2 : 1;
        var classes = [/[a-z]/, /[A-Z]/, /[0-9]/, /[^a-zA-Z0-9]/].filter(function (re) { return re.test(pw); }).length;
        var lower = pw.toLowerCase();
        if (classes < 3) s -= 1;
        if (/^[A-Z]?[a-z]+[0-9]+[^a-zA-Z0-9]*$/.test(pw)) s -= 1;
        if (/(19|20)[0-9]{2}/.test(pw)) s -= 1;
        if (/(.)\1\1/.test(pw)) s -= 1;
        if (/(abc|bcd|cde|123|234|345|456|567|678|789|890|qwe|wer|asd|zxc)/.test(lower)) s -= 1;
        if (COMMON.test(lower)) s -= 2;
        return Math.max(1, Math.min(4, s));
    }

    function input(field) { return field.querySelector("[data-password-input]"); }

    function confirmInput(field) {
        var id = field.dataset.confirm;
        return id ? document.getElementById(id) : null;
    }

    function update(field) {
        var box = input(field);
        if (!box) return;
        var pw = box.value || "";
        var r = rulesOf(field);
        var failing = unmet(pw, r);

        field.querySelectorAll("[data-rule]").forEach(function (li) {
            li.toggleAttribute("data-met", pw.length > 0 && failing.indexOf(li.dataset.rule) < 0);
        });

        var s = score(pw, failing.length > 0);
        var meter = field.querySelector("[data-password-meter]");
        if (meter) meter.dataset.score = String(s);
        var label = field.querySelector("[data-password-strength]");
        if (label) label.textContent = !pw ? "" : failing.length ? "Doesn't meet the rules yet" : LABELS[s];

        // Blank is left to the form (it may mean "keep the current password").
        box.setCustomValidity(pw && failing.length ? "This password doesn't meet the rules listed below it." : "");

        var copy = field.querySelector("[data-password-copy]");
        if (copy) copy.hidden = !pw;

        checkConfirm(field);
    }

    function checkConfirm(field) {
        var confirm = confirmInput(field);
        var box = input(field);
        if (!confirm || !box) return;
        confirm.setCustomValidity(confirm.value && confirm.value !== box.value ? "The passwords don't match." : "");
    }

    function randomIndex(n) {
        // Rejection sampling, so every character is equally likely.
        var buf = new Uint32Array(1);
        var limit = Math.floor(0x100000000 / n) * n;
        do { crypto.getRandomValues(buf); } while (buf[0] >= limit);
        return buf[0] % n;
    }

    function pick(chars) { return chars[randomIndex(chars.length)]; }

    function generate(r) {
        var sets = [LOWER, UPPER, DIGITS, r.symbols];
        // One from every class (whether required or not), the rest from all of them, shuffled.
        var chars = sets.map(pick);
        var all = sets.join("");
        while (chars.length < r.generateLength) chars.push(pick(all));
        for (var i = chars.length - 1; i > 0; i--) {
            var j = randomIndex(i + 1);
            var t = chars[i]; chars[i] = chars[j]; chars[j] = t;
        }
        var pw = chars.join("");
        return unmet(pw, r).length ? generate(r) : pw; // e.g. an unusually high unique-character rule
    }

    function setValue(box, value) {
        box.value = value;
        box.dispatchEvent(new Event("input", { bubbles: true }));
        box.dispatchEvent(new Event("change", { bubbles: true }));
    }

    function reveal(field, show) {
        var toggle = field.querySelector("[data-password-toggle]");
        var boxes = [input(field), confirmInput(field)].filter(Boolean);
        boxes.forEach(function (b) { b.type = show ? "text" : "password"; });
        if (toggle) {
            toggle.setAttribute("aria-pressed", show ? "true" : "false");
            toggle.setAttribute("aria-label", show ? "Hide password" : "Show password");
            toggle.title = show ? "Hide password" : "Show password";
        }
    }

    function copyText(text) {
        if (navigator.clipboard && navigator.clipboard.writeText) return navigator.clipboard.writeText(text);
        return new Promise(function (resolve) {
            var ta = document.createElement("textarea");
            ta.value = text;
            ta.style.position = "absolute";
            ta.style.left = "-9999px";
            document.body.appendChild(ta);
            ta.select();
            document.execCommand("copy");
            document.body.removeChild(ta);
            resolve();
        });
    }

    document.addEventListener("input", function (e) {
        var field = e.target.closest && e.target.closest("[data-password-field]");
        if (field) { update(field); return; }
        // The confirm field lives outside the component.
        document.querySelectorAll("[data-password-field][data-confirm]").forEach(function (f) {
            if (f.dataset.confirm === e.target.id) checkConfirm(f);
        });
    });

    document.addEventListener("click", function (e) {
        var btn = e.target.closest("[data-password-toggle], [data-password-generate], [data-password-copy]");
        if (!btn) return;
        var field = btn.closest("[data-password-field]");
        var box = field && input(field);
        if (!box) return;

        if (btn.hasAttribute("data-password-toggle")) {
            reveal(field, btn.getAttribute("aria-pressed") !== "true");
        } else if (btn.hasAttribute("data-password-generate")) {
            var pw = generate(rulesOf(field));
            box.readOnly = false; // no-autofill.js keeps it readonly until focused
            setValue(box, pw);
            var confirm = confirmInput(field);
            if (confirm) { confirm.readOnly = false; setValue(confirm, pw); }
            reveal(field, true); // a generated password is no use unless you can see it
            update(field);
        } else {
            copyText(box.value).then(function () {
                btn.setAttribute("data-copied", "");
                clearTimeout(btn._t);
                btn._t = setTimeout(function () { btn.removeAttribute("data-copied"); }, 1500);
            }, function () { /* clipboard blocked */ });
        }
    });

    // Fields can arrive with a value (a form re-rendered after a failed post), by a full load or
    // an enhanced navigation; Blazor's own event bus only exists once blazor.web.js has loaded.
    function init() { document.querySelectorAll("[data-password-field]").forEach(update); }
    document.addEventListener("DOMContentLoaded", init);
    document.addEventListener("enhancedload", init);
    window.addEventListener("load", function () {
        if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
            window.Blazor.addEventListener("enhancedload", init);
        }
    });
    if (document.readyState !== "loading") init();
})();
