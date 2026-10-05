const activityExpansion = new Map();
function regroupActivity() {
  const root = $('messages'), list = histories.get(chatId) || [];
  const nodes = new Map([...root.querySelectorAll('[data-message]')].map(node => [Number(node.dataset.message), node]));
  const reasons = new Map([...root.querySelectorAll('[data-reasoning]')].map(node => [Number(node.dataset.reasoning), node]));
  const groups = new Map([...root.querySelectorAll('.activity-group')].map(node => [node.dataset.turn, node]));
  const ordered = [], children = [...root.querySelectorAll('[data-child]')];
  let turn = 'initial', current = null;
  const preference = snapshot.state.showReasoningDetails === true;
  function group() {
    if (current) return current;
    const key = chatId + ':' + turn;
    let node = groups.get(key), saved = activityExpansion.get(key);
    if (!node) { node = el('details', null, 'message activity-group'); node.dataset.turn = key; }
    if (!saved || saved.preference !== preference) saved = { open: preference, preference };
    activityExpansion.set(key, saved); node.open = saved.open;
    node.ontoggle = () => activityExpansion.set(key, { open: node.open, preference });
    const summary = el('summary'), details = el('div', null, 'activity-details');
    node.replaceChildren(summary, details); ordered.push(node);
    current = { node, summary, details }; return current;
  }
  function add(node, text) {
    const activity = group(); activity.details.append(node);
    const lines = (text || '').split(/\r?\n/).map(x => x.trim()).filter(Boolean);
    activity.summary.textContent = (lines.at(-1) || L('Réflexion…', 'Thinking…')).slice(0, 240);
  }
  for (let index = 0; index < list.length; index++) {
    const message = list[index], node = nodes.get(message.id); if (!node) continue;
    if (message.role === 'user') { current = null; turn = message.id; ordered.push(node); continue; }
    if (message.role === 'tasks') { ordered.push(node); continue; }
    const reasoning = node.querySelector('[data-reasoning]') || reasons.get(message.id);
    if (reasoning && message.reasoning) add(reasoning, message.reasoning);
    let intermediate = message.role === 'tool';
    if (message.role === 'assistant') {
      for (const next of list.slice(index + 1)) {
        if (next.role === 'user') break;
        if (next.role === 'assistant' || next.role === 'tool') { intermediate = true; break; }
      }
    }
    if (intermediate) add(node, message.content); else ordered.push(node);
  }
  root.replaceChildren(...ordered, ...children);
}
