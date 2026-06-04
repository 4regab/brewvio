window.Views = window.Views || {};
(function () {
  const { el, money, esc, button, toast, modal, closeModal, promptReason, spinner, empty, dateTime } = UI;

  let menu = [], modifiers = [], cart = [], discount = 0, activeCat = 'All', seq = 1, searchQuery = '';
  let cartItemsEl, totalsEl, chargeBtn, cancelBtn, menuGrid, menuTabsEl;

  const IMG_BASE = 'img/';

  // Chao & Brew image mapping
  function menuImage(item) {
    const n = (item.name || '').trim().toLowerCase();
    const c = (item.category || '').toLowerCase();

    // Cold Brew Coffee
    if (n.includes('americano')) return IMG_BASE + 'Cold Brew Coffee/Americano.png';
    if (n.includes('caramel macchiato')) return IMG_BASE + 'Cold Brew Coffee/Caramel Macchiato.png';
    if (n.includes("chao's") || n.includes("chao")) return IMG_BASE + 'Cold Brew Coffee/Chao_s Coldbrew.png';
    if (n.includes('cold brew latte')) return IMG_BASE + 'Cold Brew Coffee/Cold Brew Latte.png';
    if (n.includes('mocha') && c === 'cold brew coffee') return IMG_BASE + 'Cold Brew Coffee/Mocha.png';
    if (n.includes('spanish latte')) return IMG_BASE + 'Cold Brew Coffee/Spanish Latte.png';
    if (n.includes('vanilla latte')) return IMG_BASE + 'Cold Brew Coffee/Vanilla Latte.png';
    if (n.includes('latte') && c === 'cold brew coffee') return IMG_BASE + 'Cold Brew Coffee/Latte.png';
    if (c === 'cold brew coffee') return IMG_BASE + 'Cold Brew Coffee/Americano.png';

    // Non-Coffee
    if (n.includes('strawberry milk')) return IMG_BASE + 'Non-Coffee/Strawberry Milk.png';
    if (n.includes('blueberry milk')) return IMG_BASE + 'Non-Coffee/Blueberry Milk.png';
    if (n.includes('mango cream')) return IMG_BASE + 'Non-Coffee/Mango Cream.png';
    if (n.includes('iced choco')) return IMG_BASE + 'Non-Coffee/Iced Choco.png';
    if (n.includes('milky oreo')) return IMG_BASE + 'Non-Coffee/Milky Oreo.png';
    if (n.includes('berry choco')) return IMG_BASE + 'Non-Coffee/Berry Choco Latte.png';
    if (c === 'non-coffee') return IMG_BASE + 'Non-Coffee/Strawberry Milk.png';

    // Matcha Series
    if (n.includes('matcha frappe') || n.includes('matcha frappuccino')) return IMG_BASE + 'Matcha Series/Matcha Frappuccino.png';
    if (n.includes('dirty matcha')) return IMG_BASE + 'Matcha Series/Dirty Matcha.png';
    if (n.includes('strawberry matcha')) return IMG_BASE + 'Matcha Series/Strawberry Matcha.png';
    if (n.includes('matcha')) return IMG_BASE + 'Matcha Series/Matcha Latte.png';
    if (c === 'matcha series') return IMG_BASE + 'Matcha Series/Matcha Latte.png';

    // Frappe
    if (n.includes('java chip')) return IMG_BASE + 'Frappe/Java Chip.png';
    if (n.includes('milo dinosaur')) return IMG_BASE + 'Frappe/Milo Dinosaur.png';
    if (n.includes('frappuccino') || n.includes('frappucino')) return IMG_BASE + 'Frappe/Frappuccino.png';
    if (n.includes('mocha') && c === 'frappe') return IMG_BASE + 'Frappe/Mocha.png';
    if (n.includes('frappe') || c === 'frappe') return IMG_BASE + 'Frappe/Strawberry.png';

    // Qik's Fried Noodles — Overload
    if (n.includes('overload') && n.includes('korean')) return IMG_BASE + 'QIK_S Fried Noodles/Overload/Overload Noodles w(2) Korean Sausage.png';
    if (n.includes('overload') && n.includes('jap')) return IMG_BASE + 'QIK_S Fried Noodles/Overload/Overload Noodles w(4) Jap Siomai.png';
    if (n.includes('overload') && n.includes('pork')) return IMG_BASE + 'QIK_S Fried Noodles/Overload/Overload Noodles w(4) Pork Siomai.png';
    if (n.includes('overload') && n.includes('egg')) return IMG_BASE + 'QIK_S Fried Noodles/Overload/Overload with 2 Eggs.png';
    if (n.includes('overload')) return IMG_BASE + 'QIK_S Fried Noodles/Overload/Overload Noodles.png';

    // Qik's Fried Noodles — Regular
    if (n.includes('korean sausage')) return IMG_BASE + 'QIK_S Fried Noodles/Noodles with Korean Sausage.png';
    if (n.includes('japanese siomai')) return IMG_BASE + 'QIK_S Fried Noodles/Noodles with Japanese Siomai.png';
    if (n.includes('pork siomai')) return IMG_BASE + 'QIK_S Fried Noodles/Noodles with Pork Siomai.png';
    if (n.includes('with egg')) return IMG_BASE + 'QIK_S Fried Noodles/Noodles with Egg.png';
    if (n.includes('plain')) return IMG_BASE + 'QIK_S Fried Noodles/Plain Noodles.png';
    if (c.includes('noodle') || c.includes('qik')) return IMG_BASE + 'QIK_S Fried Noodles/Plain Noodles.png';

    // Fruit Soda
    if (n.includes('soda') || c === 'fruit soda') return IMG_BASE + 'Non-Coffee/Strawberry Milk.png';

    // Food (Main, Silog, Extras)
    if (c === 'food') return IMG_BASE + 'Cold Brew Coffee/Americano.png';

    // Fallback
    return IMG_BASE + 'Cold Brew Coffee/Americano.png';
  }

  // Category icon mapping — Chao & Brew categories
  function catBiIcon(name) {
    const k = (name || '').toLowerCase();
    if (k === 'all') return 'bi-grid-fill';
    if (k === 'food') return 'bi-egg-fried';
    if (k === 'cold brew coffee') return 'bi-cup-hot-fill';
    if (k === 'non-coffee') return 'bi-droplet';
    if (k === 'matcha series') return 'bi-leaf';
    if (k === 'frappe') return 'bi-cup-straw';
    if (k === 'fruit soda') return 'bi-cup-straw';
    if (k.includes('noodle') || k.includes('qik')) return 'bi-box2';
    return 'bi-cup-hot';
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
    else cart.push({ uid: seq++, key: k, menuItemId: item.id, name: item.name, price: item.price, quantity: 1, modifiers: [], img: menuImage(item) });
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
          el('input', { class: 'form-check-input mt-0', type: 'checkbox', checked: chosen.has(m.id),
            onChange: (e) => e.target.checked ? chosen.add(m.id) : chosen.delete(m.id) }),
          el('span', { class: 'form-check-label', html: `${esc(m.name)} <span class="text-secondary">(${m.priceDelta >= 0 ? '+' : ''}${money(m.priceDelta)})</span>` }))))));
    modal({
      title: 'Customize - ' + line.name, body,
      footer: [button('Cancel', 'btn-light', closeModal), button('Apply', 'btn-primary', () => {
        line.modifiers = modifiers.filter((m) => chosen.has(m.id)).map((m) => ({ id: m.id, name: m.name, priceDelta: m.priceDelta }));
        line.key = keyOf(line.menuItemId, line.modifiers);
        closeModal(); renderCart();
      })],
    });
  }

  function renderMenu() {
    const cats = ['All', ...Array.from(new Set(menu.map((m) => m.category)))];
    menuTabsEl.innerHTML = '';
    cats.forEach((c) => {
      const count = c === 'All' ? menu.length : menu.filter((m) => m.category === c).length;
      const tab = el('button', { class: 'cat-tab' + (c === activeCat ? ' active' : ''), onClick: () => { activeCat = c; renderMenu(); } });
      tab.innerHTML = `<i class="bi ${catBiIcon(c)} cat-icon"></i><span class="cat-name">${esc(c)}</span><span class="cat-count">${count} Items</span>`;
      menuTabsEl.appendChild(tab);
    });

    menuGrid.innerHTML = '';
    let items = menu.filter((m) => activeCat === 'All' || m.category === activeCat);
    if (searchQuery) {
      const q = searchQuery.toLowerCase();
      items = items.filter((m) => m.name.toLowerCase().includes(q) || (m.category || '').toLowerCase().includes(q));
    }
    if (!items.length) { menuGrid.appendChild(empty('bi-cup', 'No items found.')); return; }

    items.forEach((m) => {
      const catKey = (m.category || '').toLowerCase().replace(/\s+/g, '-');
      const tile = el('button', { class: 'menu-tile', onClick: () => addToCart(m) },
        el('div', { class: 'menu-tile-photo' },
          el('img', { src: menuImage(m), alt: m.name, loading: 'lazy' })),
        el('div', { class: 'menu-tile-body' },
          el('div', { class: 'name', text: m.name }),
          el('div', { class: 'cat-label', 'data-cat': catKey, text: m.category || '' }),
          el('div', { class: 'price', text: money(m.price) })));
      menuGrid.appendChild(tile);
    });
  }

  function renderCart() {
    cartItemsEl.innerHTML = '';
    if (!cart.length) {
      const emptyEl = el('div', { class: 'cart-empty' },
        el('i', { class: 'bi bi-basket' }),
        el('div', { class: 'empty-title', text: 'No Item Selected' }),
        el('div', { class: 'empty-sub', text: 'Tap items from the menu to add.' }));
      cartItemsEl.appendChild(emptyEl);
      totalsEl.style.display = 'none';
      chargeBtn.disabled = true;
      cancelBtn.disabled = true;
      return;
    }

    totalsEl.style.display = '';
    cart.forEach((l) => {
      const row = el('div', { class: 'cart-line' },
        el('div', { class: 'cart-line-thumb' },
          el('img', { src: l.img || IMG_BASE + 'latte.png', alt: l.name })),
        el('div', { class: 'cart-line-body' },
          el('div', { class: 'cart-line-top' },
            el('div', { class: 'item-name', text: l.name }),
            el('div', { class: 'item-price', text: money(lineTotal(l)) })),
          l.modifiers.length
            ? el('div', { style: 'font-size:.68rem;color:var(--brew-muted-l);margin:.1rem 0', text: l.modifiers.map((m) => m.name).join(', ') })
            : null,
          el('div', { class: 'cart-line-actions' },
            el('div', { class: 'qty-stepper' },
              button('\u2212', '', () => { l.quantity > 1 ? l.quantity-- : (cart = cart.filter((x) => x.uid !== l.uid)); renderCart(); }),
              el('span', { text: l.quantity }),
              button('+', '', () => { l.quantity++; renderCart(); })),
            modifiers.length ? button('<i class="bi bi-sliders"></i>', 'btn-sm btn-light', () => customize(l)) : null,
            button('<i class="bi bi-trash"></i>', 'btn-sm btn-outline-danger', () => { cart = cart.filter((x) => x.uid !== l.uid); renderCart(); }))));
      cartItemsEl.appendChild(row);
    });
    renderTotals();
  }

  function renderTotals() {
    const t = totals();
    totalsEl.innerHTML = '';

    const line = (label, valNode, extra) => {
      const row = el('div', { class: 'total-line' + (extra ? ' ' + extra : '') });
      row.appendChild(el('span', { text: label }));
      if (typeof valNode === 'string') {
        const s = el('span', { class: 'val', text: valNode }); row.appendChild(s);
      } else { row.appendChild(valNode); }
      return row;
    };

    const discInput = el('input', {
      type: 'number', min: '0', step: '0.01', value: discount || '',
      class: 'disc-input',
      onInput: (e) => { discount = Math.max(0, Number(e.target.value) || 0); renderTotals(); }
    });

    totalsEl.appendChild(line('Subtotal', money(t.subtotal)));
    totalsEl.appendChild(line('Discount', discInput));
    totalsEl.appendChild(line('Tax (' + taxRate() + '%)', money(t.tax)));
    totalsEl.appendChild(line('TOTAL', money(t.total), 'grand'));

    chargeBtn.disabled = !cart.length;
    cancelBtn.disabled = !cart.length;
  }

  function openPayment() {
    const t = totals();
    let method = 'Cash';
    const cashIn = el('input', { type: 'number', min: '0', step: '0.01', class: 'form-control form-control-lg', value: t.total.toFixed(2) });
    const cardIn = el('input', { type: 'number', min: '0', step: '0.01', class: 'form-control form-control-lg', value: '0.00' });
    const changeRow = el('div', { class: 'd-flex justify-content-between fs-5 mt-3' },
      el('span', { text: 'Change' }), el('span', { class: 'fw-bold change-val', text: money(0) }));
    const cashWrap = el('div', { class: 'mb-2' }, el('label', { class: 'form-label', text: 'Cash tendered' }), cashIn);
    const cardWrap = el('div', { class: 'mb-2 d-none' }, el('label', { class: 'form-label', text: 'Card amount' }), cardIn);

    function recalc() {
      const cash = Number(cashIn.value) || 0, card = Number(cardIn.value) || 0;
      const paid = (method === 'Card' ? 0 : cash) + (method === 'Cash' ? 0 : card);
      changeRow.querySelector('.change-val').textContent = money(Math.max(0, paid - t.total));
    }
    cashIn.addEventListener('input', recalc); cardIn.addEventListener('input', recalc);

    const seg = el('div', { class: 'btn-group w-100 mb-3' });
    ['Cash', 'Card', 'Split'].forEach((m, i) => seg.appendChild(el('button', {
      class: 'btn ' + (i === 0 ? 'btn-primary' : 'btn-outline-secondary'), text: m, onClick: () => {
        method = m;
        Array.from(seg.children).forEach((b) => b.className = 'btn ' + (b.textContent === m ? 'btn-primary' : 'btn-outline-secondary'));
        cashWrap.classList.toggle('d-none', m === 'Card');
        cardWrap.classList.toggle('d-none', m === 'Cash');
        if (m === 'Card') cardIn.value = t.total.toFixed(2);
        if (m === 'Cash') cashIn.value = t.total.toFixed(2);
        if (m === 'Split') { cashIn.value = '0.00'; cardIn.value = '0.00'; }
        recalc();
      }
    })));

    const confirm = button('Confirm payment', 'btn-primary btn-lg w-100', async () => {
      const cash = Number(cashIn.value) || 0, card = Number(cardIn.value) || 0;
      const payments = [];
      if (method !== 'Card' && cash > 0) payments.push({ method: 'Cash', amount: cash });
      if (method !== 'Cash' && card > 0) payments.push({ method: 'Card', amount: card });
      if (!payments.length) { toast('Enter a payment amount.', 'warning'); return; }
      confirm.disabled = true;
      try {
        const receipt = await Api.post('/api/orders', {
          items: cart.map((l) => ({ menuItemId: l.menuItemId, quantity: l.quantity, modifierIds: l.modifiers.map((m) => m.id), notes: null })),
          discountAmount: discount || 0, payments
        });
        cart = []; discount = 0; renderCart();
        (receipt.stockWarnings || []).forEach((w) => toast(w, 'warning'));
        showReceipt(receipt, true);
      } catch (e) { toast(e.message, 'danger'); confirm.disabled = false; }
    });

    modal({ title: 'Payment - ' + money(t.total), body: el('div', {}, seg, cashWrap, cardWrap, changeRow), footer: [button('Cancel', 'btn-light', closeModal), confirm] });
    recalc();
  }

  function receiptNode(r) {
    const store = window.App.store || {};
    const row = (a, b, strong) => el('div', { class: 'row2' }, el(strong ? 'b' : 'span', { text: a }), el(strong ? 'b' : 'span', { text: b }));
    return el('div', { class: 'receipt' },
      el('h4', { text: store.storeName || 'Chao & Brew' }),
      store.address ? el('div', { class: 'center', text: store.address }) : null,
      el('hr'),
      row('Receipt #', String(r.transactionId)), row('Date', dateTime(r.timestamp)), row('Cashier', r.cashier),
      r.status !== 'Completed' ? el('div', { class: 'center', html: `<b>** ${esc(r.status.toUpperCase())} **</b>` }) : null,
      el('hr'),
      ...r.items.map((it) => el('div', {}, row(`${it.quantity}x ${it.name}`, money(it.lineTotal)),
        it.modifiers ? el('div', { style: 'font-size:11px', text: '   + ' + it.modifiers }) : null)),
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
      footer: [button('Close', 'btn-light', closeModal), button('<i class="bi bi-printer"></i> Print', 'btn-primary', () => {
        const area = document.getElementById('print-area');
        area.innerHTML = ''; area.appendChild(receiptNode(r)); window.print();
      })],
    });
  }

  // ── POS VIEW ────────────────────────────────────────────
  Views.pos = {
    render: async (root) => {
      // POS takes full height with no extra padding - handled by pos-layout
      root.style.padding = '0';
      root.style.background = 'var(--brew-surface)';
      root.innerHTML = '';
      root.appendChild(spinner('Loading menu...'));

      try { [menu, modifiers] = await Promise.all([Api.get('/api/menu'), Api.get('/api/menu/modifiers')]); }
      catch (e) { root.innerHTML = ''; root.appendChild(empty('bi-exclamation-triangle', e.message)); return; }
      root.innerHTML = '';

      menuTabsEl = el('div', { class: 'cat-tabs' });
      menuGrid = el('div', { class: 'menu-grid' });

      const searchInput = el('input', {
        type: 'text', placeholder: 'Search something sweet on your mind...',
        onInput: (e) => { searchQuery = e.target.value.trim(); renderMenu(); }
      });
      const left = el('div', { class: 'pos-left' },
        menuTabsEl,
        el('div', { class: 'pos-search' }, searchInput, el('i', { class: 'bi bi-search search-icon' })),
        menuGrid);

      cartItemsEl = el('div', { class: 'cart-items' });
      totalsEl = el('div', { class: 'cart-totals' });

      chargeBtn = el('button', { class: 'btn-complete', disabled: true, onClick: openPayment }, 'Place Order');
      cancelBtn = button('Cancel order', 'btn-outline-danger btn-sm w-100 mt-2', async () => {
        const reason = await promptReason({ title: 'Cancel order', label: 'Reason for cancellation', confirmText: 'Cancel order', confirmClass: 'btn-danger' });
        if (reason == null) return;
        try { await Api.post('/api/orders/cancel', { reason }); cart = []; discount = 0; renderCart(); toast('Order cancelled.'); }
        catch (e) { toast(e.message, 'danger'); }
      });

      const cartPanel = el('div', { class: 'cart-panel' },
        el('div', { class: 'cart-header' },
          el('div', { class: 'cart-header-icon' }, el('i', { class: 'bi bi-receipt' })),
          el('div', { class: 'order-number', text: 'Current Order' }),
          el('button', { class: 'cart-header-edit', onClick: () => { cart = []; discount = 0; renderCart(); } },
            el('i', { class: 'bi bi-pencil' }))),
        cartItemsEl, totalsEl,
        el('div', { class: 'cart-actions' }, chargeBtn, cancelBtn));

      root.appendChild(el('div', { class: 'pos-layout' }, left, cartPanel));
      renderMenu();
      renderCart();
    },
  };

  // ── ACTIVITY VIEW ────────────────────────────────────────
  Views.activity = {
    render: async (root) => {
      root.innerHTML = '';
      const tabQueue = el('button', { class: 'activity-tab active', text: 'Order Queue', onClick: () => switchTab('queue') });
      const tabHistory = el('button', { class: 'activity-tab', text: 'Order History', onClick: () => switchTab('history') });
      const tabs = el('div', { class: 'activity-tabs' }, tabQueue, tabHistory);
      const content = el('div');
      root.appendChild(tabs);
      root.appendChild(content);

      function switchTab(tab) {
        tabQueue.classList.toggle('active', tab === 'queue');
        tabHistory.classList.toggle('active', tab === 'history');
        if (tab === 'queue') renderQueue(); else renderHistory();
      }

      async function renderQueue() {
        content.innerHTML = ''; content.appendChild(spinner());
        let list;
        try { list = await Api.get('/api/orders/recent?take=50'); }
        catch (e) { content.innerHTML = ''; content.appendChild(empty('bi-exclamation-triangle', e.message)); return; }
        content.innerHTML = '';

        let filter = 'All';
        const filtersEl = el('div', { class: 'filter-row' });
        ['All', 'Active', 'Closed'].forEach((f) => {
          filtersEl.appendChild(el('button', { class: 'filter-chip' + (f === filter ? ' active' : ''), text: f, onClick: () => {
            filter = f;
            filtersEl.querySelectorAll('.filter-chip').forEach((c) => c.classList.toggle('active', c.textContent === f));
            renderList();
          }}));
        });
        content.appendChild(filtersEl);

        const listEl = el('div', { class: 'order-queue' });
        content.appendChild(listEl);

        function renderList() {
          listEl.innerHTML = '';
          const filtered = list.filter((tx) => {
            if (filter === 'Active') return tx.status !== 'Completed' && tx.status !== 'Refunded';
            if (filter === 'Closed') return tx.status === 'Completed' || tx.status === 'Refunded';
            return true;
          });
          if (!filtered.length) { listEl.appendChild(empty('bi-receipt', 'No orders found.')); return; }
          filtered.forEach((tx) => {
            const badge = tx.status === 'Completed' ? 'badge-soft-success' : tx.status === 'Refunded' ? 'badge-soft-danger' : 'badge-soft-warning';
            listEl.appendChild(el('div', { class: 'order-card', style: 'cursor:pointer', onClick: async () => {
              try { showReceipt(await Api.get('/api/orders/' + tx.id)); } catch (e) { toast(e.message, 'danger'); }
            }},
              el('div', { class: 'order-info' },
                el('div', { class: 'order-name', text: 'Order #' + String(tx.id).padStart(3, '0') }),
                el('div', { class: 'order-meta', text: dateTime(tx.timestamp) + ' · ' + tx.itemCount + ' items' })),
              el('div', {},
                el('div', { class: 'order-total', text: money(tx.totalAmount) }),
                el('div', { class: 'order-status mt-1' }, el('span', { class: 'badge ' + badge, text: tx.status })))));
          });
        }
        renderList();
      }

      async function renderHistory() {
        content.innerHTML = ''; content.appendChild(spinner());
        let list;
        try { list = await Api.get('/api/orders/recent?take=100'); }
        catch (e) { content.innerHTML = ''; content.appendChild(empty('bi-exclamation-triangle', e.message)); return; }
        content.innerHTML = '';

        const badge = (s) => `<span class="badge ${s === 'Completed' ? 'badge-soft-success' : s === 'Refunded' ? 'badge-soft-danger' : 'badge-soft-muted'}">${esc(s)}</span>`;
        const tbody = el('tbody');
        if (!list.length) tbody.appendChild(el('tr', {}, el('td', { colspan: 7 }, empty('bi-receipt', 'No transactions yet.'))));
        list.forEach((tx) => {
          const actions = el('div', { class: 'd-flex gap-2 justify-content-end' },
            button('Detail', 'btn-sm btn-light', async () => { try { showReceipt(await Api.get('/api/orders/' + tx.id)); } catch (e) { toast(e.message, 'danger'); } }),
            tx.status === 'Completed' ? button('Refund', 'btn-sm btn-outline-danger', async () => {
              const reason = await promptReason({ title: 'Refund #' + tx.id, label: 'Reason for refund', confirmText: 'Refund', confirmClass: 'btn-danger' });
              if (reason == null) return;
              try { await Api.post(`/api/orders/${tx.id}/refund`, { reason }); toast('Refunded. Stock restored.'); renderHistory(); }
              catch (e) { toast(e.message, 'danger'); }
            }) : null);
          tbody.appendChild(el('tr', {},
            el('td', { text: '#' + String(tx.id).padStart(3, '0') }),
            el('td', { text: dateTime(tx.timestamp) }),
            el('td', { text: tx.cashier }),
            el('td', { text: tx.itemCount + ' items' }),
            el('td', { html: badge(tx.status) }),
            el('td', { class: 'text-end fw-semibold', text: money(tx.totalAmount) }),
            el('td', { class: 'text-end' }, actions)));
        });
        content.appendChild(el('div', { class: 'section-card p-0' },
          el('div', { class: 'table-responsive' },
            el('table', { class: 'table align-middle mb-0' },
              el('thead', {}, el('tr', {}, ...['#', 'Date & Time', 'Cashier', 'Items', 'Status', 'Total', ''].map((h) => el('th', { class: (h === 'Total' || h === '') ? 'text-end' : '', text: h })))),
              tbody))));
      }

      await renderQueue();
    },
  };

  Views.transactions = Views.activity;
})();
