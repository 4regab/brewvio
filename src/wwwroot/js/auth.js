// Auth flow controller
// Entry: login screen (role toggled inline) → authenticating → approved/rejected
// Sign-up available via footer link.
const Auth = (() => {
  const { el, button } = UI;
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

  function setBusy(btn, on, label) {
    if (!btn) return;
    if (on) { btn.dataset.label = btn.innerHTML; btn.disabled = true; btn.innerHTML = `<span class="spinner-border spinner-border-sm me-2"></span>${label || 'Please wait…'}`; }
    else    { btn.disabled = false; if (btn.dataset.label) btn.innerHTML = btn.dataset.label; }
  }

  function field(labelText, inputEl) {
    return el('div', { class: 'mb-3' },
      el('label', { class: 'auth-label', text: labelText }),
      inputEl);
  }

  // ---------- Login screen ----------
  function loginScreen(role = 'Cashier') {
    const user = el('input', { class: 'form-control', autocomplete: 'username', placeholder: 'Enter your username' });
    const pass = el('input', { type: 'password', class: 'form-control', autocomplete: 'current-password', placeholder: 'Enter your password' });

    // Show/hide password toggle
    const pwToggle = el('button', { type: 'button', class: 'auth-pw-toggle', 'aria-label': 'Show password' },
      el('i', { class: 'bi bi-eye' }));
    pwToggle.addEventListener('click', () => {
      const show = pass.type === 'password';
      pass.type = show ? 'text' : 'password';
      pwToggle.querySelector('i').className = show ? 'bi bi-eye-slash' : 'bi bi-eye';
    });

    const err  = el('div', { class: 'auth-error d-none' });
    const submit = button('Sign In', 'btn-primary w-100');
    submit.type = 'submit';

    const form = el('form', { class: 'auth-form', onSubmit: async (e) => {
      e.preventDefault();
      err.classList.add('d-none');
      if (!user.value.trim() || !pass.value) {
        err.textContent = 'Please enter your username and password.';
        err.classList.remove('d-none'); return;
      }
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
    }},
      el('div', { class: 'auth-input-wrap' },
        el('i', { class: 'bi bi-person auth-input-icon' }),
        user),
      el('div', { class: 'auth-input-wrap' },
        el('i', { class: 'bi bi-lock auth-input-icon' }),
        pass,
        pwToggle),
      err,
      submit);

    mount(el('div', { class: 'auth-card' },
      el('h2', { class: 'auth-title', text: 'Welcome back' }),
      el('p', { class: 'auth-sub', text: 'Sign in to your account' }),
      form));

    setTimeout(() => user.focus(), 80);
  }

  // ---------- Sign-up screen ----------
  function signupScreen(role = 'Cashier') {
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
    }},
      field('Full Name', full),
      field('Username',  user),
      el('div', { class: 'mb-3' },
        el('label', { class: 'auth-label', text: 'Role' }), roleSel),
      field('Password', pass),
      err, submit);

    mount(el('div', { class: 'auth-card' },
      el('button', { class: 'auth-back', type: 'button', onClick: () => loginScreen(role) },
        el('i', { class: 'bi bi-arrow-left' }), ' Back'),
      el('h2', { class: 'auth-title', text: 'Sign Up' }),
      form,
      el('div', { class: 'auth-foot' },
        el('span', { text: 'Already have an account? ' }),
        el('a', { href: '#', class: 'auth-link', onClick: (e) => { e.preventDefault(); loginScreen(role); } }, 'Log In'))));
    setTimeout(() => full.focus(), 80);
  }

  // ---------- Authenticating (pending approval) ----------
  function authenticatingScreen(username, role) {
    const statusLine = el('p', { class: 'auth-status-text', text: 'Waiting for a manager to approve your account…' });
    mount(el('div', { class: 'auth-card auth-center' },
      el('div', { class: 'auth-spinner mt-2' }, el('span', { class: 'spinner-border' })),
      el('h2', { class: 'auth-title mt-3', text: 'Awaiting Approval' }),
      el('p',  { class: 'auth-sub', text: `Signed in as ${username}` }),
      statusLine,
      el('div', { class: 'auth-foot mt-4' },
        el('a', { href: '#', class: 'auth-link', onClick: (e) => { e.preventDefault(); clearPoll(); loginScreen(role); } }, '← Back to login'))));

    let tries = 0;
    const poll = async () => {
      tries++;
      try {
        const s = await Api.accountStatus(username);
        if (s.status === 'Active')   { clearPoll(); approvedScreen(username, role); return; }
        if (s.status === 'Rejected') { clearPoll(); rejectedScreen(username, role); return; }
        statusLine.textContent = `Still pending… (checked ${tries}×)`;
      } catch (ex) {
        statusLine.textContent = ex.status === 404 ? 'Account not found.' : 'Could not reach server, retrying…';
      }
    };
    clearPoll(); poll();
    pollTimer = setInterval(poll, 3000);
  }

  // ---------- Approved ----------
  function approvedScreen(username, role) {
    mount(el('div', { class: 'auth-card auth-center' },
      el('div', { class: 'auth-success mt-2' }, el('i', { class: 'bi bi-check-lg' })),
      el('h2', { class: 'auth-title mt-3', text: 'Account Approved!' }),
      el('p',  { class: 'auth-sub', text: `Welcome, ${username}. Your account is now active.` }),
      button('Continue to Login', 'btn-primary w-100 mt-2', () => loginScreen(role))));
  }

  // ---------- Rejected ----------
  function rejectedScreen(username, role) {
    mount(el('div', { class: 'auth-card auth-center' },
      el('div', { class: 'auth-reject mt-2' }, el('i', { class: 'bi bi-x-lg' })),
      el('h2', { class: 'auth-title mt-3', text: 'Request Declined' }),
      el('p',  { class: 'auth-sub', text: 'Your request was declined. Contact your manager.' }),
      button('Back to Login', 'btn-outline-secondary w-100 mt-2', () => loginScreen(role))));
  }

  // Demo credentials hint
  const demoHint = () => el('div', { class: 'auth-demo' },
    el('div', { class: 'auth-demo-title', text: 'Demo accounts' }),
    el('div', { html: 'Manager — <code>manager</code> / <code>Manager@123</code>' }),
    el('div', { html: 'Cashier — <code>cashier</code> / <code>Cashier@123</code>' }));

  function start() { show(); loginScreen(); }

  return { start, show, clearPoll };
})();
window.Auth = Auth;
