// App shell: authentication handoff, role-aware navigation, and a hash-based router over window.Views.
const App = (() => {
  // Sidebar matches the Figma information architecture:
  // Order · Order Summary · Inventory · Sales Report · Menu Performance · Menu Edit (+ admin).
  const NAV = [
    { id: 'pos', label: 'Order', icon: 'bi-cup-hot-fill', roles: ['Manager', 'Cashier'] },
    { id: 'transactions', label: 'Order Summary', icon: 'bi-receipt', roles: ['Manager', 'Cashier'] },
    { id: 'shift', label: 'My Shift', icon: 'bi-clock-history', roles: ['Manager', 'Cashier'] },
    { id: 'inventory', label: 'Inventory', icon: 'bi-box-seam', roles: ['Manager'] },
    { id: 'reports', label: 'Sales Report', icon: 'bi-graph-up-arrow', roles: ['Manager'] },
    { id: 'performance', label: 'Menu Performance', icon: 'bi-trophy', roles: ['Manager'] },
    { id: 'menu', label: 'Menu Edit', icon: 'bi-card-list', roles: ['Manager'] },
    { id: 'users', label: 'Users', icon: 'bi-people', roles: ['Manager'] },
    { id: 'audit', label: 'Audit Log', icon: 'bi-shield-check', roles: ['Manager'] },
    { id: 'settings', label: 'Settings', icon: 'bi-gear', roles: ['Manager'] },
  ];
  const state = { user: null };
  const $ = UI.$, $$ = UI.$$;

  const allowed = (id) => { const n = NAV.find((x) => x.id === id); return !!n && state.user && n.roles.includes(state.user.role); };
  const currentRoute = () => (location.hash || '#pos').slice(1);

  async function init() {
    $('#logout-btn').addEventListener('click', logout);
    $('#nav-toggle').addEventListener('click', () => $('#app-shell').classList.toggle('nav-open'));
    $('#sidebar-scrim').addEventListener('click', () => $('#app-shell').classList.remove('nav-open'));
    window.addEventListener('hashchange', route);
    window.addEventListener('brewvio:unauthorized', () => { if (state.user) { state.user = null; Auth.start(); UI.toast('Your session expired. Please sign in.', 'warning'); } });

    if (Api.getToken()) {
      try { await onAuthenticated(await Api.me()); return; } catch { Api.setToken(null); }
    }
    Auth.start();
  }

  async function onAuthenticated(user) {
    Auth.clearPoll();
    state.user = user;
    try { App.store = await Api.get('/api/settings/store'); } catch { App.store = { storeName: 'Brewvio Coffee', currency: 'PHP', taxRatePercent: 0 }; }
    UI.setCurrency(App.store.currency);
    showApp();
  }

  function showApp() {
    $('#auth-screen').classList.add('d-none');
    $('#app-shell').classList.remove('d-none');
    const name = state.user.fullName || state.user.username;
    $('#user-name').textContent = name;
    $('#user-role').textContent = state.user.role;
    $('#user-avatar').textContent = name.slice(0, 1).toUpperCase();
    $('#sidebar-store').textContent = (App.store && App.store.storeName) || 'Brewvio';
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
    $('#view-title').textContent = NAV.find((n) => n.id === id).label;
    $('#app-shell').classList.remove('nav-open');
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
