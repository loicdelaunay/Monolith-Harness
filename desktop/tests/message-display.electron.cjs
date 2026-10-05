// Run with desktop/node_modules/electron/dist/electron.exe; no provider requests.
const { app, BrowserWindow } = require('electron');
const fs = require('node:fs/promises'), path = require('node:path'), { pathToFileURL } = require('node:url'), assert = require('node:assert/strict');
const output = path.resolve(__dirname, '../../.tmp/desktop-display-qa');
app.setPath('userData', path.join(output, 'profile'));
let win;
(async () => {
  await fs.mkdir(output, { recursive: true }); await app.whenReady();
  win = new BrowserWindow({ show: false, width: 1100, height: 1000, webPreferences: { contextIsolation: true, nodeIntegration: false, sandbox: true, backgroundThrottling: false } });
  let network = 0;
  win.webContents.session.webRequest.onBeforeRequest({ urls: ['http://*/*', 'https://*/*'] }, (_, reply) => { network++; reply({ cancel: true }); });
  const vendor = path.resolve(__dirname, '../ui/vendor/markdown');
  const resource = name => pathToFileURL(path.join(vendor, name)).href;
  const scripts = ['katex.min.js', 'auto-render.min.js', 'mermaid.min.js', 'visuals.js'].map(name => `<script src="${resource(name)}"></script>`).join('');
  const page = `<!doctype html><html lang="fr"><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src data:; connect-src 'none'"><link rel="stylesheet" href="${resource('katex.min.css')}"><link rel="stylesheet" href="${pathToFileURL(path.resolve(__dirname, '../ui/markdown.css')).href}"><style>body{background:#202020;color:#ededed;font:16px/1.6 'Segoe UI',sans-serif;padding:24px}.message{border:1px solid #555;border-radius:14px;background:#363636;padding:18px;margin:12px 0}.user{background:#344952}.role{font-size:12px;color:#ccc;margin-bottom:14px}</style></head><body><main id="messages"></main>${scripts}<script src="${pathToFileURL(path.resolve(__dirname, '../ui/activity.js')).href}"></script></body></html>`;
  const file = path.join(output, 'fixture.html'); await fs.writeFile(file, page); await win.loadFile(file);
  const execute = code => win.webContents.executeJavaScript(code);
  await execute(`var chatId=1; var snapshot={state:{showReasoningDetails:false}}; var histories=new Map();
    function $(id){return document.getElementById(id)} function L(fr,en){return fr}
    function el(tag,text,classes){const node=document.createElement(tag);if(text)node.textContent=text;if(classes)node.className=classes;return node}
    var fixture=[{id:1,role:'user',content:'Montre les étapes puis explique le résultat.'},{id:2,role:'assistant',content:'Je vérifie les données.',reasoning:'Première réflexion.'},{id:3,role:'tool',content:'read_source · données disponibles'},{id:4,role:'assistant',content:'Résultat final',reasoning:'Dernière réflexion : réponse prête.'}];
    histories.set(1,fixture);
    for(const message of fixture){const node=el('article',null,'message '+message.role);node.dataset.message=message.id;node.append(el('div',message.role.toUpperCase(),'role'));
      if(message.reasoning){const reason=el('div',message.reasoning,'reasoning-step');reason.dataset.reasoning=message.id;node.append(reason)}
      node.append(el('div',message.content,'body'));$('messages').append(node)}
    regroupActivity();`);
  const summary = await execute(`({count:document.querySelectorAll('.activity-group').length,open:document.querySelector('.activity-group').open,summary:document.querySelector('.activity-group>summary').textContent,finalVisible:document.querySelector('[data-message="4"]').parentElement.id==='messages'})`);
  assert.equal(summary.count, 1); assert.equal(summary.open, false); assert.equal(summary.finalVisible, true); assert.match(summary.summary, /Dernière réflexion/);
  await execute(`document.querySelector('.activity-group').open=true;`); await new Promise(resolve => setTimeout(resolve, 50));
  assert.equal(await execute(`regroupActivity();document.querySelector('.activity-group').open`), true);
  await execute(`snapshot.state.showReasoningDetails=true;regroupActivity();document.querySelector('.activity-group').open=false;`); await new Promise(resolve => setTimeout(resolve, 50));
  assert.equal(await execute(`regroupActivity();document.querySelector('.activity-group').open`), false);
  const html = '<h1>Résultat</h1><p>La réponse reste visible pendant que les étapes sont repliées.</p><h2>Points vérifiés</h2><ul><li>Espacement des paragraphes</li><li>Formules et schémas</li></ul><p>Une formule inline : <span class="math">\\(a^2+b^2=c^2\\)</span>.</p><div class="math">\\[\\frac{1}{2}+\\frac{1}{2}=1\\]</div><pre class="mermaid">flowchart LR\n A[Question] --> B[Réflexion]\n B --> C[Réponse]</pre>';
  await execute(`var body=document.querySelector('[data-message="4"] .body');body.innerHTML=${JSON.stringify(html)};markdownVisuals.render(body,true)`);
  assert.deepEqual(await execute(`({math:document.querySelectorAll('.katex').length,diagrams:document.querySelectorAll('.mermaid-diagram svg').length,errors:document.querySelectorAll('.visual-error').length})`), { math: 2, diagrams: 1, errors: 0 });
  await execute(`document.querySelector('.activity-group').open=false;new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))`); await new Promise(resolve=>setTimeout(resolve,150));
  await fs.writeFile(path.join(output, 'collapsed-math-mermaid.png'), (await win.webContents.capturePage()).toPNG());
  await execute(`document.querySelector('.activity-group').open=true`); await new Promise(resolve => setTimeout(resolve, 100));
  await fs.writeFile(path.join(output, 'expanded-math-mermaid.png'), (await win.webContents.capturePage()).toPNG());
  await execute(`var invalid=document.createElement('div');invalid.innerHTML='<pre class="mermaid">this is not a diagram</pre>';$('messages').append(invalid);markdownVisuals.render(invalid,true)`);
  assert.equal(await execute(`document.querySelectorAll('.visual-error').length`), 1);
  assert.equal(network, 0);
  await fs.writeFile(path.join(output, 'smoke-ok.txt'), 'Activity grouping, latest summary, preference and manual expansion, math, Mermaid, invalid diagram source and zero external requests passed.');
  win.destroy(); app.exit(0);
})().catch(async error => { await fs.mkdir(output, { recursive: true }); await fs.writeFile(path.join(output, 'smoke-error.txt'), error.stack); if (win) win.destroy(); app.exit(1); });
