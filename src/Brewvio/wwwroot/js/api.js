// Thin API client: attaches the JWT, parses JSON, and surfaces server error messages.
const Api = (() => {
  const TOKEN_KEY = 'brewvio_token';
  let token = localStorage.getItem(TOKEN_KEY) || null;

  class ApiError extends Error {
    constructor(message, status, data) { super(message); this.status = status; this.data = data; }
  }

  const setToken = (t) => { token = t; t ? localStorage.setItem(TOKEN_KEY, t) : localStorage.removeItem(TOKEN_KEY); };
  const getToken = () => token;
  const authHeader = () => (token ? { Authorization: 'Bearer ' + token } : {});

  async function request(method, url, body) {
    const headers = { ...authHeader() };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const res = await fetch(url, { method, headers, body: body !== undefined ? JSON.stringify(body) : undefined });

    if (res.status === 401) {
      setToken(null);
      window.dispatchEvent(new Event('brewvio:unauthorized'));
      throw new ApiError('Your session has expired. Please sign in again.', 401);
    }
    if (res.status === 204) return null;
    const text = await res.text();
    let data = null;
    if (text) { try { data = JSON.parse(text); } catch { data = text; } }
    if (!res.ok) throw new ApiError((data && data.message) || res.statusText || 'Request failed', res.status, data);
    return data;
  }

  // Authenticated download (CSV / PDF / JSON backup) -> triggers a browser save.
  async function download(url, fallbackName) {
    const res = await fetch(url, { headers: authHeader() });
    if (!res.ok) throw new ApiError('Download failed', res.status);
    const blob = await res.blob();
    const cd = res.headers.get('Content-Disposition') || '';
    const m = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd);
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = m ? decodeURIComponent(m[1]) : fallbackName;
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  }

  return {
    ApiError, setToken, getToken, download,
    get: (u) => request('GET', u),
    post: (u, b) => request('POST', u, b ?? {}),
    put: (u, b) => request('PUT', u, b ?? {}),
    login: (username, password) => request('POST', '/api/auth/login', { username, password }),
    register: (body) => request('POST', '/api/auth/register', body),
    accountStatus: (username) => request('GET', '/api/auth/status?username=' + encodeURIComponent(username)),
    me: () => request('GET', '/api/auth/me'),
  };
})();
