window.Views = window.Views || {};
(function () {
  const { el, money, button, toast, spinner, empty, statCard, lineChart, barChart, doughnutChart } = UI;

  const isoDaysAgo = (n) => { const d = new Date(); d.setDate(d.getDate() - n); return d.toISOString().slice(0, 10); };

  const PERIODS = {
    daily: { label: 'Daily', days: 6 },
    weekly: { label: 'Weekly', days: 7 * 8 - 1 },
    monthly: { label: 'Monthly', days: 365 },
    yearly: { label: 'Yearly', days: 365 * 3 },
  };

  function segControl(active, onPick) {
    const seg = el('div', { class: 'seg-control' });
    Object.entries(PERIODS).forEach(([key, p]) =>
      seg.appendChild(el('button', { class: active === key ? 'active' : '', text: p.label, onClick: () => onPick(key) })));
    return seg;
  }

  // ================= CONSOLIDATED REPORTS VIEW =================
  Views.reports = {
    render: async (root) => {
      root.innerHTML = '';
      let activeTab = 'sales';

      const tabSales = el('button', { class: 'activity-tab active', text: 'Sales Report', onClick: () => switchTab('sales') });
      const tabPerf = el('button', { class: 'activity-tab', text: 'Menu Performance', onClick: () => switchTab('performance') });
      const tabs = el('div', { class: 'activity-tabs' }, tabSales, tabPerf);
      const content = el('div', { class: 'mt-3' });
      root.appendChild(tabs);
      root.appendChild(content);

      function switchTab(tab) {
        activeTab = tab;
        tabSales.classList.toggle('active', tab === 'sales');
        tabPerf.classList.toggle('active', tab === 'performance');
        if (tab === 'sales') renderSales(); else renderPerformance();
      }

      // ---- Sales Report ----
      async function renderSales() {
        content.innerHTML = '';
        let chart = null, catChart = null;
        let period = 'daily';
        const fromIn = el('input', { type: 'date', class: 'form-control', value: isoDaysAgo(PERIODS[period].days) });
        const toIn = el('input', { type: 'date', class: 'form-control', value: isoDaysAgo(0) });
        const results = el('div', { class: 'mt-3' });
        const qs = () => `?from=${fromIn.value}&to=${toIn.value}&period=${period}`;

        const load = async () => {
          results.innerHTML = ''; results.appendChild(spinner('Crunching numbers...'));
          try {
            const r = await Api.get('/api/reports' + qs());
            if (chart) { chart.destroy(); chart = null; }
            if (catChart) { catChart.destroy(); catChart = null; }
            results.innerHTML = '';
            if (r.summary.transactionCount === 0) { results.appendChild(empty('bi-bar-chart', 'No transactions found for the selected range.')); return; }

            results.appendChild(el('div', { class: 'stat-grid mb-3' },
              statCard('Total Sales', money(r.summary.totalSales)),
              statCard('Transactions', String(r.summary.transactionCount)),
              statCard('Items Sold', String(r.summary.itemsSold)),
              statCard('Avg. Order Value', money(r.summary.averageOrderValue)),
              statCard('Gross Profit', money(r.summary.grossProfit), { dir: r.summary.grossProfit >= 0 ? 'up' : 'down', text: r.summary.profitMarginPercent + '% margin' }),
              statCard('Total Tax', money(r.summary.totalTax))));

            const trendCanvas = el('canvas');
            const catCanvas = el('canvas');
            results.appendChild(el('div', { class: 'row g-3' },
              el('div', { class: 'col-lg-8' }, el('div', { class: 'section-card p-3 h-100' },
                el('h3', { class: 'h6 mb-3', text: `Revenue Trend` }),
                el('div', { style: 'position:relative;height:280px' }, trendCanvas))),
              el('div', { class: 'col-lg-4' }, el('div', { class: 'section-card p-3 h-100' },
                el('h3', { class: 'h6 mb-3', text: 'Sales by Category' }),
                el('div', { style: 'position:relative;height:280px' }, catCanvas)))));
            chart = lineChart(trendCanvas, r.trend.map((t) => t.label), r.trend.map((t) => t.sales), 'Sales');
            catChart = doughnutChart(catCanvas, r.categoryBreakdown.map((c) => c.category), r.categoryBreakdown.map((c) => c.revenue));

            const tbody = el('tbody');
            r.menuPerformance.forEach((m) => tbody.appendChild(el('tr', {},
              el('td', { text: m.name }), el('td', { text: m.category }),
              el('td', { class: 'text-end', text: m.quantitySold }),
              el('td', { class: 'text-end', text: money(m.revenue) }),
              el('td', { class: 'text-end', text: money(m.cost) }),
              el('td', { class: 'text-end fw-semibold', text: money(m.profit) }),
              el('td', { class: 'text-end', text: m.marginPercent + '%' }))));
            results.appendChild(el('div', { class: 'section-card p-0 mt-3' },
              el('div', { class: 'p-3 border-bottom' }, el('h3', { class: 'h6 mb-0', text: 'All Orders' })),
              el('div', { class: 'table-responsive' }, el('table', { class: 'table align-middle mb-0' },
                el('thead', {}, el('tr', {}, ...['Item', 'Category', 'Qty Sold', 'Revenue', 'Cost', 'Profit', 'Margin'].map((h, i) => el('th', { class: i >= 2 ? 'text-end' : '', text: h })))),
                tbody))));
          } catch (e) { results.innerHTML = ''; results.appendChild(empty('bi-exclamation-triangle', e.message)); }
        };

        const pickPeriod = (key) => {
          period = key;
          fromIn.value = isoDaysAgo(PERIODS[key].days);
          toIn.value = isoDaysAgo(0);
          head.replaceChild(segControl(period, pickPeriod), head.querySelector('.seg-control'));
          load();
        };

        const head = el('div', { class: 'd-flex flex-wrap gap-3 align-items-end' },
          segControl(period, pickPeriod),
          el('div', { class: 'd-flex gap-2 align-items-end flex-wrap' },
            el('div', {}, el('label', { class: 'form-label small mb-1', text: 'From' }), fromIn),
            el('div', {}, el('label', { class: 'form-label small mb-1', text: 'To' }), toIn),
            button('<i class="bi bi-arrow-repeat"></i> Generate', 'btn-primary', load)),
          el('div', { class: 'd-flex gap-2 ms-auto' },
            button('<i class="bi bi-filetype-csv"></i> CSV', 'btn-outline-secondary btn-sm', () => Api.download('/api/reports/export/csv' + qs(), 'sales.csv').catch((e) => toast(e.message, 'danger'))),
            button('<i class="bi bi-filetype-pdf"></i> PDF', 'btn-outline-secondary btn-sm', () => Api.download('/api/reports/export/pdf' + qs(), 'sales.pdf').catch((e) => toast(e.message, 'danger')))));

        content.appendChild(el('div', { class: 'section-card p-3' }, head));
        content.appendChild(results);
        await load();
      }

      // ---- Menu Performance ----
      async function renderPerformance() {
        content.innerHTML = '';
        let catChart = null, period = 'monthly';
        const results = el('div', { class: 'mt-3' });
        const qs = () => `?from=${isoDaysAgo(PERIODS[period].days)}&to=${isoDaysAgo(0)}&period=${period}`;

        const rankList = (title, list, accent) => {
          const wrap = el('div', { class: 'rank-list' });
          if (!list.length) wrap.appendChild(empty('bi-cup', 'No data yet.'));
          list.forEach((m, idx) => wrap.appendChild(el('div', { class: 'rank-item' + (accent ? ' top-' + (idx + 1) : '') },
            el('div', { class: 'rank-badge', text: String(idx + 1) }),
            el('div', {}, el('div', { class: 'rank-name', text: m.name }), el('div', { class: 'rank-meta', text: m.category })),
            el('div', { class: 'rank-val' }, el('div', { class: 'v', text: m.quantitySold + ' sold' }), el('div', { class: 's', text: money(m.revenue) })))));
          return el('div', { class: 'section-card p-3 h-100' }, el('h3', { class: 'h6 mb-3', text: title }), wrap);
        };

        const load = async () => {
          results.innerHTML = ''; results.appendChild(spinner('Analyzing menu...'));
          try {
            const r = await Api.get('/api/reports' + qs());
            if (catChart) { catChart.destroy(); catChart = null; }
            results.innerHTML = '';
            if (r.summary.transactionCount === 0) { results.appendChild(empty('bi-bar-chart', 'No sales in this period yet.')); return; }

            results.appendChild(el('div', { class: 'row g-3' },
              el('div', { class: 'col-lg-6' }, rankList('Best Sellers', r.bestSellers, true)),
              el('div', { class: 'col-lg-6' }, rankList('Slow Sellers', r.slowSellers, false))));

            const catCanvas = el('canvas');
            results.appendChild(el('div', { class: 'section-card p-3 mt-3' },
              el('h3', { class: 'h6 mb-3', text: 'Units Sold by Category' }),
              el('div', { style: 'position:relative;height:280px' }, catCanvas)));
            catChart = barChart(catCanvas, r.categoryBreakdown.map((c) => c.category), r.categoryBreakdown.map((c) => c.quantitySold), 'Units');

            const byProfit = [...r.menuPerformance].sort((a, b) => b.profit - a.profit);
            const tbody = el('tbody');
            byProfit.forEach((m) => tbody.appendChild(el('tr', {},
              el('td', { text: m.name }), el('td', { text: m.category }),
              el('td', { class: 'text-end', text: m.quantitySold }),
              el('td', { class: 'text-end', text: money(m.revenue) }),
              el('td', { class: 'text-end fw-semibold', text: money(m.profit) }),
              el('td', { class: 'text-end' }, el('span', { class: 'badge ' + (m.marginPercent >= 50 ? 'badge-soft-success' : m.marginPercent >= 25 ? 'badge-soft-warning' : 'badge-soft-danger'), text: m.marginPercent + '%' })))));
            results.appendChild(el('div', { class: 'section-card p-0 mt-3' },
              el('div', { class: 'p-3 border-bottom' }, el('h3', { class: 'h6 mb-0', text: 'Profitability by Item' })),
              el('div', { class: 'table-responsive' }, el('table', { class: 'table align-middle mb-0' },
                el('thead', {}, el('tr', {}, ...['Item', 'Category', 'Qty', 'Revenue', 'Profit', 'Margin'].map((h, i) => el('th', { class: i >= 2 ? 'text-end' : '', text: h })))),
                tbody))));
          } catch (e) { results.innerHTML = ''; results.appendChild(empty('bi-exclamation-triangle', e.message)); }
        };

        const pickPeriod = (key) => { period = key; head.replaceChild(segControl(period, pickPeriod), head.querySelector('.seg-control')); load(); };
        const head = el('div', { class: 'd-flex flex-wrap gap-3 align-items-center' },
          segControl(period, pickPeriod));

        content.appendChild(el('div', { class: 'section-card p-3' }, head));
        content.appendChild(results);
        await load();
      }

      await renderSales();
    },
  };

  // Keep performance view for backward compat
  Views.performance = Views.reports;
})();
