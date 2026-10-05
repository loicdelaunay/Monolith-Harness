(function () {
  let diagramId = 0;
  const options = { delimiters: [{ left: '$$', right: '$$', display: true }, { left: '\\[', right: '\\]', display: true },
    { left: '\\(', right: '\\)', display: false }], throwOnError: false, trust: false, strict: 'warn', maxExpand: 1000 };
  async function render(root, dark) {
    if (dark === undefined) dark = (getComputedStyle(root).color.match(/\d+/g) || []).slice(0, 3).reduce((sum, value) => sum + Number(value), 0) > 400;
    renderMathInElement(root, options);
    mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: dark ? 'dark' : 'default', maxTextSize: 50000,
      suppressErrorRendering: true, flowchart: { htmlLabels: false }, secure: ['securityLevel', 'startOnLoad', 'maxTextSize', 'suppressErrorRendering'] });
    for (const code of root.querySelectorAll('pre > code.language-mermaid, pre.mermaid')) {
      if (!code.isConnected && root.isConnected) break;
      const source = code.textContent, pre = code.tagName === 'PRE' ? code : code.parentElement;
      const diagram = document.createElement('div'); diagram.className = 'mermaid-diagram';
      try {
        if (source.length > 50000) throw new Error('Diagram too large');
        const result = await mermaid.render('mh-diagram-' + (++diagramId), source);
        diagram.innerHTML = result.svg; pre.replaceWith(diagram);
      } catch (error) {
        // Keep malformed or unfinished streamed source readable and available to copy.
        const message = document.createElement('div'); message.className = 'visual-error';
        message.textContent = document.documentElement.lang === 'fr' ? 'Diagramme incomplet ou invalide — source affichée.' : 'Incomplete or invalid diagram — source shown.';
        pre.before(message);
      }
    }
  }
  mermaid.initialize({ startOnLoad: false, securityLevel: 'strict' });
  window.markdownVisuals = { render, async set(html, colors, fontSize, lineHeight) {
    document.body.style.color = colors.text; document.body.style.background = colors.background;
    document.body.style.fontSize = fontSize + 'px'; document.body.style.lineHeight = lineHeight + 'px';
    const root = document.getElementById('markdown'); root.innerHTML = html;
    await render(root, colors.dark);
    window.chrome?.webview?.postMessage({ kind: 'size', height: Math.ceil(root.getBoundingClientRect().height) + 8 });
  } };
  document.addEventListener('click', event => {
    const link = event.target.closest('a'); if (!link) return;
    if (document.getElementById('markdown')) { event.preventDefault(); window.chrome?.webview?.postMessage({ kind: 'link', url: link.getAttribute('href') }); }
  });
  if (typeof ResizeObserver !== 'undefined') new ResizeObserver(() => {
    const root = document.getElementById('markdown'); if (root) window.chrome?.webview?.postMessage({ kind: 'size', height: Math.ceil(root.getBoundingClientRect().height) + 8 });
  }).observe(document.body);
})();
