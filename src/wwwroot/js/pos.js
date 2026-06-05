window.Views = window.Views || {};
(function () {
  const { el, money, esc, button, toast, modal, closeModal, promptReason, spinner, empty, dateTime } = UI;

  let menu = [], modifiers = [], cart = [], discount = 0, activeCat = 'All', seq = 1, searchQuery = '';
  let cartItemsEl, totalsEl, chargeBtn, cancelBtn, payMethodBtn, promoInputEl, menuGrid, menuTabsEl;

  const IMG_BASE = 'img/';

  // Chao & Brew image mapping
  function menuImage(item) {
    const n = (item.name || '').trim().toLowerCase();
    const c = (item.category || '').toLowerCase();

    // Cold Coffee
    if (n.includes('americano')) return IMG_BASE + 'Cold Brew Coffee/Americano.png';
    if (n.includes('caramel macchiato')) return IMG_BASE + 'Cold Brew Coffee/Caramel Macchiato.png';
    if (n.includes("chao's") || n.includes("chao")) return IMG_BASE + 'Cold Brew Coffee/Chao_s Coldbrew.png';
    if (n.includes('cold brew latte')) return IMG_BASE + 'Cold Brew Coffee/Cold Brew Latte.png';
    if (n.includes('mocha') && c === 'cold coffee') return IMG_BASE + 'Cold Brew Coffee/Mocha.png';
    if (n.includes('spanish latte')) return IMG_BASE + 'Cold Brew Coffee/Spanish Latte.png';
    if (n.includes('vanilla latte')) return IMG_BASE + 'Cold Brew Coffee/Vanilla Latte.png';
    if (n.includes('latte') && c === 'cold coffee') return IMG_BASE + 'Cold Brew Coffee/Latte.png';
    if (c === 'cold coffee') return IMG_BASE + 'Cold Brew Coffee/Americano.png';

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

    // Food — individual image mapping
    if (c === 'food') {
      if (n.includes('pork tonkatsu'))    return IMG_BASE + 'Food/Pork Tonkatsu.png';
      if (n.includes('chicken tonkatsu')) return IMG_BASE + 'Food/Chicken Tonkatsu.png';
      if (n.includes('chicken poppers'))  return IMG_BASE + 'Food/Chicken Poppers.png';
      if (n.includes('chicken fingers'))  return IMG_BASE + 'Food/Chicken Fingers.png';
      if (n.includes('crabstick'))        return IMG_BASE + 'Food/Crabstick Katsu.png';
      if (n.includes('spamsilog'))        return IMG_BASE + 'Food/Spamsilog.png';
      if (n.includes('hungarian') || n.includes('hungariansilog')) return IMG_BASE + 'Food/Hungarian Silog.png';
      if (n.includes('tocilog'))          return IMG_BASE + 'Food/Tocilog.png';
      if (n.includes('tapsilog'))         return IMG_BASE + 'Food/Tapsilog.png';
      if (n.includes('sausilog'))         return IMG_BASE + 'Food/Sausilog.png';
      if (n.includes('bacsilog'))         return IMG_BASE + 'Food/Bacsilog.png';
      // Generic food fallback
      return IMG_BASE + 'Food/Chicken Tonkatsu.png';
    }

    // Fallback — use a non-coffee default to avoid confusion
    return IMG_BASE + 'Cold Brew Coffee/Americano.png';
  }

  // Category icon mapping — Chao & Brew categories
  function catBiIcon(name) {
    const k = (name || '').toLowerCase();
    if (k === 'all') return 'bi-grid-fill';
    if (k === 'food') return 'bi-egg-fried';
    if (k === 'cold coffee') return 'bi-cup-hot-fill';
    if (k === 'non-coffee') return 'bi-droplet';
    if (k.includes('matcha')) return 'bi-flower2';
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

    // Running extra cost
    const calcExtra = () => modifiers.filter((m) => chosen.has(m.id)).reduce((s, m) => s + m.priceDelta, 0);

    // Summary price element
    const summaryPrice = el('span', { class: 'cust-summary-price', text: money(lineUnit(line)) });
    const updateSummary = () => {
      const base = line.price;
      const extra = calcExtra();
      summaryPrice.textContent = money(base + extra);
    };

    // Item header
    const header = el('div', { class: 'cust-header' },
      el('div', { class: 'cust-thumb' },
        el('img', { src: line.img || IMG_BASE + 'Cold Brew Coffee/Americano.png', alt: line.name })),
      el('div', { class: 'cust-info' },
        el('div', { class: 'cust-name', text: line.name }),
        el('div', { class: 'cust-base-price', text: 'Base: ' + money(line.price) })));

    // Build groups
    const groupEls = Object.entries(groups).map(([g, mods]) => {
      const chips = mods.map((m) => {
        const chip = el('button', {
          type: 'button',
          class: 'cust-chip' + (chosen.has(m.id) ? ' active' : ''),
          onClick: () => {
            if (chosen.has(m.id)) { chosen.delete(m.id); chip.classList.remove('active'); }
            else { chosen.add(m.id); chip.classList.add('active'); }
            updateSummary();
          }
        },
          el('span', { class: 'cust-chip-name', text: m.name }),
          el('span', { class: 'cust-chip-price', text: (m.priceDelta >= 0 ? '+' : '') + money(m.priceDelta) }));
        return chip;
      });
      return el('div', { class: 'cust-group' },
        el('div', { class: 'cust-group-label', text: g }),
        el('div', { class: 'cust-chips' }, ...chips));
    });

    // Summary bar
    const summaryBar = el('div', { class: 'cust-summary' },
      el('span', { class: 'cust-summary-label', text: 'Unit price with add-ons' }),
      summaryPrice);

    const body = el('div', { class: 'cust-body' }, header, ...groupEls, summaryBar);

    modal({
      title: 'Customize Order', body,
      footer: [
        button('Reset', 'btn-light', () => {
          chosen.clear();
          body.querySelectorAll('.cust-chip.active').forEach((c) => c.classList.remove('active'));
          updateSummary();
        }),
        button('Apply', 'btn-primary', () => {
          line.modifiers = modifiers.filter((m) => chosen.has(m.id)).map((m) => ({ id: m.id, name: m.name, priceDelta: m.priceDelta }));
          line.key = keyOf(line.menuItemId, line.modifiers);
          closeModal(); renderCart();
        }),
      ],
    });
  }

  function renderMenu() {
    const cats = ['All', ...Array.from(new Set(menu.map((m) => m.category)))];
    menuTabsEl.innerHTML = '';
    cats.forEach((c) => {
      const count = c === 'All' ? menu.length : menu.filter((m) => m.category === c).length;
      const tab = el('button', { class: 'cat-tab' + (c === activeCat ? ' active' : ''), onClick: () => { activeCat = c; renderMenu(); } });
      tab.innerHTML = `<i class="bi ${catBiIcon(c)} cat-icon"></i><span class="cat-name">${esc(c)}</span><span class="cat-count">${count} ${count === 1 ? 'Item' : 'Items'}</span>`;
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
          el('div', { class: 'cat-label', 'data-cat': catKey, text: m.category || '' }),
          el('img', { src: menuImage(m), alt: m.name, loading: 'lazy' })),
        el('div', { class: 'menu-tile-body' },
          el('div', { class: 'name', text: m.name }),
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
      chargeBtn.disabled = true;
      cancelBtn.disabled = true;
      if (payMethodBtn) payMethodBtn.disabled = true;
      renderTotals();
      return;
    }


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
            modifiers.length ? el('button', { class: 'btn btn-sm btn-light', type: 'button', 'aria-label': 'Customize ' + l.name, onClick: () => customize(l) }, el('i', { class: 'bi bi-sliders' })) : null,
            el('button', { class: 'btn btn-sm btn-outline-danger', type: 'button', 'aria-label': 'Remove ' + l.name, onClick: () => { cart = cart.filter((x) => x.uid !== l.uid); renderCart(); } }, el('i', { class: 'bi bi-trash' })))));
      cartItemsEl.appendChild(row);
    });
    renderTotals();
  }

  // Split formatted money into currency code + numeric portion for 3-col layout
  function moneyParts(n) {
    const s = money(n);
    const i = s.indexOf(' ');
    return i < 0 ? { cur: '', val: s } : { cur: s.slice(0, i), val: s.slice(i + 1) };
  }

  function renderTotals() {
    const t = totals();
    totalsEl.innerHTML = '';

    const line = (label, n, extra) => {
      const { cur, val } = moneyParts(n);
      const row = el('div', { class: 'total-line' + (extra ? ' ' + extra : '') });
      row.appendChild(el('span', { class: 'total-label', text: label }));
      row.appendChild(el('span', { class: 'total-cur', text: cur }));
      row.appendChild(el('span', { class: 'total-val', text: val }));
      return row;
    };

    totalsEl.appendChild(line('Subtotal', t.subtotal));
    if (t.disc > 0) totalsEl.appendChild(line('Discount', -t.disc, 'discount'));
    totalsEl.appendChild(line('Tax (' + taxRate() + '%)', t.tax));
    totalsEl.appendChild(el('div', { class: 'total-divider' }));
    totalsEl.appendChild(line('TOTAL', t.total, 'grand'));

    chargeBtn.disabled = !cart.length;
    cancelBtn.disabled = !cart.length;
    if (payMethodBtn) payMethodBtn.disabled = !cart.length;
  }

  function openPayment() {
    const t = totals();
    let method = 'Cash';
    const cashIn = el('input', { type: 'number', min: '0', step: '0.01', class: 'form-control form-control-lg', value: t.total.toFixed(2) });
    const changeRow = el('div', { class: 'd-flex justify-content-between fs-5 mt-3' },
      el('span', { text: 'Change' }), el('span', { class: 'fw-bold change-val', text: money(0) }));
    const cashLabel = el('label', { class: 'form-label', text: 'Cash tendered' });
    const cashWrap = el('div', { class: 'mb-2' }, cashLabel, cashIn);

    function recalc() {
      const cash = Number(cashIn.value) || 0;
      changeRow.querySelector('.change-val').textContent = money(Math.max(0, cash - t.total));
    }
    cashIn.addEventListener('input', recalc);

    const seg = el('div', { class: 'btn-group w-100 mb-3' });
    const setMethod = (m) => {
      method = m;
      Array.from(seg.children).forEach((b) => b.className = 'btn ' + (b.dataset.method === m ? 'btn-primary' : 'btn-outline-secondary'));
      cashLabel.textContent = m === 'GCash' ? 'GCash amount' : 'Cash tendered';
      cashIn.value = t.total.toFixed(2);
      recalc();
    };
    [
      { key: 'Cash', label: '<i class="bi bi-cash-coin"></i> Cash' },
      { key: 'GCash', label: '<i class="bi bi-phone"></i> GCash' },
    ].forEach((m, i) => seg.appendChild(el('button', {
      class: 'btn ' + (i === 0 ? 'btn-primary' : 'btn-outline-secondary'),
      html: m.label, dataset: { method: m.key },
      onClick: () => setMethod(m.key),
    })));

    const confirm = button('Confirm payment', 'btn-complete btn-lg w-100', async () => {
      const tendered = Number(cashIn.value) || 0;
      if (tendered + 0.005 < t.total) { toast('Insufficient payment.', 'warning'); return; }
      confirm.disabled = true;
      try {
        const receipt = await Api.post('/api/orders', {
          items: cart.map((l) => ({ menuItemId: l.menuItemId, quantity: l.quantity, modifierIds: l.modifiers.map((m) => m.id), notes: null })),
          discountAmount: discount || 0,
          payments: [{ method, amount: tendered }],
        });
        cart = []; discount = 0; if (promoInputEl) promoInputEl.value = ''; renderCart();
        // Update order number for next order
        try { const nr = await Api.get('/api/orders/next-number'); cartOrderNumEl.textContent = 'Order Number: #' + String(nr.nextId).padStart(3, '0'); } catch { /* non-critical */ }
        (receipt.stockWarnings || []).forEach((w) => toast(w, 'warning'));
        showReceipt(receipt, true);
      } catch (e) { toast(e.message, 'danger'); confirm.disabled = false; }
    });

    modal({ title: 'Payment - ' + money(t.total), body: el('div', {}, seg, cashWrap, changeRow), footer: [confirm] });
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
    const orderId = r.id || r.transactionId;
    const pdfBtn = button('<i class="bi bi-file-earmark-pdf"></i> Save PDF', 'btn-primary', async () => {
      try { await Api.download(`/api/orders/${orderId}/pdf`, `receipt_${orderId}.pdf`); }
      catch (e) { toast(e.message, 'danger'); }
    });

    let footerBtns;
    if (isNew) {
      footerBtns = [pdfBtn];
    } else {
      const refundBtn = r.status === 'Completed'
        ? button('<i class="bi bi-arrow-counterclockwise"></i> Refund', 'btn-outline-danger', async () => {
            const reason = await promptReason({ title: 'Refund #' + r.transactionId, label: 'Reason for refund', confirmText: 'Refund', confirmClass: 'btn-danger' });
            if (reason == null) return;
            try {
              await Api.post(`/api/orders/${orderId}/refund`, { reason });
              toast('Refunded. Stock restored.', 'success');
              closeModal();
            } catch (e) { toast(e.message, 'danger'); }
          })
        : null;

      footerBtns = [refundBtn, pdfBtn].filter(Boolean);
    }

    modal({
      title: isNew ? 'Order complete' : 'Receipt #' + r.transactionId,
      body: isNew
        ? el('div', { class: 'py-3 text-center' },
            el('i', { class: 'bi bi-check-circle-fill text-success', style: 'font-size:3rem' }),
            el('h5', { class: 'mt-3 mb-1', text: 'Payment confirmed!' }),
            el('div', { class: 'text-muted', text: `Order #${r.transactionId} • ${money(r.totalAmount)}` }),
            r.change > 0 ? el('div', { class: 'mt-2 fw-semibold', text: `Change: ${money(r.change)}` }) : null)
        : el('div', { class: 'py-2' }, receiptNode(r)),
      footer: footerBtns,
    });
  }

  // ── POS VIEW ────────────────────────────────────────────
  Views.pos = {
    render: async (root) => {
      // POS takes full height with no extra padding - handled by pos-layout
      root.style.padding = '0';
      root.style.background = 'var(--brew-surface)';
      root.classList.add('is-pos');
      root.innerHTML = '';
      root.appendChild(spinner('Loading menu...'));

      try { [menu, modifiers] = await Promise.all([Api.get('/api/menu'), Api.get('/api/menu/modifiers')]); }
      catch (e) { root.innerHTML = ''; root.appendChild(empty('bi-exclamation-triangle', e.message)); return; }
      root.innerHTML = '';

      // Fetch next order number
      let nextOrderNum = '---';
      try { const r = await Api.get('/api/orders/next-number'); nextOrderNum = String(r.nextId).padStart(3, '0'); } catch { /* non-critical */ }

      // ── POS topbar layout (matches reference) ────────────────────────
      // LEFT (after hamburger):  📅 date  —  🕐 time
      // RIGHT:                   ● End Shift
      const topbar = document.querySelector('.topbar');
      const navToggle = document.querySelector('#nav-toggle');

      // Clean up any leftover POS topbar elements from a previous render
      document.querySelectorAll('.pos-tb-left').forEach((el) => el.remove());
      topbar.classList.add('pos-active'); // hides the default plain date

      const now = new Date();
      const dateStr = now.toLocaleDateString('en-US', { weekday: 'short', day: '2-digit', month: 'short', year: 'numeric' });

      const posClockText = el('span', { class: 'pos-tb-clock-text',
        text: now.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' }) });

      // Left group — inserted right after the hamburger button
      const posLeftGroup = el('div', { class: 'pos-tb-left' },
        el('div', { class: 'pos-tb-date' },
          el('i', { class: 'bi bi-calendar3' }),
          el('span', { text: dateStr })),
        el('span', { class: 'pos-tb-sep', text: '—' }),
        el('div', { class: 'pos-tb-clock' },
          el('i', { class: 'bi bi-clock' }),
          posClockText));
      navToggle.insertAdjacentElement('afterend', posLeftGroup);

      // Live clock tick
      const clockTick = setInterval(() => {
        if (document.contains(posClockText)) {
          posClockText.textContent = new Date().toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });
        } else {
          clearInterval(clockTick);
        }
      }, 10000);

      // Restore topbar when navigating away
      const cleanup = () => {
        clearInterval(clockTick);
        topbar.classList.remove('pos-active');
        posLeftGroup.remove();
        root.classList.remove('is-pos');
      };
      window.addEventListener('hashchange', cleanup, { once: true });

      menuTabsEl = el('div', { class: 'cat-rail' });
      menuGrid = el('div', { class: 'menu-grid' });

      const searchInput = el('input', {
        type: 'text', placeholder: 'Search Product...',
        onInput: (e) => { searchQuery = e.target.value.trim(); renderMenu(); }
      });
      const left = el('div', { class: 'pos-left' },
        el('div', { class: 'pos-menu-row' },
          menuTabsEl,
          el('div', { class: 'pos-menu-col' },
            el('div', { class: 'pos-search' }, el('i', { class: 'bi bi-search search-icon-left' }), searchInput),
            el('div', { class: 'menu-grid-wrap' }, menuGrid))));

      cartItemsEl = el('div', { class: 'cart-items' });
      totalsEl = el('div', { class: 'cart-totals' });

      chargeBtn = el('button', { class: 'btn-complete', disabled: true, onClick: openPayment }, 'Place Order');
      cancelBtn = button('Cancel order', 'btn-outline-danger btn-sm w-100 mt-2', async () => {
        const reason = await promptReason({ title: 'Cancel order', label: 'Reason for cancellation', confirmText: 'Cancel order', confirmClass: 'btn-danger' });
        if (reason == null) return;
        try { await Api.post('/api/orders/cancel', { reason }); cart = []; discount = 0; if (promoInputEl) promoInputEl.value = ''; renderCart(); toast('Order cancelled.'); }
        catch (e) { toast(e.message, 'danger'); }
      });

      // Promo / voucher input — drives discount amount
      const promoInput = el('input', {
        type: 'number', min: '0', step: '0.01', placeholder: 'Add Promo or Voucher',
        class: 'promo-input', value: discount || '',
        onInput: (e) => { discount = Math.max(0, Number(e.target.value) || 0); renderTotals(); },
      });
      promoInputEl = promoInput;
      const promoApplyBtn = el('button', {
        class: 'promo-apply', type: 'button', title: 'Apply promo',
        onClick: () => { discount = Math.max(0, Number(promoInput.value) || 0); renderTotals(); },
      }, el('i', { class: 'bi bi-tag' }));
      const promoRow = el('div', { class: 'promo-wrap' }, promoInput, promoApplyBtn);

      payMethodBtn = el('button', {
        class: 'pay-method-pill', type: 'button', disabled: true,
        onClick: openPayment,
      }, 'Payment Method');

      const checkoutRow = el('div', { class: 'checkout-row' }, promoRow, payMethodBtn);

      const cartOrderNumEl = el('div', { class: 'cart-header-eyebrow', text: 'Order Number: #' + nextOrderNum });

      const cartPanel = el('div', { class: 'cart-panel' },
        el('div', { class: 'cart-header' },
          el('div', { class: 'cart-header-icon' }, el('i', { class: 'bi bi-receipt' })),
          el('div', { class: 'cart-header-title' },
            el('div', { class: 'cart-header-name', text: 'New Order' }),
            cartOrderNumEl)),
        cartItemsEl, totalsEl,
        el('div', { class: 'cart-actions' }, checkoutRow, chargeBtn, cancelBtn));

      root.appendChild(el('div', { class: 'pos-layout' }, left, cartPanel));
      renderMenu();
      renderCart();
    },
  };

  // ── ACTIVITY VIEW ────────────────────────────────────────
  Views.activity = {
    render: async (root) => {
      root.innerHTML = '';
      let activeTab = 'queue';
      let filter = 'All';

      // Filter chips — shared between tabs
      const filtersEl = el('div', { class: 'activity-filter-row' });
      function renderFilterChips() {
        filtersEl.innerHTML = '';
        ['All', 'Active', 'Closed'].forEach((f) => {
          filtersEl.appendChild(el('button', { class: 'filter-chip' + (f === filter ? ' active' : ''), text: f, onClick: () => {
            filter = f;
            filtersEl.querySelectorAll('.filter-chip').forEach((c) => c.classList.toggle('active', c.textContent === f));
            if (activeTab === 'queue') renderList(); else renderHistoryList();
          }}));
        });
      }

      const tabQueue = el('button', { class: 'activity-tab active', text: 'Order Queue', onClick: () => switchTab('queue') });
      const tabHistory = el('button', { class: 'activity-tab', text: 'Order History', onClick: () => switchTab('history') });
      const tabGroup = el('div', { class: 'activity-tabs-group' }, tabQueue, tabHistory);
      const downloadBtn = button('<i class="bi bi-download"></i> Download', 'btn-sm btn-outline-secondary',
        () => Api.download('/api/orders/export?take=200', 'orders.xlsx').catch((e) => toast(e.message, 'danger')));
      const tabs = el('div', { class: 'activity-tabs-row' }, tabGroup, filtersEl, downloadBtn);

      // No viewToolbar — title is in the topbar
      root.appendChild(tabs);
      const content = el('div');
      root.appendChild(content);

      let currentList = [];

      function switchTab(tab) {
        activeTab = tab;
        tabQueue.classList.toggle('active', tab === 'queue');
        tabHistory.classList.toggle('active', tab === 'history');
        if (tab === 'queue') renderQueue(); else renderHistory();
      }

      // ── Queue list renderer (reused when filter changes) ──
      let listEl;
      function renderList() {
        if (!listEl) return;
        listEl.innerHTML = '';
        const filtered = currentList.filter((tx) => {
          if (filter === 'Active') return tx.status === 'Pending' || tx.status === 'Preparing';
          if (filter === 'Closed') return tx.status === 'Completed' || tx.status === 'Refunded';
          return true;
        });
        if (!filtered.length) { listEl.appendChild(empty('bi-receipt', 'No orders found.')); return; }
        filtered.forEach((tx) => {
          const badge = tx.status === 'Completed' ? 'badge-soft-success'
            : tx.status === 'Refunded' ? 'badge-soft-danger'
            : tx.status === 'Preparing' ? 'badge-soft-warning'
            : 'badge-soft-info';

          const canAdvance = tx.status === 'Pending' || tx.status === 'Preparing';
          const advanceBtn = canAdvance ? el('button', { class: 'btn btn-sm btn-complete order-advance-btn', onClick: async (e) => {
            e.stopPropagation();
            const prevStatus = tx.status;
            tx.status = 'Completed';
            renderList();
            try {
              await Api.post(`/api/orders/${tx.id}/advance`);
            } catch (err) {
              tx.status = prevStatus;
              renderList();
              toast(err.message, 'danger');
            }
          }, text: 'Mark as Done' }) : null;

          listEl.appendChild(el('div', { class: 'order-card', onClick: async () => {
            try { showReceipt(await Api.get('/api/orders/' + tx.id)); } catch (e) { toast(e.message, 'danger'); }
          }},
            el('div', { class: 'order-info' },
              el('div', { class: 'order-name', text: 'Order #' + String(tx.id).padStart(3, '0') }),
              el('div', { class: 'order-meta', text: dateTime(tx.timestamp) + ' \u00B7 ' + tx.itemSummary })),
            el('div', { class: 'order-right' },
              el('div', { class: 'order-total', text: money(tx.totalAmount) }),
              el('div', { class: 'order-status mt-1 d-flex align-items-center gap-2' },
                el('span', { class: 'badge ' + badge, text: tx.status }),
                advanceBtn))));
        });
      }

      async function renderQueue() {
        content.innerHTML = ''; content.appendChild(spinner());
        try { currentList = await Api.get('/api/orders/recent?take=50'); }
        catch (e) { content.innerHTML = ''; content.appendChild(empty('bi-exclamation-triangle', e.message)); return; }
        content.innerHTML = '';
        listEl = el('div', { class: 'order-queue' });
        content.appendChild(listEl);
        renderFilterChips();
        renderList();
      }

      let historyList = [];
      function renderHistoryList() {
        const filtered = historyList.filter((tx) => {
          if (filter === 'Active') return tx.status === 'Pending' || tx.status === 'Preparing';
          if (filter === 'Closed') return tx.status === 'Completed' || tx.status === 'Refunded';
          return true;
        });
        const badge = (s) => `<span class="badge ${s === 'Completed' ? 'badge-soft-success' : s === 'Refunded' ? 'badge-soft-danger' : s === 'Preparing' ? 'badge-soft-warning' : s === 'Pending' ? 'badge-soft-info' : 'badge-soft-muted'}">${esc(s)}</span>`;
        const tbody = el('tbody');
        if (!filtered.length) tbody.appendChild(el('tr', {}, el('td', { colspan: 7 }, empty('bi-receipt', 'No transactions yet.'))));
        filtered.forEach((tx) => {
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
            el('td', { text: tx.itemSummary }),
            el('td', { html: badge(tx.status) }),
            el('td', { class: 'text-end fw-semibold', text: money(tx.totalAmount) }),
            el('td', { class: 'text-end' }, actions)));
        });
        content.innerHTML = '';
        content.appendChild(el('div', { class: 'section-card p-0' },
          el('div', { class: 'table-responsive' },
            el('table', { class: 'table align-middle mb-0' },
              el('thead', {}, el('tr', {}, ...['#', 'Date & Time', 'Cashier', 'Order', 'Status', 'Total', ''].map((h) => el('th', { class: (h === 'Total' || h === '') ? 'text-end' : '', text: h })))),
              tbody))));
      }

      async function renderHistory() {
        content.innerHTML = ''; content.appendChild(spinner());
        try { historyList = await Api.get('/api/orders/recent?take=100'); }
        catch (e) { content.innerHTML = ''; content.appendChild(empty('bi-exclamation-triangle', e.message)); return; }
        renderFilterChips();
        renderHistoryList();
      }

      await renderQueue();
    },
  };

  Views.transactions = Views.activity;
})();
