// Shared UI utilities: DOM building, formatting, modal/toast, prompts, charts.
const UI = (() => {
  const $ = (sel, root = document) => root.querySelector(sel);
  const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

  // Build a DOM element: el('div', {class:'x', onClick:fn}, child1, 'text', [..more]).
  function el(tag, attrs, ...kids) {
    const node = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs || {})) {
      if (v == null || v === false) continue;
      if (k === 'class') node.className = v;
      else if (k === 'html') node.innerHTML = v;
      else if (k === 'text') node.textContent = v;
      else if (k === 'dataset') Object.assign(node.dataset, v);
      else if (k.startsWith('on') && typeof v === 'function') node.addEventListener(k.slice(2).toLowerCase(), v);
      else node.setAttribute(k, v === true ? '' : v);
    }
    for (const kid of kids.flat()) {
      if (kid == null || kid === false) continue;
      node.appendChild(typeof kid === 'object' ? kid : document.createTextNode(String(kid)));
    }
    return node;
  }

  const esc = (s) => { const d = document.createElement('div'); d.textContent = s == null ? '' : String(s); return d.innerHTML; };

  let currency = 'PHP';
  const setCurrency = (c) => { currency = c || 'PHP'; };
  const money = (n) => currency + ' ' + Number(n || 0).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const qty = (n) => Number(n || 0).toLocaleString(undefined, { maximumFractionDigits: 3 });
  const dateTime = (s) => new Date(s).toLocaleString();
  const dateOnly = (s) => new Date(s).toLocaleDateString();

  const button = (label, classes, onClick) => {
    // String labels may contain icon markup (e.g. '<i class="bi bi-x"></i> Save'); render as HTML.
    // Array labels are treated as DOM children.
    if (Array.isArray(label)) return el('button', { class: 'btn ' + classes, type: 'button', onClick }, ...label);
    return el('button', { class: 'btn ' + classes, type: 'button', html: String(label), onClick });
  };

  // ---- Toast ----
  function toast(message, type = 'success') {
    const colors = { success: 'text-bg-success', danger: 'text-bg-danger', warning: 'text-bg-warning', info: 'text-bg-secondary' };
    const t = el('div', { class: `toast align-items-center border-0 ${colors[type] || colors.info}`, role: 'alert' },
      el('div', { class: 'd-flex' },
        el('div', { class: 'toast-body', text: message }),
        el('button', { class: 'btn-close btn-close-white me-2 m-auto', 'data-bs-dismiss': 'toast' })));
    $('#toast-host').appendChild(t);
    const bs = bootstrap.Toast.getOrCreateInstance(t, { delay: type === 'danger' ? 5000 : 2800 });
    bs.show();
    t.addEventListener('hidden.bs.toast', () => t.remove());
  }

  // ---- Modal ----
  let bsModal = null;
  function modal({ title, body, footer, size }) {
    $('#app-modal-title').textContent = title || '';
    const bodyEl = $('#app-modal-body'); bodyEl.innerHTML = '';
    bodyEl.append(typeof body === 'string' ? el('div', { html: body }) : body);
    const footEl = $('#app-modal-footer'); footEl.innerHTML = '';
    (footer || []).forEach((b) => footEl.appendChild(b));
    footEl.style.display = (footer && footer.length) ? '' : 'none';
    $('#app-modal-dialog').className = 'modal-dialog modal-dialog-centered modal-dialog-scrollable' + (size ? ' modal-' + size : '');
    bsModal = bootstrap.Modal.getOrCreateInstance($('#app-modal'));
    bsModal.show();
    return bsModal;
  }
  const closeModal = () => bsModal && bsModal.hide();

  // ---- Reason prompt (returns entered text, or null if cancelled) ----
  function promptReason({ title, label, placeholder, confirmText = 'Confirm', confirmClass = 'btn-primary', required = true, minLength }) {
    return new Promise((resolve) => {
      const input = el('textarea', { class: 'form-control', rows: 3, placeholder: placeholder || '' });
      const err = el('div', { class: 'invalid-feedback d-block text-danger small mt-1', text: '' });
      let done = false;
      const finish = (v) => { if (!done) { done = true; resolve(v); closeModal(); } };
      modal({
        title,
        body: el('div', {}, label ? el('label', { class: 'form-label', text: label }) : null, input, err),
        footer: [
          button('Cancel', 'btn-light', () => finish(null)),
          button(confirmText, confirmClass, () => {
            const v = input.value.trim();
            if (required && !v) { err.textContent = 'This field is required.'; input.classList.add('is-invalid'); return; }
            if (minLength && v.length < minLength) { err.textContent = `Must be at least ${minLength} characters.`; input.classList.add('is-invalid'); return; }
            finish(v);
          }),
        ],
      });
      $('#app-modal').addEventListener('hidden.bs.modal', () => finish(null), { once: true });
      setTimeout(() => input.focus(), 200);
    });
  }

  // ---- Spinner / empty / error blocks ----
  const spinner = (label = 'Loading…') => el('div', { class: 'text-center text-secondary py-5' },
    el('div', { class: 'spinner-border text-coffee', role: 'status' }), el('div', { class: 'mt-2 small', text: label }));
  const empty = (icon, text) => el('div', { class: 'empty-state' }, el('i', { class: 'bi ' + icon }), el('div', { text }));

  // ---- KPI stat card (shared by dashboard, inventory, menu performance) ----
  const statCard = (label, value, delta) => el('div', { class: 'stat-card' },
    el('div', { class: 'label', text: label }),
    el('div', { class: 'value', text: value }),
    delta ? el('div', { class: 'delta ' + (delta.dir || '') , text: delta.text }) : null);

  // ---- Chart.js helpers ----
  function lineChart(canvas, labels, data, label) {
    return new Chart(canvas, {
      type: 'line',
      data: { labels, datasets: [{ label, data, borderColor: '#1c4a39', backgroundColor: 'rgba(28,74,57,.12)', fill: true, tension: .3, pointRadius: 3, pointBackgroundColor: '#1c4a39' }] },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { y: { beginAtZero: true } } },
    });
  }

  function barChart(canvas, labels, data, label) {
    return new Chart(canvas, {
      type: 'bar',
      data: { labels, datasets: [{ label, data, backgroundColor: '#246048', borderRadius: 6, maxBarThickness: 46 }] },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { y: { beginAtZero: true } } },
    });
  }

  function doughnutChart(canvas, labels, data) {
    const palette = ['#1c4a39', '#246048', '#2f7256', '#c98a3d', '#ad7126', '#6a5d49', '#9aa1ad'];
    return new Chart(canvas, {
      type: 'doughnut',
      data: { labels, datasets: [{ data, backgroundColor: labels.map((_, i) => palette[i % palette.length]), borderWidth: 0 }] },
      options: { responsive: true, maintainAspectRatio: false, cutout: '62%', plugins: { legend: { position: 'right', labels: { boxWidth: 12, font: { size: 11 } } } } },
    });
  }

  // Page-level toolbar: title on the left, actions on the right.
  // viewToolbar('Title', actionEl, actionEl, ...) or viewToolbar({ title, subtitle }, ...actions).
  function viewToolbar(arg, ...actions) {
    const opts = typeof arg === 'string' ? { title: arg } : (arg || {});
    // Title is now shown in the topbar — just render action buttons
    const right = el('div', { class: 'view-toolbar-actions' }, ...actions);
    return el('div', { class: 'view-toolbar' }, right);
  }

  return { $, $$, el, esc, setCurrency, money, qty, dateTime, dateOnly, button, toast, modal, closeModal, promptReason, spinner, empty, statCard, lineChart, barChart, doughnutChart, viewToolbar };
})();
