(function () {
  let diagramId = 0, requestId = 0, queue = Promise.resolve(), cacheSize = 0;
  const cache = new Map(), pending = new Map(), requests = new Map(), versions = new WeakMap(), documents = new WeakMap();
  const MAX_ENTRIES = 64, MAX_CHARS = 4 * 1024 * 1024;
  const options = { delimiters: [{ left: '$$', right: '$$', display: true }, { left: '\\[', right: '\\]', display: true },
    { left: '\\(', right: '\\)', display: false }], throwOnError: false, trust: false, strict: 'warn', maxExpand: 1000 };
  const key = (source, dark) => (dark ? 'dark\n' : 'light\n') + source;
  function remember(source, dark, svg) {
    if (typeof svg !== 'string' || svg.length > MAX_CHARS) return;
    const id = key(source, dark); if (cache.has(id)) { cacheSize -= cache.get(id).length; cache.delete(id); }
    cache.set(id, svg); cacheSize += svg.length;
    while (cache.size > MAX_ENTRIES || cacheSize > MAX_CHARS) { const first = cache.keys().next().value; cacheSize -= cache.get(first).length; cache.delete(first); }
  }
  function read(source, dark) {
    const id = key(source, dark), svg = cache.get(id);
    if (svg !== undefined) { cache.delete(id); cache.set(id, svg); }
    return svg;
  }
  function fromHost(source, dark) {
    if (!window.chrome?.webview?.postMessage) return Promise.resolve(null);
    return new Promise(resolve => {
      const id = ++requestId, timer = setTimeout(() => { requests.delete(id); resolve(null); }, 1200);
      requests.set(id, svg => { clearTimeout(timer); resolve(svg); });
      window.chrome.webview.postMessage({ kind: 'diagram-cache-get', id, source, dark });
    });
  }
  async function diagramSvg(source, dark) {
    const cached = read(source, dark); if (cached !== undefined) return cached;
    const id = key(source, dark); if (pending.has(id)) return pending.get(id);
    const work = (async () => {
      let svg = await fromHost(source, dark);
      if (typeof svg !== 'string') {
        const render = queue.catch(() => {}).then(async () => {
          mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: dark ? 'dark' : 'default', maxTextSize: 50000,
            suppressErrorRendering: true, flowchart: { htmlLabels: false }, secure: ['securityLevel', 'startOnLoad', 'maxTextSize', 'suppressErrorRendering'] });
          return (await mermaid.render('mh-diagram-' + (++diagramId), source)).svg;
        });
        queue = render; svg = await render;
        window.chrome?.webview?.postMessage({ kind: 'diagram-cache-put', source, dark, svg });
      }
      remember(source, dark, svg); return svg;
    })();
    pending.set(id, work);
    try { return await work; } finally { pending.delete(id); }
  }
  function insertSvg(pre, svg) {
    const diagram = document.createElement('div'); diagram.className = 'mermaid-diagram'; diagram.innerHTML = svg;
    // Cached SVGs can appear more than once in a document. Give every ID and reference a unique namespace.
    const ids = new Map(), prefix = 'mh-copy-' + (++diagramId) + '-';
    for (const node of diagram.querySelectorAll('[id]')) ids.set(node.id, prefix + ids.size);
    for (const node of diagram.querySelectorAll('*')) {
      for (const attribute of [...node.attributes]) {
        let value = attribute.value;
        if (attribute.name === 'id') value = ids.get(value) || value;
        else {
          value = value.replace(/url\(\s*(['"]?)#([^\s)'"]+)\1\s*\)/g, (whole, quote, id) => ids.has(id) ? 'url(#' + ids.get(id) + ')' : whole);
          if ((attribute.name === 'href' || attribute.name === 'xlink:href') && value.startsWith('#')) value = '#' + (ids.get(value.slice(1)) || value.slice(1));
          if (attribute.name === 'aria-labelledby' || attribute.name === 'aria-describedby') value = value.split(' ').map(id => ids.get(id) || id).join(' ');
        }
        if (value !== attribute.value) node.setAttribute(attribute.name, value);
      }
      if (node.tagName.toLowerCase() === 'style') node.textContent = node.textContent.replace(/#([\w:-]+)/g, (whole, id) => ids.has(id) ? '#' + ids.get(id) : whole);
    }
    pre.replaceWith(diagram);
  }
  async function render(root, dark, enabled = {}) {
    if (dark === undefined) dark = (getComputedStyle(root).color.match(/\d+/g) || []).slice(0, 3).reduce((sum, value) => sum + Number(value), 0) > 400;
    const version = (versions.get(root) || 0) + 1; versions.set(root, version);
    if (enabled.math !== false) renderMathInElement(root, options);
    if (enabled.mermaid === false) return;
    for (const code of root.querySelectorAll('pre > code.language-mermaid, pre.mermaid')) {
      const valid = () => versions.get(root) === version && root.contains(code) && (!root.isConnected || code.isConnected);
      if (!valid()) return;
      const source = code.textContent.trim(), pre = code.tagName === 'PRE' ? code : code.parentElement;
      try {
        if (source.length > 50000) throw new Error('Diagram too large');
        let svg = read(source, dark);
        if (svg === undefined) {
          // Coalesce rapidly changing streamed source before scheduling Mermaid's expensive layout.
          await new Promise(resolve => setTimeout(resolve, 100)); if (!valid()) return;
          svg = await diagramSvg(source, dark);
        }
        if (valid()) insertSvg(pre, svg);
      } catch (error) {
        if (!valid()) return;
        const message = document.createElement('div'); message.className = 'visual-error';
        message.textContent = document.documentElement.lang === 'fr' ? 'Diagramme incomplet ou invalide — source affichée.' : 'Incomplete or invalid diagram — source shown.';
        pre.before(message);
      }
    }
  }
  let searchQuery = '';
  function search(query) {
    searchQuery = String(query || '');
    const root = document.getElementById('markdown'); if (!root) return;
    const modern = typeof Highlight !== 'undefined' && typeof CSS !== 'undefined' && CSS.highlights;
    if (modern) CSS.highlights.delete('monolith-search');
    for (const mark of root.querySelectorAll('mark.monolith-search')) mark.replaceWith(...mark.childNodes);
    if (!searchQuery) return;
    const nodes = [], offsets = [], walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
      acceptNode(node) { return node.parentElement?.closest('script,style,.katex-mathml,defs,title,desc') ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT; }
    });
    let text = '', node;
    while ((node = walker.nextNode())) { offsets.push(text.length); nodes.push(node); text += node.textContent; }
    const escaped = searchQuery.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const matches = [...text.matchAll(new RegExp(escaped, 'giu'))], ranges = [];
    for (const match of matches) {
      const start = match.index, end = start + match[0].length;
      let first = 0, last = 0;
      while (first + 1 < nodes.length && offsets[first + 1] <= start) first++;
      last = first; while (last + 1 < nodes.length && offsets[last + 1] < end) last++;
      if (!nodes[first] || !nodes[last]) continue;
      const range = document.createRange(); range.setStart(nodes[first], start - offsets[first]); range.setEnd(nodes[last], end - offsets[last]); ranges.push(range);
    }
    if (modern) CSS.highlights.set('monolith-search', new Highlight(...ranges));
    else {
      // Preserve links, emphasis and diagram DOM even on WebKit versions without CSS highlights.
      for (let i = nodes.length - 1; i >= 0; i--) {
        const node = nodes[i], beginning = offsets[i], finish = beginning + node.textContent.length;
        const pieces = matches.map(m => [Math.max(0, m.index - beginning), Math.min(finish, m.index + m[0].length) - beginning]).filter(([a,b]) => b > a);
        for (let j = pieces.length - 1; j >= 0; j--) {
          const [a,b] = pieces[j], tail = node.splitText(b), target = node.splitText(a);
          const mark = document.createElement('mark'); mark.className = 'monolith-search'; target.replaceWith(mark); mark.append(target);
        }
      }
    }
  }
  let reportedHeight = -1, sizeQueued = false;
  function reportSize() {
    if (sizeQueued) return; sizeQueued = true;
    requestAnimationFrame(() => {
      sizeQueued = false; const root = document.getElementById('markdown'); if (!root) return;
      const height = Math.ceil(root.getBoundingClientRect().height) + 8;
      if (height !== reportedHeight) { reportedHeight = height; window.chrome?.webview?.postMessage({ kind: 'size', height }); }
    });
  }
  mermaid.initialize({ startOnLoad: false, securityLevel: 'strict' });
  window.markdownVisuals = { render, search, resolveCache(id, svg) { const resolve = requests.get(id); if (resolve) { requests.delete(id); resolve(svg); } },
    async set(html, colors, fontSize, lineHeight, enabled = {}) {
      const root = document.getElementById('markdown');
      const signature = JSON.stringify([colors, fontSize, lineHeight, enabled, document.documentElement.lang]);
      const previous = documents.get(root);
      if (previous?.html === html && previous.signature === signature) { reportSize(); return; }
      documents.set(root, { html, signature });
      document.body.style.color = colors.text; document.body.style.background = colors.background;
      document.body.style.fontSize = fontSize + 'px'; document.body.style.lineHeight = lineHeight + 'px';
      root.innerHTML = html; await render(root, colors.dark, enabled); search(searchQuery); reportSize();
    }
  };
  document.addEventListener('selectionchange', () => { const selection = window.getSelection(); window.chrome?.webview?.postMessage({ kind: 'selection', selected: !!selection && !selection.isCollapsed }); });
  document.addEventListener('click', event => {
    const link = event.target.closest('a'); if (!link) return;
    if (document.getElementById('markdown')) { event.preventDefault(); window.chrome?.webview?.postMessage({ kind: 'link', url: link.getAttribute('href') }); }
  });
  if (typeof ResizeObserver !== 'undefined') new ResizeObserver(reportSize).observe(document.body);
})();
