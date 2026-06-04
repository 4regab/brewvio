window.Views = window.Views || {};
(function () {
  const { el, money, qty, esc, button, toast, modal, closeModal, promptReason, spinner, empty, dateTime } = UI;

  let menu = [], modifiers = [], cart = [], discount = 0, activeCat = 'All', seq = 1;
  let cartItemsEl, totalsEl, chargeBtn, cancelBtn;

  // ----- Product imagery -----
  // Figma supplied 6 photos; the rest are dedicated per-product photos so every
  // item shows the correct drink/food instead of a recycled look-alike.
  const IMG_BASE = 'img/';
  // Exact match on the seeded menu item names (see DatabaseInitializer.cs).
  const IMAGE_BY_NAME = {
    'espresso':          'espresso.png',
    'americano':         'americano.png',
    'cappuccino':        'cappuccino.png',
    'caffe latte':       'latte.png',
    'flat white':        'flat-white.png',
    'caramel macchiato': 'macchiato.png',
    'cafe mocha':        'mocha.png',
    'iced latte':        'iced-latte.png',
    'brewed coffee':     'americano.png',
    'matcha latte':      'matcha-latte.png',
    'hot chocolate':     'hot-chocolate.png',
    'iced tea':          'iced-tea.png',
    // extra coverage for common items a manager might add later
    'latte':             'latte.png',
    'macchiato':         'macchiato.png',
    'fruit soda':        'fruit-soda.png',
    'donut':             'donut.png',
    'croissant':         'croissant.png',
  };
  // Resolve a menu item to a product photo: exact name first, then keyword,
  // then a sensible per-category fallback.
  function menuImage(item) {
    const n = (item.name || '').trim().toLowerCase();
    if (IMAGE_BY_NAME[n]) return IMG_BASE + IMAGE_BY_NAME[n];
    if (n.includes('matcha')) return IMG_BASE + 'matcha-latte.png';
    if (n.includes('hot choc')) return IMG_BASE + 'hot-chocolate.png';
    if (n.includes('mocha')) return IMG_BASE + 'mocha.png';
    if (n.includes('cappuccino')) return IMG_BASE + 'cappuccino.png';
    if (n.includes('macchiato')) return IMG_BASE + 'macchiato.png';
    if (n.includes('flat white')) return IMG_BASE + 'flat-white.png';
    if (n.includes('espresso')) return IMG_BASE + 'espresso.png';
    if (n.includes('iced') && n.includes('latte')) return IMG_BASE + 'iced-latte.png';
    if (n.includes('latte')) return IMG_BASE + 'latte.png';
    if (n.includes('americano') || n.includes('brewed') || n.includes('coffee')) return IMG_BASE + 'americano.png';
    if (n.includes('tea')) return IMG_BASE + 'iced-tea.png';
    if (n.includes('soda') || n.includes('juice') || n.includes('lemon')) return IMG_BASE + 'fruit-soda.png';
    if (n.includes('donut') || n.includes('doughnut')) return IMG_BASE + 'donut.png';
    if (n.includes('croissant') || n.includes('pastry')) return IMG_BASE + 'croissant.png';
    // category fallback
    const c = (item.category || '').toLowerCase();
    if (c.includes('cold')) return IMG_BASE + 'iced-latte.png';
    if (c.includes('tea')) return IMG_BASE + 'iced-tea.png';
    if (c.includes('non-coffee')) return IMG_BASE + 'matcha-latte.png';
    if (c.includes('espresso') || c.includes('coffee')) return IMG_BASE + 'americano.png';
    if (c.includes('food') || c.includes('pastry')) return IMG_BASE + 'croissant.png';
    return IMG_BASE + 'latte.png';
  }

  const taxRate = () => Number((window.App.store && window.App.store.taxRatePercent) || 0);
  const lineUnit = (l) => l.price + l.modifiers.reduce((s, m) => s + m.priceDelta, 0);
  const lineTotal = (l) => lineUnit(l) * l.quantity;
  const keyOf = (id, mods) => id + ':' + mods.map((m) => m.id).sort((a, b) => a - b).join(',');

  function totals() {
    const subtotal = cart.reduce((s, l) => s + lineTotal(l), 0);
    const disc = Math.min(discount || 0, subtotal);
    const tax = Math.round((subtotal - disc) * taxRate()) / 100;
    return { subtotal, disc, tax, total: subtotal - disc + tax };
  }

  function addToCart(item) {
    const k = keyOf(item.id, []);
    const existing = cart.find((l) => l.key === k);
    if (existing) existing.quantity++;
    else cart.push({ uid: seq++, key: k, menuItemId: item.id, name: item.name, price: item.price, quantity: 1, modifiers: [] });
    renderCart();
  }

  function customize(line) {
    const groups = {};
    modifiers.forEach((m) => (groups[m.groupName] = groups[m.groupName] || []).push(m));
    const chosen = new Set(line.modifiers.map((m) => m.id));
    const body = el('div', {},
      Object.entries(groups).map(([g, mods]) => el('div', { class: 'mb-3' },
        el('div', { class: 'fw-semibold mb-1', text: g }),
        mods.map((m) => el('label', { class: 'form-check d-flex align-items-center gap-2' },
          el('input', { class: 'form-check-input mt-0', type: 'checkbox', checked: chosen.has(m.id), onChange: (e) => e.target.checked ? chosen.add(m.id) : chosen.delete(m.id) }),
          el('span', { class: 'form-check-label', html: `${esc(m.name)} <span class="text-secondary">(${m.priceDelta >= 0 ? '+' : ''}${money(m.priceDelta)})</span>` }))))));
    modal({
      title: 'Customize — ' + line.name, body,
      footer: [button('Cancel', 'btn-light', closeModal), button('Apply', 'btn-primary', () => {
        line.modifiers = modifiers.filter((m) => chosen.has(m.id)).map((m) => ({ id: m.id, name: m.name, priceDelta: m.priceDelta }));
        line.key = keyOf(line.menuItemId, line.modifiers);
        closeModal(); renderCart();
      })],
    });
  }

  function renderMenu(grid) {
    const cats = ['All', ...Array.from(new Set(menu.map((m) => m.category)))];
    const tabs = grid.parentElement.querySelector('.cat-tabs');
    tabs.innerHTML = '';
    cats.forEach((c) => tabs.appendChild(el('button', { class: 'cat-tab' + (c === activeCat ? ' active' : ''), text: c, onClick: () => { activeCat = c; renderMenu(grid); } })));
    grid.innerHTML = '';
    const items = menu.filter((m) => activeCat === 'All' || m.category === activeCat);
    if (!items.length) { grid.appendChild(empty('bi-cup', 'No items in this category.')); return; }
    items.forEach((m) => grid.appendChild(el('button', { class: 'menu-tile', onClick: () => addToCart(m) },
      el('div', { class: 'menu-tile-photo' }, el('img', { src: menuImage(m), alt: m.name, loading: 'lazy' })),
      el('div', { class: 'menu-tile-body' },
        el('div', { class: 'name', text: m.name }),
        el('div', { class: 'price', text: money(m.price) })))));
  }

  function renderCart() {
    cartItemsEl.innerHTML = '';
    if (!cart.length) cartItemsEl.appendChild(empty('bi-basket', 'Cart is empty. Tap items to add.'));
    cart.forEach((l) => {
      const row = el('div', { class: 'cart-line' },
        el('div', { class: 'ttl' }, el('div', { class: 'fw-semibold', text: l.name }), el('div', { class: 'fw-semibold', text: money(lineTotal(l)) })),
        l.modifiers.length ? el('div', { class: 'text-secondary small', text: l.modifiers.map((m) => m.name).join(', ') }) : null,
        el('div', { class: 'd-flex align-items-center justify-content-between mt-2' },
          el('div', { class: 'qty-stepper' },
            button('−', '', () => { l.quantity > 1 ? l.quantity-- : (cart = cart.filter((x) => x.uid !== l.uid)); renderCart(); }),
            el('span', { text: l.quantity }),
            button('+', '', () => { l.quantity++; renderCart(); })),
          el('div', { class: 'd-flex gap-2' },
            modifiers.length ? button('<i class="bi bi-sliders"></i>', 'btn-sm btn-light', () => customize(l)) : null,
            button('<i class="bi bi-trash"></i>', 'btn-sm btn-outline-danger', () => { cart = cart.filter((x) => x.uid !== l.uid); renderCart(); }))));
      cartItemsEl.appendChild(row);
    });
    renderTotals();
  }

  function renderTotals() {
    const t = totals();
    totalsEl.innerHTML = '';
    const line = (k, v, strong) => el('div', { class: 'd-flex justify-content-between ' + (strong ? 'fs-5 fw-bold' : 'text-secondary') }, el('span', { text: k }), el('span', { text: v }));
    totalsEl.append(
      line('Subtotal', money(t.subtotal)),
      el('div', { class: 'd-flex justify-content-between align-items-center my-1' },
        el('span', { class: 'text-secondary', text: 'Discount' }),
        el('input', { type: 'number', min: '0', step: '0.01', value: discount || '', class: 'form-control form-control-sm', style: 'width:120px;text-align:right',
          onInput: (e) => { discount = Math.max(0, Number(e.target.value) || 0); const tt = totals(); totalsEl.querySelector('.tax-val').textContent = money(tt.tax); totalsEl.querySelector('.total-val').textContent = money(tt.total); } })),
      el('div', { class: 'd-flex justify-content-between text-secondary' }, el('span', { text: `Tax (${taxRate()}%)` }), el('span', { class: 'tax-val', text: money(t.tax) })),
      el('hr', { class: 'my-2' }),
      el('div', { class: 'd-flex justify-content-between fs-5 fw-bold text-coffee' }, el('span', { text: 'Total' }), el('span', { class: 'total-val', text: money(t.total) })));
    const enabled = cart.length > 0;
    chargeBtn.disabled = !enabled; cancelBtn.disabled = !enabled;
  }

  function openPayment() {
    const t = totals();
    let method = 'Cash';
    const cashIn = el('input', { type: 'number', min: '0', step: '0.01', class: 'form-control form-control-lg', value: t.total.toFixed(2) });
    const cardIn = el('input', { type: 'number', min: '0', step: '0.01', class: 'form-control form-control-lg', value: '0.00' });
    const changeRow = el('div', { class: 'd-flex justify-content-between fs-5 mt-3' }, el('span', { text: 'Change' }), el('span', { class: 'fw-bold change-val', text: money(0) }));
    const cashWrap = el('div', { class: 'mb-2' }, el('label', { class: 'form-label', text: 'Cash tendered' }), cashIn);
    const cardWrap = el('div', { class: 'mb-2 d-none' }, el('label', { class: 'form-label', text: 'Card amount' }), cardIn);

    function recalc() {
      const cash = Number(cashIn.value) || 0, card = Number(cardIn.value) || 0;
      const paid = (method === 'Card' ? 0 : cash) + (method === 'Cash' ? 0 : card);
      changeRow.querySelector('.change-val').textContent = money(Math.max(0, paid - t.total));
    }
    cashIn.addEventListener('input', recalc); cardIn.addEventListener('input', recalc);

    const seg = el('div', { class: 'btn-group w-100 mb-3' });
    ['Cash', 'Card', 'Split'].forEach((m, i) => seg.appendChild(el('button', { class: 'btn ' + (i === 0 ? 'btn-primary' : 'btn-outline-secondary'), text: m, onClick: () => {
      method = m; Array.from(seg.children).forEach((b) => b.className = 'btn ' + (b.textContent === m ? 'btn-primary' : 'btn-outline-secondary'));
      cashWrap.classList.toggle('d-none', m === 'Card');
      cardWrap.classList.toggle('d-none', m === 'Cash');
      if (m === 'Card') cardIn.value = t.total.toFixed(2);
      if (m === 'Cash') cashIn.value = t.total.toFixed(2);
      if (m === 'Split') { cashIn.value = '0.00'; cardIn.value = '0.00'; }
      recalc();
    } })));

    const confirm = button('Confirm payment', 'btn-primary', async () => {
      const cash = Number(cashIn.value) || 0, card = Number(cardIn.value) || 0;
      const payments = [];
      if (method !== 'Card' && cash > 0) payments.push({ method: 'Cash', amount: cash });
      if (method !== 'Cash' && card > 0) payments.push({ method: 'Card', amount: card });
      if (!payments.length) { toast('Enter a payment amount.', 'warning'); return; }
      confirm.disabled = true;
      try {
        const receipt = await Api.post('/api/orders', { items: cart.map((l) => ({ menuItemId: l.menuItemId, quantity: l.quantity, modifierIds: l.modifiers.map((m) => m.id), notes: null })), discountAmount: discount || 0, payments });
        cart = []; discount = 0; renderCart();
        (receipt.stockWarnings || []).forEach((w) => toast(w, 'warning'));
        showReceipt(receipt, true);   // swap modal content in place (avoids hide/show race)
      } catch (e) { toast(e.message, 'danger'); confirm.disabled = false; }
    });

    modal({ title: 'Payment — ' + money(t.total), body: el('div', {}, seg, cashWrap, cardWrap, changeRow), footer: [button('Cancel', 'btn-light', closeModal), confirm] });
    recalc();
  }

  function receiptNode(r) {
    const store = window.App.store || {};
    const row = (a, b, strong) => el('div', { class: 'row2' }, el(strong ? 'b' : 'span', { text: a }), el(strong ? 'b' : 'span', { text: b }));
    return el('div', { class: 'receipt' },
      el('h4', { text: store.storeName || 'Brewvio Coffee' }),
      store.address ? el('div', { class: 'center', text: store.address }) : null,
      el('hr'),
      row('Receipt #', String(r.transactionId)), row('Date', dateTime(r.timestamp)), row('Cashier', r.cashier),
      r.status !== 'Completed' ? el('div', { class: 'center', html: `<b>** ${esc(r.status.toUpperCase())} **</b>` }) : null,
      el('hr'),
      ...r.items.map((it) => el('div', {}, row(`${it.quantity}x ${it.name}`, money(it.lineTotal)), it.modifiers ? el('div', { style: 'font-size:11px', text: '   + ' + it.modifiers }) : null)),
      el('hr'),
      row('Subtotal', money(r.subtotal)),
      r.discountAmount > 0 ? row('Discount', '-' + money(r.discountAmount)) : null,
      row('Tax', money(r.taxAmount)),
      row('TOTAL', money(r.totalAmount), true),
      el('hr'),
      ...r.payments.map((p) => row(p.method, money(p.amount))),
      r.change > 0 ? row('Change', money(r.change)) : null,
      el('hr'),
      el('div', { class: 'center', text: 'Thank you! Please come again.' }));
  }

  function showReceipt(r, isNew) {
    modal({
      title: isNew ? 'Order complete' : 'Receipt #' + r.transactionId,
      body: el('div', { class: 'py-2' }, receiptNode(r)),
      footer: [button('Close', 'btn-light', closeModal), button('<i class="bi bi-printer"></i> Print', 'btn-primary', () => printReceipt(r))],
    });
  }

  function printReceipt(r) {
    const area = document.getElementById('print-area');
    area.innerHTML = '';
    area.appendChild(receiptNode(r));
    window.print();
  }

  Views.pos = {
    render: async (root) => {
      root.appendChild(spinner('Loading menu…'));
      try { [menu, modifiers] = await Promise.all([Api.get('/api/menu'), Api.get('/api/menu/modifiers')]); }
      catch (e) { root.innerHTML = ''; root.appendChild(empty('bi-exclamation-triangle', e.message)); return; }
      root.innerHTML = '';

      const grid = el('div', { class: 'menu-grid' });
      const left = el('div', {}, el('div', { class: 'cat-tabs' }), grid);

      cartItemsEl = el('div', { class: 'cart-items' });
      totalsEl = el('div', { class: 'p-3 border-top' });
      chargeBtn = button('<i class="bi bi-credit-card"></i> Charge', 'btn-primary btn-lg w-100', openPayment);
      cancelBtn = button('Cancel order', 'btn-outline-danger w-100 mt-2', async () => {
        const reason = await promptReason({ title: 'Cancel order', label: 'Reason for cancellation', confirmText: 'Cancel order', confirmClass: 'btn-danger' });
        if (reason == null) return;
        try { await Api.post('/api/orders/cancel', { reason }); cart = []; discount = 0; renderCart(); toast('Order cancelled.'); }
        catch (e) { toast(e.message, 'danger'); }
      });
      const cartPanel = el('div', { class: 'cart-panel' },
        el('div', { class: 'p-3 border-bottom fw-semibold d-flex align-items-center gap-2' }, el('i', { class: 'bi bi-basket text-coffee' }), 'Current Order'),
        cartItemsEl, totalsEl, el('div', { class: 'p-3 pt-0' }, chargeBtn, cancelBtn));

      root.appendChild(el('div', { class: 'pos-layout' }, left, cartPanel));
      renderMenu(grid); renderCart();
    },
  };

  Views.transactions = {
    render: async (root) => {
      root.appendChild(spinner());
      let list;
      try { list = await Api.get('/api/orders/recent?take=100'); }
      catch (e) { root.innerHTML = ''; root.appendChild(empty('bi-exclamation-triangle', e.message)); return; }
      root.innerHTML = '';
      const badge = (s) => `<span class="badge ${s === 'Completed' ? 'badge-soft-success' : s === 'Refunded' ? 'badge-soft-danger' : 'badge-soft-muted'}">${esc(s)}</span>`;
      const tbody = el('tbody');
      if (!list.length) tbody.appendChild(el('tr', {}, el('td', { colspan: 8 }, empty('bi-receipt', 'No transactions yet.'))));
      list.forEach((tx) => {
        const actions = el('div', { class: 'd-flex gap-2 justify-content-end' },
          button('Receipt', 'btn-sm btn-light', async () => { try { showReceipt(await Api.get('/api/orders/' + tx.id)); } catch (e) { toast(e.message, 'danger'); } }),
          tx.status === 'Completed' ? button('Refund', 'btn-sm btn-outline-danger', async () => {
            const reason = await promptReason({ title: 'Refund #' + tx.id, label: 'Reason for refund', confirmText: 'Refund', confirmClass: 'btn-danger' });
            if (reason == null) return;
            try { await Api.post(`/api/orders/${tx.id}/refund`, { reason }); toast('Refunded. Stock restored.'); Views.transactions.render(root); }
            catch (e) { toast(e.message, 'danger'); }
          }) : null);
        tbody.appendChild(el('tr', {},
          el('td', { text: '#' + tx.id }), el('td', { text: dateTime(tx.timestamp) }),
          el('td', { text: tx.cashier }), el('td', { text: tx.itemCount }),
          el('td', { text: tx.paymentMethod }), el('td', { html: badge(tx.status) }),
          el('td', { class: 'text-end fw-semibold', text: money(tx.totalAmount) }),
          el('td', { class: 'text-end' }, actions)));
      });
      root.appendChild(el('div', { class: 'section-card p-0' },
        el('div', { class: 'table-responsive' },
          el('table', { class: 'table align-middle mb-0' },
            el('thead', {}, el('tr', {}, ...['#', 'Date', 'Cashier', 'Items', 'Payment', 'Status', 'Total', ''].map((h) => el('th', { class: (h === 'Total' || h === '') ? 'text-end' : '', text: h })))),
            tbody))));
    },
  };
})();
