// NQuad Browser: lists the kinds of entity, shows a schema-driven grid, and edits one entity with a form built from its schema.
// Everything it knows comes from /api/browser; it names no feature. The token never leaves this browser tab (sessionStorage).
(function () {
  'use strict';
  var typesEl = document.getElementById('types');
  var viewEl = document.getElementById('view');
  var tokenEl = document.getElementById('token');
  var whoEl = document.getElementById('who');
  var current = null;      // { key, title }
  var query = { page: 1, pageSize: 25, sort: null, descending: false };

  // A token copied from DevTools may carry the "Bearer " prefix, quotes or line breaks; keep only the three dot-separated parts.
  function clean(raw) { return (raw || '').replace(/^\s*"?bearer\s+/i, '').replace(/["'\s]/g, ''); }
  function token() { try { return sessionStorage.getItem('nqb.token') || ''; } catch (e) { return ''; } }
  function el(tag, attrs, children) {
    var node = document.createElement(tag);
    Object.keys(attrs || {}).forEach(function (k) {
      if (k === 'text') { node.textContent = attrs[k]; }
      else if (k.indexOf('on') === 0) { node.addEventListener(k.slice(2), attrs[k]); }
      else { node.setAttribute(k, attrs[k]); }
    });
    (children || []).forEach(function (c) { node.appendChild(c); });
    return node;
  }
  function api(method, path, body) {
    return fetch('/api/browser' + path, {
      method: method,
      headers: Object.assign({ 'Authorization': 'Bearer ' + token() }, body ? { 'Content-Type': 'application/json' } : {}),
      body: body ? JSON.stringify(body) : undefined
    }).then(function (r) {
      if (r.status === 401 || r.status === 403) { throw new Error('Not allowed: use a token of an Admin from the main API.'); }
      if (r.status === 204) { return null; }
      return r.text().then(function (t) {
        var data = null;
        try { data = t ? JSON.parse(t) : null; }
        catch (e) { throw new Error('The server answered ' + r.status + ' with text, not JSON: ' + t.slice(0, 400)); }
        if (!r.ok) { throw new Error((data && (data.detail || data.title || (data.errors && JSON.stringify(data.errors)))) || ('Request failed (' + r.status + ')')); }
        return data;
      });
    });
  }
  function show(node) { viewEl.replaceChildren(node); }
  function message(text, bad) { show(el('p', { 'class': 'msg' + (bad ? ' err' : ''), text: text })); }

  function loadTypes() {
    typesEl.replaceChildren();
    return api('GET', '/types').then(function (types) {
      whoEl.textContent = 'signed in';
      types.forEach(function (t) {
        typesEl.appendChild(el('button', { text: t.title, 'data-key': t.key, onclick: function () { open(t); } }));
      });
      if (!types.length) { message('The store has no schemas the browser knows.'); }
    }).catch(function (e) { whoEl.textContent = ''; message(e.message, true); });
  }

  function open(type) {
    current = type; query.page = 1; query.sort = null; query.descending = false;
    Array.prototype.forEach.call(typesEl.children, function (b) { b.classList.toggle('on', b.getAttribute('data-key') === type.key); });
    grid();
  }

  function grid() {
    var qs = '?page=' + query.page + '&pageSize=' + query.pageSize + (query.sort ? '&sort=' + encodeURIComponent(query.sort) + '&descending=' + query.descending : '');
    api('GET', '/' + current.key + qs).then(function (g) {
      var fields = g.schema.fields.filter(function (f) { return f.isListVisible; });
      var head = el('tr', {}, fields.map(function (f) {
        return el('th', { text: (f.label || f.name) + (query.sort === f.name ? (query.descending ? ' ▼' : ' ▲') : ''), onclick: function () {
          query.descending = query.sort === f.name ? !query.descending : false; query.sort = f.name; grid();
        } });
      }));
      var body = g.rows.map(function (row) {
        return el('tr', { 'class': 'row', onclick: function () { edit(row.entityId); } }, fields.map(function (f) {
          var shown = (row.display && row.display[f.name]) || row.values[f.name] || '';
          return el('td', { text: shown, title: shown });
        }));
      });
      var paging = g.paging || { page: 1, pageSize: 25, totalCount: g.rows.length };
      var pages = Math.max(1, Math.ceil(paging.totalCount / paging.pageSize));
      var bar = el('div', { 'class': 'bar' }, [
        el('strong', { text: current.title }),
        el('span', { 'class': 'msg', text: paging.totalCount + ' in total, page ' + paging.page + ' of ' + pages }),
        el('button', { 'class': 'act', text: 'Previous', onclick: function () { if (query.page > 1) { query.page--; grid(); } } }),
        el('button', { 'class': 'act', text: 'Next', onclick: function () { if (query.page < pages) { query.page++; grid(); } } }),
        el('button', { 'class': 'act primary', text: 'New', onclick: function () { edit(null); } })
      ]);
      show(el('div', {}, [bar, el('table', {}, [el('thead', {}, [head]), el('tbody', {}, body)])]));
    }).catch(function (e) { message(e.message, true); });
  }

  function control(field, value) {
    var common = { id: 'f-' + field.name, name: field.name };
    if (field.isReadOnly) { common.disabled = 'disabled'; }
    var type = (field.type || '').toLowerCase();
    var input;
    if (field.choices && field.choices.length) {
      input = el('select', common, [el('option', { value: '', text: '' })].concat(field.choices.map(function (c) { return el('option', { value: c, text: c }); })));
      input.value = value || '';
    } else if (type === 'bool') {
      input = el('select', common, [el('option', { value: '', text: '' }), el('option', { value: 'true', text: 'true' }), el('option', { value: 'false', text: 'false' })]);
      input.value = value || '';
    } else if (type === 'markdown' || type === 'text') {
      input = el('textarea', common); input.value = value || '';
    } else {
      input = el('input', Object.assign({ type: type === 'int' ? 'number' : 'text' }, common)); input.value = value || '';
    }
    return input;
  }

  function edit(id) {
    api('GET', '/' + current.key + '/' + (id ? encodeURIComponent(id) : 'new')).then(function (form) {
      var fields = form.schema.fields.slice().sort(function (a, b) { return a.order - b.order; });
      var inputs = {};
      var wrap = el('div', { 'class': 'form' });
      wrap.appendChild(el('div', { 'class': 'bar' }, [el('strong', { text: current.title + (id ? ' / ' + id : ' / new') })]));
      fields.forEach(function (f) {
        var value = form.entity.values[f.name];
        var input = control(f, value);
        inputs[f.name] = input;
        wrap.appendChild(el('label', { 'for': 'f-' + f.name, text: (f.label || f.name) + (f.isRequired ? ' *' : '') + (f.refSchema ? '  (id' + (f.type.indexOf('refs:') === 0 ? 's, comma separated' : '') + ')' : '') }));
        wrap.appendChild(input);
      });
      var status = el('p', { 'class': 'msg' });
      function collect() {
        var values = {};
        fields.forEach(function (f) { if (!f.isReadOnly) { values[f.name] = inputs[f.name].value === '' ? null : inputs[f.name].value; } });
        return { entityId: id || '', values: values };
      }
      var canUpdate = !form.permissions || form.permissions.canUpdate !== false;
      var buttons = [el('button', { 'class': 'act', text: 'Back', onclick: grid })];
      if (canUpdate) {
        buttons.push(el('button', { 'class': 'act primary', text: 'Save', onclick: function () {
          api(id ? 'PUT' : 'POST', '/' + current.key + (id ? '/' + encodeURIComponent(id) : ''), collect())
            .then(function () { status.className = 'msg'; status.textContent = 'Saved.'; if (!id) { grid(); } })
            .catch(function (e) { status.className = 'msg err'; status.textContent = e.message; });
        } }));
      }
      if (id && (!form.permissions || form.permissions.canDelete !== false)) {
        buttons.push(el('button', { 'class': 'act danger', text: 'Delete', onclick: function () {
          if (!window.confirm('Delete this entity? (It is tombstoned, not physically removed.)')) { return; }
          api('DELETE', '/' + current.key + '/' + encodeURIComponent(id)).then(grid).catch(function (e) { status.className = 'msg err'; status.textContent = e.message; });
        } }));
      }
      wrap.appendChild(el('div', { 'class': 'bar after' }, buttons));
      wrap.appendChild(status);
      show(wrap);
    }).catch(function (e) { message(e.message, true); });
  }

  document.getElementById('saveToken').addEventListener('click', function () {
    try { var cleaned = clean(tokenEl.value); tokenEl.value = cleaned; sessionStorage.setItem('nqb.token', cleaned); } catch (e) { /* storage blocked: the token stays in the field only */ }
    loadTypes();
  });
  if (token()) { tokenEl.value = token(); loadTypes(); }
  tokenEl.addEventListener('paste', function () { setTimeout(function () { tokenEl.value = clean(tokenEl.value); }, 0); });
})();
