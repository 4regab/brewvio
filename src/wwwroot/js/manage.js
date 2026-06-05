window.Views = window.Views || {};
(function () {
  const { el, money, qty, esc, button, toast, modal, closeModal, promptReason, spinner, empty, statCard, dateTime } = UI;

  // ---- Reusable form modal ----
  function formModal({ title, fields, values = {}, submitText = 'Save', onSubmit }) {
    const inputs = {};
    const body = el('div', {});
    fields.forEach((f) => {
      let input;
      if (f.type === 'select') {
        input = el('select', { class: 'form-select' });
        (f.options || []).forEach((o) => input.appendChild(el('option', { value: o.value, selected: String(values[f.name] ?? '') === String(o.value) }, o.label)));
      } else if (f.type === 'checkbox') {
        input = el('input', { class: 'form-check-input', type: 'checkbox' }); input.checked = values[f.name] ?? f.default ?? false;
      } else if (f.type === 'textarea') {
        input = el('textarea', { class: 'form-control', rows: f.rows || 2 }); input.value = values[f.name] ?? '';
      } else {
        input = el('input', { class: 'form-control', type: f.type || 'text', step: f.step, min: f.min });
        input.value = values[f.name] ?? f.default ?? '';
      }
      inputs[f.name] = input;
      body.appendChild(f.type === 'checkbox'
        ? el('div', { class: 'form-check form-switch mb-3' }, input, el('label', { class: 'form-check-label', text: f.label }))
        : el('div', { class: 'mb-3' }, el('label', { class: 'form-label', text: f.label }), input));
    });
    const err = el('div', { class: 'text-danger small mb-2' });
    body.appendChild(err);
    const submit = button(submitText, 'btn-primary', async () => {
      const v = {};
      for (const f of fields) {
        v[f.name] = f.type === 'checkbox' ? inputs[f.name].checked : f.type === 'number' ? Number(inputs[f.name].value || 0) : inputs[f.name].value.trim();
        if (f.required && (v[f.name] === '' || v[f.name] == null)) { err.textContent = f.label + ' is required.'; return; }
      }
      submit.disabled = true;
      try { await onSubmit(v); closeModal(); } catch (e) { err.textContent = e.message; submit.disabled = false; }
    });
    modal({ title, body, footer: [button('Cancel', 'btn-light', closeModal), submit] });
  }

  const card = (...kids) => el('div', { class: 'section-card p-0' }, ...kids);
  const tableWrap = (head, tbody) => el('div', { class: 'table-responsive' }, el('table', { class: 'table align-middle mb-0' },
    el('thead', {}, el('tr', {}, ...head.map((h) => el('th', { class: h.end ? 'text-end' : '', text: h.t })))), tbody));
  const toolbar = (...actions) => el('div', { class: 'd-flex align-items-center flex-wrap gap-2 mb-3' },
    ...actions);

  // ================= INVENTORY =================
  Views.inventory = {
    render: async (root) => {
      root.appendChild(spinner());
      let items; try { items = await Api.get('/api/inventory'); } catch (e) { root.innerHTML = ''; root.appendChild(empty('bi-exclamation-triangle', e.message)); return; }
      root.innerHTML = '';
      const isManager = App.state.user && App.state.user.role === 'Manager';
      const low = items.filter((i) => i.lowStock);
      const search = el('input', { class: 'form-control', placeholder: 'Search code, name or category…', style: 'max-width:280px' });

      const toolbarActions = [];
      if (isManager) {
        toolbarActions.push(
          button('<i class="bi bi-download"></i> Export CSV', 'btn-outline-secondary', () => Api.download('/api/inventory/export', 'inventory.csv').catch((e) => toast(e.message, 'danger'))),
          button('<i class="bi bi-plus-lg"></i> Add item', 'btn-primary', () => ingredientForm()));
      }
      root.appendChild(UI.viewToolbar('Inventory', ...toolbarActions));

      // KPI strip: total items / low / out of stock.
      const outCount = items.filter((i) => i.status === 'Out of Stock').length;
      root.appendChild(el('div', { class: 'stat-grid mb-3' },
        statCard('Total Items', String(items.length)),
        statCard('Low Stock', String(low.length - outCount < 0 ? 0 : low.filter((i) => i.status === 'Low Stock').length)),
        statCard('Out of Stock', String(outCount))));

      if (low.length) root.appendChild(el('div', { class: 'alert alert-warning d-flex align-items-center gap-2' },
        el('i', { class: 'bi bi-exclamation-triangle-fill' }), `${low.length} item(s) at or below threshold: ${low.map((i) => i.name).join(', ')}`));

      // Search row above the table
      search.style.maxWidth = '360px';
      root.appendChild(el('div', { class: 'd-flex align-items-center mb-3' }, search));

      const tbody = el('tbody');
      const statusBadge = (s) => s === 'Out of Stock' ? '<span class="badge badge-soft-danger">Out of Stock</span>'
        : s === 'Low Stock' ? '<span class="badge badge-soft-warning">Low Stock</span>'
        : '<span class="badge badge-soft-success">In Stock</span>';
      const renderRows = (list) => {
        tbody.innerHTML = '';
        if (!list.length) { tbody.appendChild(el('tr', {}, el('td', { colspan: 8 }, empty('bi-box', 'No matching items.')))); return; }
        list.forEach((i) => tbody.appendChild(el('tr', { class: i.lowStock ? 'low-stock' : '' },
          el('td', { class: 'fw-semibold text-nowrap', text: i.code || '—' }),
          el('td', { class: 'fw-semibold', text: i.name }),
          el('td', { text: i.category || '—' }),
          el('td', { class: 'text-end', text: qty(i.stockLevel) + ' ' + i.unit }),
          el('td', { class: 'text-end', text: qty(i.threshold) }),
          el('td', { html: statusBadge(i.status) }),
          el('td', { class: 'text-end' }, isManager
            ? el('div', { class: 'd-flex gap-2 justify-content-end' },
                button('Adjust', 'btn-sm btn-light', () => adjustForm(i)),
                button('Edit', 'btn-sm btn-outline-secondary', () => ingredientForm(i)))
            : el('span', { class: 'text-muted small', text: '—' })))));
      };
      renderRows(items);
      search.addEventListener('input', () => {
        const q = search.value.trim().toLowerCase();
        renderRows(items.filter((i) => !q || (i.code + ' ' + i.name + ' ' + i.category).toLowerCase().includes(q)));
      });
      root.appendChild(card(tableWrap([{ t: 'Code' }, { t: 'Name' }, { t: 'Category' }, { t: 'Stock', end: 1 }, { t: 'Threshold', end: 1 }, { t: 'Status' }, { t: '', end: 1 }], tbody)));

      function reload() { Views.inventory.render(root); }
      function ingredientForm(i) {
        formModal({
          title: i ? 'Edit item' : 'Add item',
          fields: [
            { name: 'code', label: 'Item code', required: true },
            { name: 'name', label: 'Name', required: true },
            { name: 'category', label: 'Category (Coffee, Dairy, Syrup…)' },
            { name: 'unit', label: 'Unit (ml, g, pc…)', required: true },
            ...(i ? [] : [{ name: 'stockLevel', label: 'Initial stock', type: 'number', step: '0.001' }]),
            { name: 'threshold', label: 'Low-stock threshold', type: 'number', step: '0.001' },
          ],
          values: i || {},
          onSubmit: async (v) => {
            const payload = { code: v.code, name: v.name, category: v.category, unit: v.unit, stockLevel: i ? i.stockLevel : v.stockLevel, threshold: v.threshold, costPerUnit: i ? i.costPerUnit : 0 };
            if (i) await Api.put('/api/inventory/' + i.id, payload); else await Api.post('/api/inventory', payload);
            toast('Item saved.'); reload();
          },
        });
      }
      function adjustForm(i) {
        formModal({
          title: 'Adjust stock — ' + i.name,
          fields: [
            { name: 'newQuantity', label: `New counted quantity (${i.unit})`, type: 'number', step: '0.001', default: i.stockLevel, required: true },
            { name: 'reason', label: 'Reason', type: 'textarea', required: true },
          ],
          submitText: 'Apply adjustment',
          onSubmit: async (v) => { await Api.post(`/api/inventory/${i.id}/adjust`, { newQuantity: v.newQuantity, reason: v.reason }); toast('Stock adjusted.'); reload(); },
        });
      }
    },
  };

  // ================= MENU =================
  Views.menu = {
    render: async (root) => {
      root.appendChild(spinner());
      let items, ingredients, mods;
      try { [items, ingredients, mods] = await Promise.all([Api.get('/api/menu?includeInactive=true'), Api.get('/api/inventory'), Api.get('/api/menu/modifiers?includeInactive=true')]); }
      catch (e) { root.innerHTML = ''; root.appendChild(empty('bi-exclamation-triangle', e.message)); return; }
      root.innerHTML = '';
      const reload = () => Views.menu.render(root);

      // --- Menu items ---
      root.appendChild(toolbar(button('<i class="bi bi-plus-lg"></i> Add item', 'btn-primary', () => itemForm())));
      const tb = el('tbody');
      items.forEach((m) => tb.appendChild(el('tr', {},
        el('td', { class: 'fw-semibold', text: m.name }), el('td', { text: m.category }),
        el('td', { class: 'text-end', text: money(m.price) }),
        el('td', { html: m.isActive ? '<span class="badge badge-soft-success">Active</span>' : '<span class="badge badge-soft-muted">Hidden</span>' }),
        el('td', { class: 'text-end' }, el('div', { class: 'd-flex gap-2 justify-content-end' },
          button('Edit', 'btn-sm btn-outline-secondary', () => itemForm(m)),
          button(m.isActive ? 'Hide' : 'Show', 'btn-sm btn-light', async () => { await Api.post(`/api/menu/${m.id}/active?active=${!m.isActive}`); reload(); }))))));
      root.appendChild(card(tableWrap([{ t: 'Name' }, { t: 'Category' }, { t: 'Price', end: 1 }, { t: 'Status' }, { t: '', end: 1 }], tb)));

      // --- Modifiers ---
      root.appendChild(el('div', { class: 'mt-4' }, toolbar('Modifiers', button('<i class="bi bi-plus-lg"></i> Add modifier', 'btn-primary', () => modifierForm()))));
      const mtb = el('tbody');
      mods.forEach((m) => mtb.appendChild(el('tr', {},
        el('td', { class: 'fw-semibold', text: m.name }), el('td', { text: m.groupName }),
        el('td', { class: 'text-end', text: money(m.priceDelta) }),
        el('td', { html: m.isActive ? '<span class="badge badge-soft-success">Active</span>' : '<span class="badge badge-soft-muted">Hidden</span>' }),
        el('td', { class: 'text-end' }, button('Edit', 'btn-sm btn-outline-secondary', () => modifierForm(m))))));
      root.appendChild(card(tableWrap([{ t: 'Name' }, { t: 'Group' }, { t: 'Price +/-', end: 1 }, { t: 'Status' }, { t: '', end: 1 }], mtb)));

      function modifierForm(m) {
        formModal({
          title: m ? 'Edit modifier' : 'Add modifier',
          fields: [
            { name: 'name', label: 'Name', required: true }, { name: 'groupName', label: 'Group (Milk, Syrup…)', required: true },
            { name: 'priceDelta', label: 'Price change', type: 'number', step: '0.01' },
            { name: 'isActive', label: 'Active', type: 'checkbox', default: true },
          ], values: m || {},
          onSubmit: async (v) => { if (m) await Api.put('/api/menu/modifiers/' + m.id, v); else await Api.post('/api/menu/modifiers', v); toast('Modifier saved.'); reload(); },
        });
      }

      function itemForm(item) {
        const v = item || { name: '', category: '', price: 0, isActive: true, recipe: [] };
        const nameIn = el('input', { class: 'form-control', value: v.name });
        const catIn = el('input', { class: 'form-control', value: v.category, list: 'cat-list' });
        const catList = el('datalist', { id: 'cat-list' }, ...Array.from(new Set(items.map((i) => i.category))).map((c) => el('option', { value: c })));
        const priceIn = el('input', { class: 'form-control', type: 'number', step: '0.01', value: v.price });
        const activeIn = el('input', { class: 'form-check-input', type: 'checkbox' }); activeIn.checked = v.isActive;
        const recipeWrap = el('div', {});
        const addRow = (line) => {
          const sel = el('select', { class: 'form-select' }, ...ingredients.map((i) => el('option', { value: i.id, selected: line && line.ingredientId === i.id }, `${i.name} (${i.unit})`)));
          const qtyIn = el('input', { class: 'form-control', type: 'number', step: '0.001', min: '0', value: line ? line.quantity : '' });
          const row = el('div', { class: 'd-flex gap-2 mb-2' }, el('div', { class: 'flex-grow-1' }, sel), el('div', { style: 'width:120px' }, qtyIn));
          row.appendChild(button('<i class="bi bi-x-lg"></i>', 'btn-outline-danger', () => row.remove()));
          row._get = () => ({ ingredientId: Number(sel.value), quantity: Number(qtyIn.value) || 0 });
          recipeWrap.appendChild(row);
        };
        (v.recipe || []).forEach(addRow);
        const err = el('div', { class: 'text-danger small mb-2' });
        const body = el('div', {}, catList,
          el('div', { class: 'mb-3' }, el('label', { class: 'form-label', text: 'Name' }), nameIn),
          el('div', { class: 'row g-2 mb-3' }, el('div', { class: 'col-7' }, el('label', { class: 'form-label', text: 'Category' }), catIn), el('div', { class: 'col-5' }, el('label', { class: 'form-label', text: 'Price' }), priceIn)),
          el('div', { class: 'form-check form-switch mb-3' }, activeIn, el('label', { class: 'form-check-label', text: 'Active (visible on POS)' })),
          el('label', { class: 'form-label', text: 'Recipe (auto-deducted per sale)' }), recipeWrap,
          button('<i class="bi bi-plus"></i> Add ingredient', 'btn-sm btn-light mb-2', () => addRow()), err);
        const submit = button('Save', 'btn-primary', async () => {
          if (!nameIn.value.trim()) { err.textContent = 'Name is required.'; return; }
          const recipe = Array.from(recipeWrap.children).filter((r) => r._get).map((r) => r._get()).filter((x) => x.quantity > 0);
          const payload = { name: nameIn.value.trim(), category: catIn.value.trim() || 'Uncategorized', price: Number(priceIn.value) || 0, isActive: activeIn.checked, recipe };
          submit.disabled = true;
          try { if (item) await Api.put('/api/menu/' + item.id, payload); else await Api.post('/api/menu', payload); closeModal(); toast('Menu item saved.'); reload(); }
          catch (e) { err.textContent = e.message; submit.disabled = false; }
        });
        modal({ title: item ? 'Edit item' : 'Add item', body, footer: [button('Cancel', 'btn-light', closeModal), submit] });
      }
    },
  };

  // ================= USERS =================
  Views.users = {
    render: async (root) => {
      root.appendChild(spinner());
      let users, pending;
      try { [users, pending] = await Promise.all([Api.get('/api/users'), Api.get('/api/users/pending')]); }
      catch (e) { root.innerHTML = ''; root.appendChild(empty('bi-exclamation-triangle', e.message)); return; }
      root.innerHTML = '';
      const reload = () => Views.users.render(root);
      const roleOpts = [{ value: 'Cashier', label: 'Cashier' }, { value: 'Manager', label: 'Manager' }];

      root.appendChild(toolbar(button('<i class="bi bi-person-plus"></i> Add user', 'btn-primary', () => formModal({
        title: 'Add user',
        fields: [{ name: 'username', label: 'Username', required: true }, { name: 'fullName', label: 'Full name' },
          { name: 'password', label: 'Password', type: 'password', required: true }, { name: 'role', label: 'Role', type: 'select', options: roleOpts }],
        onSubmit: async (v) => { await Api.post('/api/users', v); toast('User created.'); reload(); },
      }))));

      // ---- Pending approval queue (registration/approval workflow) ----
      if (pending.length) {
        const banner = el('div', { class: 'pending-banner' },
          el('div', { class: 'd-flex align-items-center gap-2 mb-1' },
            el('i', { class: 'bi bi-person-fill-exclamation', style: 'font-size:1.2rem;color:#8a6314' }),
            el('h2', { class: 'h6 mb-0', text: `${pending.length} account${pending.length > 1 ? 's' : ''} awaiting approval` })));
        pending.forEach((p) => banner.appendChild(el('div', { class: 'pending-row' },
          el('div', { class: 'pending-avatar', text: (p.fullName || p.username).slice(0, 1).toUpperCase() }),
          el('div', {}, el('div', { class: 'fw-semibold', text: p.fullName || p.username }),
            el('div', { class: 'text-secondary small', text: `@${p.username} · requested ${p.role} · ${dateTime(p.createdAt)}` })),
          el('div', { class: 'ms-auto d-flex gap-2' },
            button('<i class="bi bi-check-lg"></i> Approve', 'btn-sm btn-primary', async () => {
              try { await Api.post(`/api/users/${p.id}/approve`); toast(`${p.username} approved.`); reload(); } catch (e) { toast(e.message, 'danger'); }
            }),
            button('<i class="bi bi-x-lg"></i> Reject', 'btn-sm btn-outline-danger', async () => {
              if (!confirm(`Reject ${p.username}? This will permanently deny their account.`)) return;
              try { await Api.post(`/api/users/${p.id}/reject`); toast(`${p.username} rejected.`, 'warning'); reload(); } catch (e) { toast(e.message, 'danger'); }
            })))));
        root.appendChild(banner);
      }

      const statusBadge = (u) => {
        if (u.status === 'Pending') return '<span class="badge badge-soft-warning">Pending</span>';
        if (u.status === 'Rejected') return '<span class="badge badge-soft-danger">Rejected</span>';
        return u.isActive ? '<span class="badge badge-soft-success">Active</span>' : '<span class="badge badge-soft-danger">Disabled</span>';
      };
      const tbody = el('tbody');
      users.forEach((u) => tbody.appendChild(el('tr', {},
        el('td', { class: 'fw-semibold', text: u.username }), el('td', { text: u.fullName || '—' }),
        el('td', { html: `<span class="badge ${u.role === 'Manager' ? 'badge-soft-success' : 'badge-soft-muted'}">${esc(u.role)}</span>` }),
        el('td', { html: statusBadge(u) }),
        el('td', { text: dateTime(u.createdAt) }),
        el('td', { class: 'text-end' }, el('div', { class: 'd-flex gap-2 justify-content-end' },
          button('Edit', 'btn-sm btn-outline-secondary', () => formModal({
            title: 'Edit ' + u.username,
            fields: [{ name: 'fullName', label: 'Full name' }, { name: 'role', label: 'Role', type: 'select', options: roleOpts }, { name: 'isActive', label: 'Active', type: 'checkbox' }],
            values: u, onSubmit: async (v) => { await Api.put('/api/users/' + u.id, v); toast('User updated.'); reload(); },
          })),
          button('Reset password', 'btn-sm btn-light', async () => {
            const pwd = await promptReason({ title: 'Reset password — ' + u.username, label: 'New password (min 6 chars)', placeholder: 'New password', confirmText: 'Reset', minLength: 6 });
            if (pwd == null) return;
            try { await Api.post(`/api/users/${u.id}/reset-password`, { newPassword: pwd }); toast('Password reset.'); } catch (e) { toast(e.message, 'danger'); }
          }),
          button('Delete', 'btn-sm btn-outline-danger', async () => {
            const reason = await promptReason({ title: 'Delete ' + u.username, label: 'Are you sure? This cannot be undone. Type "delete" to confirm.', placeholder: 'delete', confirmText: 'Delete user', confirmClass: 'btn-danger', minLength: 6 });
            if (reason == null || reason.toLowerCase() !== 'delete') { if (reason != null) toast('Type "delete" to confirm.', 'warning'); return; }
            try { await Api.post(`/api/users/${u.id}/delete`, {}); toast('User deleted.'); reload(); } catch (e) { toast(e.message, 'danger'); }
          }))))));
      root.appendChild(card(tableWrap([{ t: 'Username' }, { t: 'Full name' }, { t: 'Role' }, { t: 'Status' }, { t: 'Created' }, { t: '', end: 1 }], tbody)));
    },
  };

  // ================= AUDIT =================
  Views.audit = {
    render: async (root) => {
      root.appendChild(spinner());
      let logs; try { logs = await Api.get('/api/audit?take=300'); } catch (e) { root.innerHTML = ''; root.appendChild(empty('bi-exclamation-triangle', e.message)); return; }
      root.innerHTML = '';
      root.appendChild(toolbar('Audit log'));
      const tbody = el('tbody');
      if (!logs.length) tbody.appendChild(el('tr', {}, el('td', { colspan: 4 }, empty('bi-shield-check', 'No audit entries yet.'))));
      logs.forEach((a) => tbody.appendChild(el('tr', {},
        el('td', { class: 'text-nowrap', text: dateTime(a.timestamp) }), el('td', { text: a.username }),
        el('td', { html: `<span class="badge badge-soft-muted">${esc(a.action)}</span>` }), el('td', { text: a.details }))));
      root.appendChild(card(tableWrap([{ t: 'Time' }, { t: 'User' }, { t: 'Action' }, { t: 'Details' }], tbody)));
    },
  };

  // ================= SETTINGS =================
  Views.settings = {
    render: async (root) => {
      root.appendChild(spinner());
      let s; try { s = await Api.get('/api/settings'); } catch (e) { root.innerHTML = ''; root.appendChild(empty('bi-exclamation-triangle', e.message)); return; }
      root.innerHTML = '';
      const f = {
        storeName: el('input', { class: 'form-control', value: s.storeName }),
        address: el('input', { class: 'form-control', value: s.address }),
        currency: el('input', { class: 'form-control', value: s.currency }),
        taxRatePercent: el('input', { class: 'form-control', type: 'number', step: '0.01', min: '0', value: s.taxRatePercent }),
      };
      const save = button('Save settings', 'btn-primary', async () => {
        try {
          const dto = { storeName: f.storeName.value.trim(), address: f.address.value.trim(), currency: f.currency.value.trim() || 'PHP', taxRatePercent: Number(f.taxRatePercent.value) || 0 };
          await Api.put('/api/settings', dto);
          window.App.store = { storeName: dto.storeName, address: dto.address, currency: dto.currency, taxRatePercent: dto.taxRatePercent };
          UI.setCurrency(dto.currency);
          const storeEl = document.getElementById('sidebar-store');
          if (storeEl) storeEl.textContent = dto.storeName;
          toast('Settings saved.');
        } catch (e) { toast(e.message, 'danger'); }
      });
      root.appendChild(el('div', { class: 'row g-3' },
        el('div', { class: 'col-lg-7' }, el('div', { class: 'section-card p-4' },
          el('h2', { class: 'h5 mb-3', text: 'Store settings' }),
          el('div', { class: 'mb-3' }, el('label', { class: 'form-label', text: 'Store name' }), f.storeName),
          el('div', { class: 'mb-3' }, el('label', { class: 'form-label', text: 'Address' }), f.address),
          el('div', { class: 'row g-2 mb-3' },
            el('div', { class: 'col-6' }, el('label', { class: 'form-label', text: 'Currency' }), f.currency),
            el('div', { class: 'col-6' }, el('label', { class: 'form-label', text: 'Tax rate (%)' }), f.taxRatePercent)),
          save)),
        el('div', { class: 'col-lg-5' }, el('div', { class: 'section-card p-4' },
          el('h2', { class: 'h5 mb-3', text: 'Data backup' }),
          el('p', { class: 'text-secondary small', text: 'Download a full JSON snapshot of the database (users, menu, inventory, transactions, audit log) for safekeeping.' }),
          button('<i class="bi bi-cloud-download"></i> Download backup', 'btn-outline-secondary', () => Api.download('/api/settings/backup', 'chaobrew-backup.json').catch((e) => toast(e.message, 'danger')))))));
    },
  };
})();
