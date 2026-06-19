window.Views = window.Views || {};
(function () {
  const { el, money, button, toast, spinner, empty, lineChart, barChart, dateTime } = UI;

  const isoDaysAgo = (n) => { const d = new Date(); d.setDate(d.getDate() - n); return d.toISOString().slice(0, 10); };

  const PERIODS = {
    daily:   { label: 'Daily',   days: 30 },
    weekly:  { label: 'Weekly',  days: 7 * 12 - 1 },
    monthly: { label: 'Monthly', days: 365 },
    yearly:  { label: 'Yearly',  days: 365 * 3 },
  };

  // ── Helper: icon stat card matching the reference ──
  function kpiCard(icon, label, value, unit, delta, pct) {
    const deltaEl = (delta != null || pct != null)
      ? el('div', { class: 'rpt-kpi-delta ' + ((delta != null ? delta >= 0 : pct >= 0) ? 'up' : 'down') },
          delta != null ? el('span', { class: 'rpt-kpi-delta-val', text: (delta >= 0 ? '+ ' : '- ') + money(Math.abs(delta)) }) : null,
          pct != null ? el('span', { class: 'rpt-kpi-pct', text: Number(Math.abs(pct)).toFixed(1) + '% ' + (pct >= 0 ? '↑' : '↓') }) : null)
      : null;
    return el('div', { class: 'rpt-kpi' },
      el('div', { class: 'rpt-kpi-head' },
        el('i', { class: 'bi ' + icon }),
        el('span', { class: 'rpt-kpi-label', text: label })),
      el('div', { class: 'rpt-kpi-row' },
        el('span', { class: 'rpt-kpi-value', text: value }),
        unit ? el('span', { class: 'rpt-kpi-unit', text: unit }) : null),
      deltaEl);
  }

  // ── Helper: period pill selector ──
  function periodPill(active, onPick) {
    const wrap = el('div', { class: 'rpt-period-wrap' },
      el('span', { class: 'rpt-period-label', text: 'Date Period:' }));
    Object.entries(PERIODS).forEach(([key, p]) =>
      wrap.appendChild(el('button', {
        class: 'rpt-period-btn' + (active === key ? ' active' : ''),
        text: p.label,
        onClick: () => onPick(key),
      })));
    return wrap;
  }

  // ================= REPORTS VIEW =================
  Views.reports = {
    render: async (root) => {
      root.innerHTML = '';
      let chart = null;
      let period = 'daily';
      let currentQs = '';
      const fromIn = el('input', { type: 'date', class: 'form-control form-control-sm', value: isoDaysAgo(PERIODS['daily'].days) });
      const toIn = el('input', { type: 'date', class: 'form-control form-control-sm', value: isoDaysAgo(0) });

      const qs = () => `?from=${fromIn.value}&to=${toIn.value}&period=${period}`;

      // Download button — lives in the period controls row
      const downloadBtn = el('div', { class: 'rpt-download-pill' },
        el('span', { text: 'Download' }),
        el('button', { class: 'rpt-dl-icon', title: 'Download CSV',
          onClick: () => Api.download('/api/reports/export/csv' + currentQs, 'sales.xlsx').catch((e) => toast(e.message, 'danger')) },
          el('i', { class: 'bi bi-download' })));

      // No viewToolbar — title is in the topbar, no Show Graph toggle needed
      const periodRow = el('div', { class: 'rpt-controls' });
      const kpiGrid = el('div', { class: 'rpt-kpi-grid' });
      const graphSection = el('div', { class: 'rpt-graph-section' });
      const ordersSection = el('div', { class: 'rpt-orders-section' });

      root.appendChild(periodRow);
      root.appendChild(kpiGrid);
      root.appendChild(graphSection);
      root.appendChild(ordersSection);

      function rebuildPeriodRow() {
        periodRow.innerHTML = '';
        periodRow.appendChild(periodPill(period, pickPeriod));
        periodRow.appendChild(downloadBtn);
      }

      function pickPeriod(key) {
        period = key;
        fromIn.value = isoDaysAgo(PERIODS[key].days);
        toIn.value = isoDaysAgo(0);
        rebuildPeriodRow();
        load();
      }

      async function load() {
        currentQs = qs();
        kpiGrid.innerHTML = ''; kpiGrid.appendChild(spinner());
        graphSection.innerHTML = '';
        ordersSection.innerHTML = '';

        try {
          const r = await Api.get('/api/reports' + currentQs);
          if (chart) { chart.destroy(); chart = null; }
          kpiGrid.innerHTML = '';

          if (r.summary.transactionCount === 0) {
            kpiGrid.appendChild(empty('bi-bar-chart', 'No transactions found for the selected range.'));
            return;
          }

          const s = r.summary;

          // KPI cards — real data only, no fake growth placeholders
          kpiGrid.appendChild(kpiCard('bi-graph-up-arrow', 'Monthly Sales Amount', money(s.totalSales), null, null, null));
          kpiGrid.appendChild(kpiCard('bi-calendar-day', 'Sales Today', money(s.salesToday), null, null, null));
          kpiGrid.appendChild(kpiCard('bi-receipt', 'Total Transactions', String(s.transactionCount), 'Orders', null, null));

          // Graph section: always visible
          {
            graphSection.style.display = '';
            const trendCanvas = el('canvas');

            // Metric selector dropdown for the chart
            const metricSelect = el('select', { class: 'rpt-chart-select', onChange: () => switchMetric() });
            [{ key: 'sales', label: 'Total Sales Amount' }, { key: 'transactions', label: 'Transaction Count' }].forEach((opt) =>
              metricSelect.appendChild(el('option', { value: opt.key, text: opt.label })));

            const switchMetric = () => {
              if (chart) chart.destroy();
              const metric = metricSelect.value;
              const labels = r.trend.map((t) => t.label);
              const values = metric === 'sales'
                ? r.trend.map((t) => t.sales)
                : r.trend.map((t) => t.transactionCount);
              const chartLabel = metric === 'sales' ? 'Sales' : 'Transactions';
              // Bar for daily (one bar per day is industry standard), line for broader periods
              chart = period === 'daily'
                ? barChart(trendCanvas, labels, values, chartLabel)
                : lineChart(trendCanvas, labels, values, chartLabel);
            };

            const chartCol = el('div', { class: 'rpt-chart-col' },
              el('div', { class: 'rpt-chart-head' },
                el('div', { class: 'rpt-chart-title' },
                  el('span', { class: 'rpt-dot' }),
                  el('span', { text: 'Report Graph' })),
                el('div', { class: 'rpt-chart-dropdown-wrap' }, metricSelect)),
              el('div', { class: 'rpt-chart-canvas' }, trendCanvas));

            // Favorite products column with header pills
            const favCol = el('div', { class: 'rpt-fav-col' },
              el('div', { class: 'rpt-fav-head' },
                el('div', { class: 'rpt-fav-title' },
                  el('span', { class: 'rpt-dot' }),
                  el('span', { text: 'Favorite Product' })),
                el('button', { class: 'rpt-fav-search', type: 'button' }, el('i', { class: 'bi bi-search' }))),
              el('div', { class: 'rpt-fav-cols' },
                el('span', { class: 'rpt-fav-col-pill', text: 'Img' }),
                el('span', { class: 'rpt-fav-col-pill', text: 'Product Name' }),
                el('span', { class: 'rpt-fav-col-pill', text: 'Total Orders' })),
              buildFavList(r.bestSellers));

            graphSection.appendChild(el('div', { class: 'rpt-unified-card' },
              el('div', { class: 'rpt-graph-grid' }, chartCol, favCol)));
            // Initial render — bar for daily, line for broader periods
            chart = period === 'daily'
              ? barChart(trendCanvas, r.trend.map((t) => t.label), r.trend.map((t) => t.sales), 'Sales')
              : lineChart(trendCanvas, r.trend.map((t) => t.label), r.trend.map((t) => t.sales), 'Sales');
          }

          // All Orders table — real transactions with actual order numbers
          await buildOrdersTable(ordersSection);

        } catch (e) {
          kpiGrid.innerHTML = '';
          kpiGrid.appendChild(empty('bi-exclamation-triangle', e.message));
        }
      }

      function buildFavList(sellers) {
        const list = el('div', { class: 'rpt-fav-list' });
        if (!sellers || !sellers.length) { list.appendChild(empty('bi-cup', 'No data.')); return list; }
        sellers.slice(0, 5).forEach((item) => {
          list.appendChild(el('div', { class: 'rpt-fav-item' },
            el('div', { class: 'rpt-fav-img' },
              el('img', { src: menuImageByName(item.name, item.category), alt: item.name, loading: 'lazy' })),
            el('div', { class: 'rpt-fav-info' },
              el('div', { class: 'rpt-fav-name', text: item.name }),
              el('div', { class: 'rpt-fav-cat', text: item.category })),
            el('div', { class: 'rpt-fav-count', text: item.quantitySold + ' Times' })));
        });
        return list;
      }

      async function buildOrdersTable(container) {
        container.innerHTML = '';
        const head = el('div', { class: 'rpt-orders-head' },
          el('div', { class: 'rpt-orders-title' },
            el('span', { class: 'rpt-dot' }),
            el('span', { text: 'All Orders' })),
          el('div', { class: 'rpt-orders-filters' },
            el('span', { text: 'Date:' }), fromIn,
            el('span', { class: 'rpt-filter-sep', text: '—' }), toIn,
            button('<i class="bi bi-search"></i>', 'btn-sm btn-outline-secondary', load)));

        const tbody = el('tbody');
        const card = el('div', { class: 'rpt-orders-card' },
          head,
          el('div', { class: 'table-responsive' },
            el('table', { class: 'table align-middle mb-0' },
              el('thead', {}, el('tr', {},
                ...[['Order #', ''], ['Date & Time', ''], ['Cashier', ''], ['Items', ''],
                    ['Status', ''], ['Payment', ''], ['Total', 'text-end']].map(([h, cls]) =>
                  el('th', { class: cls, text: h })))),
              tbody)));
        container.appendChild(card);

        // Fetch real orders for the selected date range (to is inclusive — server adds a day).
        let list;
        try {
          list = await Api.get(`/api/orders/recent?take=200&from=${fromIn.value}&to=${toIn.value}`);
        } catch (e) {
          tbody.appendChild(el('tr', {}, el('td', { colspan: 7 }, empty('bi-exclamation-triangle', e.message))));
          return;
        }

        if (!list || !list.length) {
          tbody.appendChild(el('tr', {}, el('td', { colspan: 7 }, empty('bi-receipt', 'No orders in this range.'))));
          return;
        }

        list.forEach((o) => {
          const status = (o.status || '').toLowerCase();
          tbody.appendChild(el('tr', {},
            el('td', { class: 'fw-semibold', text: '#' + o.id }),
            el('td', { text: dateTime(o.timestamp) }),
            el('td', { text: o.cashier }),
            el('td', { class: 'text-truncate', style: 'max-width:260px', title: o.itemSummary,
              text: o.itemSummary || (o.itemCount + ' item' + (o.itemCount === 1 ? '' : 's')) }),
            el('td', {}, el('span', { class: 'rpt-status rpt-status-' + status, text: o.status })),
            el('td', { text: o.paymentMethod }),
            el('td', { class: 'text-end fw-semibold', text: money(o.totalAmount) })));
        });
      }

      // Menu image resolver — kept in sync with menuImage() in pos.js
      function menuImageByName(name, category) {
        const IMG_BASE = 'img/';
        const n = (name || '').trim().toLowerCase();
        const c = (category || '').toLowerCase();

        // Cold Brew Coffee
        if (n.includes('americano')) return IMG_BASE + 'Cold Brew Coffee/Americano.webp';
        if (n.includes('caramel macchiato')) return IMG_BASE + 'Cold Brew Coffee/Caramel Macchiato.webp';
        if (n.includes("chao's") || n.includes("chao")) return IMG_BASE + 'Cold Brew Coffee/Chao_s Coldbrew.webp';
        if (n.includes('cold brew latte')) return IMG_BASE + 'Cold Brew Coffee/Cold Brew Latte.webp';
        if (n.includes('mocha') && (c === 'cold brew coffee' || c === 'cold coffee')) return IMG_BASE + 'Cold Brew Coffee/Mocha.webp';
        if (n.includes('spanish latte')) return IMG_BASE + 'Cold Brew Coffee/Spanish Latte.webp';
        if (n.includes('vanilla latte')) return IMG_BASE + 'Cold Brew Coffee/Vanilla Latte.webp';
        if (n.includes('latte') && (c === 'cold brew coffee' || c === 'cold coffee')) return IMG_BASE + 'Cold Brew Coffee/Latte.webp';
        if (c === 'cold brew coffee' || c === 'cold coffee') return IMG_BASE + 'Cold Brew Coffee/Americano.webp';

        // Non-Coffee
        if (n.includes('strawberry milk')) return IMG_BASE + 'Non-Coffee/Strawberry Milk.webp';
        if (n.includes('blueberry milk')) return IMG_BASE + 'Non-Coffee/Blueberry Milk.webp';
        if (n.includes('mango cream')) return IMG_BASE + 'Non-Coffee/Mango Cream.webp';
        if (n.includes('iced choco')) return IMG_BASE + 'Non-Coffee/Iced Choco.webp';
        if (n.includes('milky oreo')) return IMG_BASE + 'Non-Coffee/Milky Oreo.webp';
        if (n.includes('berry choco')) return IMG_BASE + 'Non-Coffee/Berry Choco Latte.webp';
        if (c === 'non-coffee') return IMG_BASE + 'Non-Coffee/Strawberry Milk.webp';

        // Matcha Series
        if (n.includes('matcha frappe') || n.includes('matcha frappuccino')) return IMG_BASE + 'Matcha Series/Matcha Frappuccino.webp';
        if (n.includes('dirty matcha')) return IMG_BASE + 'Matcha Series/Dirty Matcha.webp';
        if (n.includes('strawberry matcha')) return IMG_BASE + 'Matcha Series/Strawberry Matcha.webp';
        if (n.includes('matcha')) return IMG_BASE + 'Matcha Series/Matcha Latte.webp';
        if (c === 'matcha series') return IMG_BASE + 'Matcha Series/Matcha Latte.webp';

        // Frappe — per-flavor matching
        if (n.includes('java chip')) return IMG_BASE + 'Frappe/Java Chip.webp';
        if (n.includes('milo dinosaur')) return IMG_BASE + 'Frappe/Milo Dinosaur.webp';
        if (n.includes('frappuccino') || n.includes('frappucino')) return IMG_BASE + 'Frappe/Frappuccino.webp';
        if (n.includes('mocha') && c === 'frappe') return IMG_BASE + 'Frappe/Mocha.webp';
        if (n.includes('frappe') || c === 'frappe') {
          if (n.includes('blueberry'))                        return IMG_BASE + 'Frappe/Blueberry.webp';
          if (n.includes('chocolate') || n.includes('choco')) return IMG_BASE + 'Frappe/Chocolate.webp';
          if (n.includes('cookies') || n.includes('cream') || n.includes('oreo')) return IMG_BASE + 'Frappe/Cookies & Cream.webp';
          if (n.includes('strawberry'))                       return IMG_BASE + 'Frappe/Strawberry.webp';
          return IMG_BASE + 'Frappe/Strawberry.webp'; // generic frappe fallback
        }

        // Qik's Fried Noodles — Overload variants
        if (n.includes('overload') && n.includes('korean')) return IMG_BASE + 'QIK_S Fried Noodles/Overload/Overload Noodles w(2) Korean Sausage.webp';
        if (n.includes('overload') && n.includes('jap'))    return IMG_BASE + 'QIK_S Fried Noodles/Overload/Overload Noodles w(4) Jap Siomai.webp';
        if (n.includes('overload') && n.includes('pork'))   return IMG_BASE + 'QIK_S Fried Noodles/Overload/Overload Noodles w(4) Pork Siomai.webp';
        if (n.includes('overload') && n.includes('egg'))    return IMG_BASE + 'QIK_S Fried Noodles/Overload/Overload with 2 Eggs.webp';
        if (n.includes('overload')) return IMG_BASE + 'QIK_S Fried Noodles/Overload/Overload Noodles.webp';

        // Qik's Fried Noodles — Regular
        if (n.includes('korean sausage'))  return IMG_BASE + 'QIK_S Fried Noodles/Noodles with Korean Sausage.webp';
        if (n.includes('japanese siomai')) return IMG_BASE + 'QIK_S Fried Noodles/Noodles with Japanese Siomai.webp';
        if (n.includes('pork siomai'))     return IMG_BASE + 'QIK_S Fried Noodles/Noodles with Pork Siomai.webp';
        if (n.includes('with egg'))        return IMG_BASE + 'QIK_S Fried Noodles/Noodles with Egg.webp';
        if (n.includes('plain'))           return IMG_BASE + 'QIK_S Fried Noodles/Plain Noodles.webp';
        if (c.includes('noodle') || c.includes('qik')) return IMG_BASE + 'QIK_S Fried Noodles/Plain Noodles.webp';

        // Fruit Soda
        if (n.includes('soda') || c === 'fruit soda') return IMG_BASE + 'Fruit Soda/Fruit Soda.webp';

        // Food
        if (c === 'food') {
          if (n.includes('tonkatsu sauce'))   return IMG_BASE + 'Food/Tonkatsu Sauce.webp';
          if (n.includes('pork tonkatsu'))    return IMG_BASE + 'Food/Pork Tonkatsu.webp';
          if (n.includes('chicken tonkatsu')) return IMG_BASE + 'Food/Chicken Tonkatsu.webp';
          if (n.includes('chicken poppers'))  return IMG_BASE + 'Food/Chicken Poppers.webp';
          if (n.includes('chicken fingers'))  return IMG_BASE + 'Food/Chicken Fingers.webp';
          if (n.includes('crabstick'))        return IMG_BASE + 'Food/Crabstick Katsu.webp';
          if (n.includes('spamsilog'))        return IMG_BASE + 'Food/Spamsilog.webp';
          if (n.includes('hungarian') || n.includes('hungariansilog')) return IMG_BASE + 'Food/Hungarian Silog.webp';
          if (n.includes('tocilog'))          return IMG_BASE + 'Food/Tocilog.webp';
          if (n.includes('tapsilog'))         return IMG_BASE + 'Food/Tapsilog.webp';
          if (n.includes('sausilog'))         return IMG_BASE + 'Food/Sausilog.webp';
          if (n.includes('bacsilog'))         return IMG_BASE + 'Food/Bacsilog.webp';
          if (n === 'egg')                    return IMG_BASE + 'Food/Egg.webp';
          if (n === 'rice')                   return IMG_BASE + 'Food/Rice.webp';
          return IMG_BASE + 'Food/Chicken Tonkatsu.webp';
        }

        return IMG_BASE + 'Cold Brew Coffee/Americano.webp';
      }

      rebuildPeriodRow();
      await load();
    },
  };

  Views.performance = Views.reports;
})();
