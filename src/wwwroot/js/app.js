// App shell: authentication handoff, role-aware navigation, and a hash-based router over window.Views.
const App = (() => {
  // Consolidated navigation matching reference design:
  // Point of Sales, Activity (Order Queue + Order History), Report, Inventory, Settings
  const NAV = [
    { id: 'pos', label: 'Point of Sales', icon: 'bi-cup-hot-fill', roles: ['Manager', 'Cashier'] },
    { id: 'activity', label: 'Activity', icon: 'bi-receipt-cutoff', roles: ['Manager', 'Cashier'] },
    { id: 'reports', label: 'Report', icon: 'bi-graph-up-arrow', roles: ['Manager'] },
    { id: 'inventory', label: 'Inventory', icon: 'bi-box-seam', roles: ['Manager', 'Cashier'] },
    { id: 'menu', label: 'Menu Edit', icon: 'bi-card-list', roles: ['Manager'] },
    { id: 'users', label: 'Users', icon: 'bi-people', roles: ['Manager'] },
    { id: 'settings', label: 'Settings', icon: 'bi-gear', roles: ['Manager'] },
  ];
  const state = { user: null };
  const $ = UI.$, $$ = UI.$$;

  const allowed = (id) => { const n = NAV.find((x) => x.id === id); return !!n && state.user && n.roles.includes(state.user.role); };
  const currentRoute = () => (location.hash || '#pos').slice(1);

  function updateTopbarDate() {
    const el = document.getElementById('topbar-date-text');
    if (!el) return;
    const now = new Date();
    const opts = { weekday: 'short', day: '2-digit', month: 'short', year: 'numeric' };
    el.textContent = now.toLocaleDateString('en-US', opts);
  }

  async function init() {
    $('#sidebar-logout').addEventListener('click', logout);
    $('#nav-toggle').addEventListener('click', () => $('#app-shell').classList.add('nav-open'));
    $('#sidebar-scrim').addEventListener('click', closeSidebar);
    window.addEventListener('hashchange', route);
    window.addEventListener('brewvio:unauthorized', () => { if (state.user) { state.user = null; Auth.start(); UI.toast('Your session expired. Please sign in.', 'warning'); } });

    updateTopbarDate();
    setInterval(updateTopbarDate, 60000);

    if (Api.getToken()) {
      try { await onAuthenticated(await Api.me()); return; } catch { Api.setToken(null); }
    }
    Auth.start();
  }

  function closeSidebar() { $('#app-shell').classList.remove('nav-open'); }

  async function onAuthenticated(user) {
    Auth.clearPoll();
    state.user = user;
    try { App.store = await Api.get('/api/settings/store'); } catch { App.store = { storeName: 'Chao & Brew', currency: 'PHP', taxRatePercent: 0 }; }
    UI.setCurrency(App.store.currency);
    showApp();
  }

  function showApp() {
    $('#auth-screen').classList.add('d-none');
    $('#app-shell').classList.remove('d-none');
    const name = state.user.fullName || state.user.username;
    $('#sidebar-user-name').textContent = name;
    $('#sidebar-user-role').textContent = state.user.role;
    $('#sidebar-avatar').textContent = name.slice(0, 1).toUpperCase();
    buildNav();
    const target = allowed(currentRoute()) ? (location.hash || '#pos') : '#pos';
    if (location.hash === target) route(); else location.hash = target;
  }

  function buildNav() {
    const nav = $('#nav-menu'); nav.innerHTML = '';
    NAV.filter((n) => n.roles.includes(state.user.role)).forEach((n) =>
      nav.appendChild(UI.el('a', { class: 'nav-link', href: '#' + n.id }, UI.el('i', { class: 'bi ' + n.icon }), UI.el('span', { text: n.label }))));
  }

  async function route() {
    if (!state.user) return;
    const id = currentRoute();
    if (!allowed(id)) { location.hash = '#pos'; return; }
    $$('#nav-menu .nav-link').forEach((a) => a.classList.toggle('active', a.getAttribute('href') === '#' + id));
    closeSidebar();
    const root = $('#app'); root.innerHTML = '';
    const view = window.Views[id];
    if (!view) { root.appendChild(UI.empty('bi-question-circle', 'View not found.')); return; }
    try { await view.render(root); } catch (e) { root.innerHTML = ''; root.appendChild(UI.empty('bi-exclamation-triangle', e.message || 'Failed to load view.')); }
  }

  function logout() { Api.setToken(null); state.user = null; Auth.start(); }

  return { init, state, store: null, onAuthenticated };
})();
window.App = App;
document.addEventListener('DOMContentLoaded', App.init);
