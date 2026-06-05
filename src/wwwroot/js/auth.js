// Auth flow controller — renders the multi-screen sign-in / sign-up / approval experience
// Screens: role → (login | signup) → authenticating (poll /auth/status) → approved
const Auth = (() => {
  const { el, button, toast } = UI;
  let pollTimer = null;

  const root = () => document.getElementById('auth-root');
  const show = () => {
    document.getElementById('app-shell').classList.add('d-none');
    document.getElementById('auth-screen').classList.remove('d-none');
  };
  const clearPoll = () => { if (pollTimer) { clearInterval(pollTimer); pollTimer = null; } };

  function mount(node) {
    clearPoll();
    const r = root();
    r.innerHTML = '';
    r.appendChild(node);
  }

  function setBusy(btn, on, busyLabel) {
    if (!btn) return;
    if (on) { btn.dataset.label = btn.innerHTML; btn.disabled = true; btn.innerHTML = `<span class="spinner-border spinner-border-sm me-2"></span>${busyLabel || 'Please wait…'}`; }
    else { btn.disabled = false; if (btn.dataset.label) btn.innerHTML = btn.dataset.label; }
  }

  // Wrapped input with icon
  function iconInput(icon, inputEl) {
    const wrap = el('div', { class: 'auth-input-wrap' });
    wrap.appendChild(el('i', { class: 'bi ' + icon + ' auth-input-icon' }));
    wrap.appendChild(inputEl);
    return wrap;
  }

  // ---------- Screen 1: Role selection ----------
  function roleScreen() {
    const pick = (role) => loginScreen(role);
    const roleCard = (role, icon, blurb) => el('button', { class: 'role-card', onClick: () => pick(role) },
      el('div', { class: 'role-icon' }, el('i', { class: 'bi ' + icon })),
      el('div', { class: 'role-name', text: role }),
      el('div', { class: 'role-blurb', text: blurb }),
      el('span', { class: 'role-go', html: 'Continue <i class="bi bi-arrow-right"></i>' }));

    mount(el('div', { class: 'auth-card' },
      el('div', { class: 'auth-head' },
        el('h2', { class: 'auth-title', text: 'Welcome!' }),
        el('p', { class: 'auth-sub', text: 'Who is logging in?' })),
      el('div', { class: 'role-grid' },
        roleCard('Manager', 'bi-person-badge', 'Full access: POS, inventory, reports & staff.'),
        roleCard('Cashier', 'bi-cup-hot', 'Take orders & print receipts.')),
      el('div', { class: 'auth-foot mt-3' },
        el('span', { text: "Don't have an account? " }),
        el('a', { href: '#', class: 'auth-link', onClick: (e) => { e.preventDefault(); signupScreen('Cashier'); } }, 'Sign Up'))));
  }

  // ---------- Screen 2a: Log in ----------
  function loginScreen(role) {
    const user = el('input', { class: 'form-control', autocomplete: 'off', placeholder: 'Name' });
    const pass = el('input', { type: 'password', class: 'form-control', autocomplete: 'new-password', placeholder: 'Password' });
    const err  = el('div', { class: 'auth-error d-none' });
    const submit = button('Log In', 'btn-primary w-100');
    submit.type = 'submit';

    const form = el('form', { class: 'auth-form', onSubmit: async (e) => {
      e.preventDefault();
      err.classList.add('d-none');
      if (!user.value.trim() || !pass.value) { err.textContent = 'Enter your username and password.'; err.classList.remove('d-none'); return; }
      setBusy(submit, true, 'Signing in…');
      try {
        const res = await Api.login(user.value.trim(), pass.value);
        Api.setToken(res.token);
        await App.onAuthenticated({ username: res.username, fullName: res.fullName, role: res.role });
      } catch (ex) {
        setBusy(submit, false);
        if (/awaiting/i.test(ex.message)) { authenticatingScreen(user.value.trim(), role); return; }
        err.textContent = ex.message; err.classList.remove('d-none');
      }
    } },
      iconInput('bi-person', user),
      iconInput('bi-lock', pass),
      err, submit);

    mount(el('div', { class: 'auth-card' },
      backRow(() => roleScreen()),
      el('div', { class: 'auth-head' },
        el('span', { class: 'auth-pill', text: role + ' sign-in' }),
        el('h2', { class: 'auth-title', text: 'Log In' })),
      form,
      el('div', { class: 'auth-foot' },
        el('span', { text: "Don't have an account? " }),
        el('a', { href: '#', class: 'auth-link', onClick: (e) => { e.preventDefault(); signupScreen(role); } }, 'Sign Up')),
      demoHint()));
    setTimeout(() => user.focus(), 120);
  }

  // ---------- Screen 2b: Sign up ----------
  function signupScreen(role) {
    const full = el('input', { class: 'form-control', placeholder: 'Full name' });
    const user = el('input', { class: 'form-control', autocomplete: 'username', placeholder: 'Username' });
    const pass = el('input', { type: 'password', class: 'form-control', autocomplete: 'new-password', placeholder: 'Password (min 6 characters)' });
    const roleSel = el('select', { class: 'form-select' },
      el('option', { value: 'Cashier', selected: role !== 'Manager' }, 'Cashier'),
      el('option', { value: 'Manager', selected: role === 'Manager' }, 'Manager'));
    const err    = el('div', { class: 'auth-error d-none' });
    const submit = button('Create Account', 'btn-primary w-100');
    submit.type  = 'submit';

    const form = el('form', { class: 'auth-form', onSubmit: async (e) => {
      e.preventDefault();
      err.classList.add('d-none');
      if (!user.value.trim()) { err.textContent = 'Username is required.'; err.classList.remove('d-none'); return; }
      if (pass.value.length < 6) { err.textContent = 'Password must be at least 6 characters.'; err.classList.remove('d-none'); return; }
      setBusy(submit, true, 'Submitting…');
      try {
        await Api.register({ username: user.value.trim(), fullName: full.value.trim(), password: pass.value, role: roleSel.value });
        authenticatingScreen(user.value.trim(), roleSel.value);
      } catch (ex) { setBusy(submit, false); err.textContent = ex.message; err.classList.remove('d-none'); }
    } },
      iconInput('bi-person', full),
      iconInput('bi-person-badge', user),
      el('div', { class: 'mb-2' },
        el('label', { class: 'form-label', style: 'color:rgba(255,255,255,.7);font-size:.8rem', text: 'Role' }),
        roleSel),
      iconInput('bi-lock', pass),
      err, submit);

    mount(el('div', { class: 'auth-card' },
      backRow(() => loginScreen(role)),
      el('div', { class: 'auth-head' },
        el('span', { class: 'auth-pill', text: 'New account' }),
        el('h2', { class: 'auth-title', text: 'Sign Up' })),
      form,
      el('div', { class: 'auth-foot' },
        el('span', { text: 'Already have an account? ' }),
        el('a', { href: '#', class: 'auth-link', onClick: (e) => { e.preventDefault(); loginScreen(role); } }, 'Log In'))));
    setTimeout(() => full.focus(), 120);
  }

  // ---------- Screen 3: Authenticating… ----------
  function authenticatingScreen(username, role) {
    const statusLine = el('p', { class: 'auth-status-text', text: 'Waiting for a manager to review your request…' });
    mount(el('div', { class: 'auth-card auth-center' },
      el('div', { class: 'auth-spinner mt-2' }, el('span', { class: 'spinner-border' })),
      el('h2', { class: 'auth-title mt-3', text: 'Authenticating...' }),
      el('p', { class: 'auth-sub', text: `Signing in as ${username}` }),
      statusLine,
      el('div', { class: 'auth-foot mt-4' },
        el('a', { href: '#', class: 'auth-link', onClick: (e) => { e.preventDefault(); clearPoll(); loginScreen(role); } }, 'Back to login'))));

    let tries = 0;
    const poll = async () => {
      tries++;
      try {
        const s = await Api.accountStatus(username);
        if (s.status === 'Active')    { clearPoll(); approvedScreen(username, role);  return; }
        if (s.status === 'Rejected')  { clearPoll(); rejectedScreen(username, role);  return; }
        statusLine.textContent = `Still pending approval… (checked ${tries}×)`;
      } catch (ex) {
        statusLine.textContent = ex.status === 404 ? 'Account not found.' : 'Could not reach the server, retrying…';
      }
    };
    clearPoll();
    poll();
    pollTimer = setInterval(poll, 3000);
  }

  // ---------- Screen 4a: Account approved ----------
  function approvedScreen(username, role) {
    mount(el('div', { class: 'auth-card auth-center' },
      el('div', { class: 'auth-success mt-2' }, el('i', { class: 'bi bi-check-lg' })),
      el('h2', { class: 'auth-title mt-3', text: 'Account Approved!' }),
      el('p', { class: 'auth-sub', text: `Welcome aboard, ${username}. Your account is now active.` }),
      button('Continue to login', 'btn-primary w-100 mt-2', () => loginScreen(role))));
  }

  // ---------- Screen 4b: Request declined ----------
  function rejectedScreen(username, role) {
    mount(el('div', { class: 'auth-card auth-center' },
      el('div', { class: 'auth-reject mt-2' }, el('i', { class: 'bi bi-x-lg' })),
      el('h2', { class: 'auth-title mt-3', text: 'Request Declined' }),
      el('p', { class: 'auth-sub', text: 'Your account request was declined. Please contact your manager.' }),
      button('Back to start', 'btn-outline-secondary w-100 mt-2', () => roleScreen())));
  }

  // ---------- Helpers ----------
  const backRow = (onBack) => el('button', { class: 'auth-back', type: 'button', onClick: onBack },
    el('i', { class: 'bi bi-arrow-left' }), 'Back');

  const demoHint = () => el('div', { class: 'auth-demo' },
    el('div', { class: 'auth-demo-title', text: 'Demo accounts' }),
    el('div', { html: 'Manager — <code>manager</code> / <code>Manager@123</code>' }),
    el('div', { html: 'Cashier — <code>cashier</code> / <code>Cashier@123</code>' }));

  function start() { show(); roleScreen(); }

  return { start, show, clearPoll };
})();
window.Auth = Auth;
