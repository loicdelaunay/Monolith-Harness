// Per-browser convenience only: the colour scheme. Failing storage must never break the page.
window.mhTheme = {
    get: () => { try { return localStorage.getItem('mh-theme'); } catch (e) { return null; } },
    set: value => { try { localStorage.setItem('mh-theme', value); } catch (e) { } }
};
window.mhScrollToEnd = id => { const el = document.getElementById(id); if (el) el.scrollTop = el.scrollHeight; };
