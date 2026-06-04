window.Views = window.Views || {};
(function () {
  const { el, money, button, toast, spinner, dateTime } = UI;

  function stat(label, value, strong) {
    return el('div', { class: 'd-flex justify-content-between py-1 ' + (strong ? 'fs-5 fw-bold text-coffee' : '') },
      el('span', { class: strong ? '' : 'text-secondary', text: label }), el('span', { text: value }));
  }

  Views.shift = {
    render: async (root) => {
      root.appendChild(spinner());
      let shift;
      try { shift = await Api.get('/api/shifts/current'); }
      catch (e) { root.innerHTML = ''; root.appendChild(UI.empty('bi-exclamation-triangle', e.message)); return; }
      root.innerHTML = '';

      if (!shift) {
        const cashIn = el('input', { type: 'number', min: '0', step: '0.01', class: 'form-control form-control-lg', value: '0.00' });
        root.appendChild(el('div', { class: 'section-card p-4 mx-auto', style: 'max-width:460px' },
          el('div', { class: 'text-center mb-3' }, el('i', { class: 'bi bi-clock-history text-coffee', style: 'font-size:2.4rem' }),
            el('h3', { class: 'h5 mt-2 mb-0', text: 'No active shift' }), el('p', { class: 'text-secondary small', text: 'Start a shift to begin taking orders.' })),
          el('label', { class: 'form-label', text: 'Starting cash in drawer' }), cashIn,
          button('Start shift', 'btn-primary btn-lg w-100 mt-3', async () => {
            try { await Api.post('/api/shifts/start', { startingCash: Number(cashIn.value) || 0 }); toast('Shift started.'); Views.shift.render(root); }
            catch (e) { toast(e.message, 'danger'); }
          })));
        return;
      }

      const endIn = el('input', { type: 'number', min: '0', step: '0.01', class: 'form-control form-control-lg', value: shift.expectedCash.toFixed(2) });
      root.appendChild(el('div', { class: 'row g-3' },
        el('div', { class: 'col-lg-6' }, el('div', { class: 'section-card p-4 h-100' },
          el('div', { class: 'd-flex align-items-center gap-2 mb-3' }, el('span', { class: 'badge badge-soft-success', text: 'OPEN' }), el('h3', { class: 'h5 mb-0', text: 'Current shift' })),
          stat('Cashier', shift.cashier), stat('Started', dateTime(shift.startTime)),
          stat('Starting cash', money(shift.startingCash)), el('hr'),
          stat('Transactions', String(shift.transactionCount)), stat('Total sales', money(shift.totalSales)),
          stat('Cash sales', money(shift.cashSales)), stat('Card sales', money(shift.cardSales)), el('hr'),
          stat('Expected cash in drawer', money(shift.expectedCash), true))),
        el('div', { class: 'col-lg-6' }, el('div', { class: 'section-card p-4 h-100' },
          el('h3', { class: 'h5 mb-3', text: 'End shift' }),
          el('p', { class: 'text-secondary small', text: 'Count the drawer and enter the actual cash to close the shift.' }),
          el('label', { class: 'form-label', text: 'Counted cash in drawer' }), endIn,
          button('End shift', 'btn-primary btn-lg w-100 mt-3', async () => {
            try {
              const closed = await Api.post('/api/shifts/end', { endingCash: Number(endIn.value) || 0 });
              const v = closed.cashVariance || 0;
              toast(`Shift closed. Cash variance: ${money(v)}.`, Math.abs(v) < 0.01 ? 'success' : 'warning');
              Views.shift.render(root);
            } catch (e) { toast(e.message, 'danger'); }
          })))));
    },
  };
})();
