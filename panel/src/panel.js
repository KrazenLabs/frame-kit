(function (version) {
  if (window.__fkp && typeof window.__fkp.dispose === 'function') {
    try { window.__fkp.dispose(); } catch (error) { }
  }

  const POPUP_PREFIX = 'valve.steam.gamepadui.barpopup.';
  const STEAM_PANEL_PREFIX = 'quickaccess_content_';
  const LIST_VIEW = '';
  const STALE_MS = 20000;
  const PENDING_MS = 10000;
  const LONG_LABEL = 22;
  const TRANSIENT_CLASSES = ['Panel', 'Focusable', 'gpfocus', 'gpfocuswithin'];
  const ICON_PREFIX = 'data:image/svg+xml;base64,';
  const APPS_ICON = '<svg viewBox="0 0 36 36" width="24" height="24" fill="none" xmlns="http://www.w3.org/2000/svg">' +
    '<rect x="6" y="6" width="10" height="10" rx="2.5" fill="currentColor"/><rect x="20" y="6" width="10" height="10" rx="2.5" fill="currentColor"/>' +
    '<rect x="6" y="20" width="10" height="10" rx="2.5" fill="currentColor"/><rect x="20" y="20" width="10" height="10" rx="2.5" stroke="currentColor" stroke-width="2.4"/></svg>';
  const BACK_ICON = '<svg viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M10 3L5 8L10 13" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>';
  const CHEVRON = '<svg viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M6 3L11 8L6 13" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>';

  const POPUP_CSS = [
    '@keyframes fkp-enter { from { opacity: 0; transform: translateY(12%); } to { opacity: 1; transform: translateY(0); } }',
    '@keyframes fkp-pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.35; } }',
    '[data-fkp-hidden] { display: none !important; }',
    '#fkp-tab { position: relative; }',
    '#fkp-tab svg { display: block; margin: auto; }',
    '#fkp-badge { position: absolute; top: 8px; right: calc(50% - 16px); width: 8px; height: 8px; border-radius: 50%; background: #1A9FFF; box-shadow: 0 0 0 2px #0e141b; }',
    '#fkp-badge[hidden] { display: none; }',
    '#fkp-wrapper { flex-direction: column; animation: fkp-enter 320ms cubic-bezier(0, 0, 0.1, 1) 80ms both; }',
    '#fkp-header { display: flex; align-items: center; gap: 4px; }',
    '#fkp-back { flex: 0 0 auto; width: 32px; height: 32px; margin: 0 0 0 -6px; padding: 0; border: none; border-radius: 4px; background: transparent; color: #DCDEDF; display: flex; align-items: center; justify-content: center; cursor: pointer; }',
    '#fkp-back:hover { background: rgba(255, 255, 255, 0.1); color: #FFFFFF; }',
    '#fkp-back[hidden] { display: none; }',
    '#fkp-back svg { width: 18px; height: 18px; }',
    '#fkp-title { flex: 1 1 auto; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }',
    '#fkp-panel { flex: 1 1 1px; min-height: 0; display: flex; flex-direction: column; }',
    '.fkp-scroll { flex: 1 1 auto; min-height: 0; overflow-y: auto; padding: 12px 0 60px; }',
    '.fkp-scroll::-webkit-scrollbar { display: none; }',
    '.fkp-icon { flex: 0 0 auto; width: 26px; height: 26px; background-color: currentColor; -webkit-mask-position: center; -webkit-mask-repeat: no-repeat; -webkit-mask-size: contain; color: #DCDEDF; }',
    '.fkp-app { display: flex; align-items: center; gap: 12px; min-height: 52px; padding: 6px 16px; border-radius: 2px; cursor: pointer; transition: background-color 0.15s; }',
    '.fkp-app:hover { background: rgba(255, 255, 255, 0.07); }',
    '.fkp-text { flex: 1 1 auto; min-width: 0; display: flex; flex-direction: column; gap: 4px; }',
    '.fkp-name { color: #FFFFFF; font-size: 16px; line-height: 18px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }',
    '.fkp-caption { color: #8B929A; font-size: 12px; line-height: 15px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }',
    '.fkp-chevron { flex: 0 0 auto; width: 14px; height: 14px; color: #67707B; }',
    '.fkp-dot { flex: 0 0 auto; width: 8px; height: 8px; border-radius: 50%; background: #1A9FFF; }',
    '.fkp-hero { display: flex; align-items: center; gap: 12px; padding: 0 16px 10px; }',
    '.fkp-hero .fkp-icon { width: 34px; height: 34px; }',
    '.fkp-hero .fkp-name { font-size: 18px; line-height: 21px; font-weight: 600; }',
    '.fkp-lines { padding: 0 16px 8px; display: flex; flex-direction: column; gap: 4px; }',
    '.fkp-line { color: #DCDEDF; font-size: 13px; line-height: 17px; overflow-wrap: anywhere; }',
    '.fkp-tone-ok { color: #59BF40 !important; }',
    '.fkp-tone-warn { color: #FFC82C !important; }',
    '.fkp-tone-error { color: #E5483B !important; }',
    '.fkp-muted { color: #67707B; }',
    '.fkp-buttons { display: flex; flex-wrap: wrap; gap: 8px; padding: 4px 16px 8px; }',
    '.fkp-buttons button { box-sizing: border-box; flex: 1 1 calc(50% - 4px); min-width: 0; min-height: 36px; height: auto; margin: 0; padding: 5px 8px; font-size: 14px; line-height: 17px; white-space: normal; overflow: hidden; }',
    '.fkp-label { display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2; overflow: hidden; overflow-wrap: anywhere; }',
    '.fkp-buttons button:enabled { color: #FFFFFF; }',
    '.fkp-buttons button.fkp-wide { flex-basis: 100%; }',
    '.fkp-buttons button.fkp-danger:enabled { color: #FF7B6E; }',
    '.fkp-busy { animation: fkp-pulse 1s ease-in-out infinite; }',
    '.fkp-note { padding: 0 16px 8px; color: #8B929A; font-size: 12px; line-height: 16px; overflow-wrap: anywhere; }',
    '.fkp-section { padding: 12px 16px 4px; color: #8B929A; font-size: 13px; font-weight: 600; line-height: 16px; border-top: 1px solid rgba(255, 255, 255, 0.06); margin-top: 6px; }',
    '.fkp-option { display: flex; align-items: center; gap: 12px; min-height: 40px; padding: 2px 16px; cursor: pointer; transition: background-color 0.15s; }',
    '.fkp-option:hover { background: rgba(255, 255, 255, 0.07); }',
    '.fkp-option.fkp-disabled { cursor: default; opacity: 0.45; }',
    '.fkp-option.fkp-disabled:hover { background: transparent; }',
    '.fkp-option-title { flex: 1 1 auto; min-width: 0; color: #FFFFFF; font-size: 14px; line-height: 18px; }',
    '.fkp-confirm { margin: 6px 12px 0; padding: 10px 0 8px; border: 1px solid rgba(255, 255, 255, 0.08); border-radius: 6px; background: #23262E; }',
    '.fkp-confirm-title { padding: 0 14px 6px; color: #FFFFFF; font-size: 14px; line-height: 18px; }',
    '.fkp-empty { padding: 10px 16px; display: flex; flex-direction: column; gap: 6px; }',
    '.fkp-empty-title { color: #FFFFFF; font-size: 16px; }',
    '.fkp-empty-text { color: #8B929A; font-size: 13px; line-height: 18px; }',
    '.fkp-toggle { position: relative; flex: 0 0 38px; width: 38px; height: 22px; border-radius: 16px; transition: box-shadow 50ms ease-out; }',
    '.fkp-option:not(.fkp-disabled):hover .fkp-toggle { box-shadow: rgba(255, 255, 255, 0.3) 0 0 0 4px; transition: box-shadow 100ms ease-in; }',
    '.fkp-track { position: absolute; inset: 0; background: rgba(255, 255, 255, 0.15); border-radius: 9001px; overflow: hidden; }',
    '.fkp-track::before { content: ""; position: absolute; inset: 0; background: #1A9FFF; transform: translateX(-27px); transition: transform 0.2s ease-out; }',
    '.fkp-toggle.fkp-checked .fkp-track::before { transform: translateX(-11px); }',
    '.fkp-knob { position: absolute; left: 0; top: 0; width: 22px; height: 22px; border-radius: 9001px; background: #FFFFFF; box-shadow: rgba(0, 0, 0, 0.35) 0 0 5px 0; transition: transform 0.2s cubic-bezier(0.1, 0.12, 0.53, 1.72); }',
    '.fkp-toggle.fkp-checked .fkp-knob { transform: translateX(16px); }',
    '.fkp-toggle.fkp-unknown .fkp-knob { opacity: 0.5; }',
    '.fkp-toggle.fkp-working .fkp-knob { animation: fkp-pulse 1s ease-in-out infinite; }',
    '[data-fkp-deselected] svg { transform: none !important; }'
  ].join('\n');

  const st = {
    version: version,
    session: Math.random().toString(36).slice(2, 10),
    seq: 0,
    data: null,
    icons: {},
    lastData: 0,
    queue: [],
    pending: new Map(),
    popupWindow: null,
    popupDoc: null,
    popupObserver: null,
    popupOpen: false,
    tabActive: false,
    view: LIST_VIEW,
    confirm: '',
    confirmPcs: false,
    confirmAction: '',
    confirmSteamvr: '',
    purge: false,
    pointerDown: false,
    classes: null,
    drawn: '',
    scrollTop: 0,
    syncing: false,
    wasStale: false,
    timer: 0
  };

  function notify(reason) {
    try {
      if (typeof window.__fkpNotify === 'function') window.__fkpNotify(String(reason));
    } catch (error) { }
  }

  function isStale() {
    return !st.data || Date.now() - st.lastData > STALE_MS;
  }

  function fresh(key) {
    const entry = st.pending.get(key);
    if (!entry) return null;
    if (Date.now() - entry.at > PENDING_MS) {
      st.pending.delete(key);
      return null;
    }
    return entry;
  }

  function apps() {
    return st.data && Array.isArray(st.data.apps) ? st.data.apps : [];
  }

  function currentView() {
    const list = apps();
    if (list.length === 1) return list[0].id;
    if (st.view !== LIST_VIEW && list.some(function (app) { return app.id === st.view; })) return st.view;
    return LIST_VIEW;
  }

  function viewApp(app) {
    const shown = Object.assign({}, app);
    shown.toggles = (app.toggles || []).map(function (toggle) {
      const pending = fresh(app.id + '/toggle/' + toggle.id);
      return pending ? Object.assign({}, toggle, { on: pending.on, known: true, busy: true }) : toggle;
    });
    const update = Object.assign({}, app.update || {});
    const auto = fresh(app.id + '/auto');
    if (auto) update.auto = auto.on;
    if (fresh(app.id + '/check')) update.checking = true;
    if (fresh(app.id + '/update')) update.updating = true;
    shown.update = update;
    shown.actions = (app.actions || []).map(function (action) {
      return fresh(app.id + '/action/' + action.id) ? Object.assign({}, action, { busy: true }) : action;
    });
    if (fresh(app.id + '/uninstall')) shown.removing = true;
    if (fresh(app.id + '/restart-steamvr')) shown.steamvrRestarting = true;
    return shown;
  }

  function findPopupWindow() {
    try {
      const manager = window.opener && window.opener.g_PopupManager;
      const popups = manager && manager.m_mapPopups;
      if (!popups || typeof popups.forEach !== 'function') return null;
      let found = null;
      popups.forEach(function (popup, key) {
        if (found || !popup) return;
        const name = String(popup.m_strName || key || '');
        const win = popup.window || popup.m_popup;
        if (name.indexOf(POPUP_PREFIX) === 0 && win && !win.closed && win.document) found = win;
      });
      return found;
    } catch (error) {
      return null;
    }
  }

  function steamTabs(doc) {
    const all = Array.prototype.slice.call(doc.querySelectorAll('[role="tab"]'));
    const own = all.filter(function (t) { return (t.getAttribute('aria-controls') || '').indexOf(STEAM_PANEL_PREFIX) === 0; });
    return own.length ? own : all.filter(function (t) { return t.id !== 'fkp-tab'; });
  }

  function learnClasses(doc) {
    const tabs = steamTabs(doc);
    const selected = tabs.filter(function (t) { return t.getAttribute('aria-selected') === 'true'; })[0] || tabs[0];
    if (!tabs.length || !selected) return null;
    const keep = function (c) { return TRANSIENT_CLASSES.indexOf(c) < 0; };
    const common = Array.prototype.filter.call(tabs[0].classList, function (c) {
      return keep(c) && tabs.every(function (t) { return t.classList.contains(c); });
    });
    const reference = tabs.filter(function (t) { return t.getAttribute('aria-selected') === 'true'; })[0];
    const tabSelected = reference ? Array.prototype.filter.call(reference.classList, function (c) { return keep(c) && common.indexOf(c) < 0; }) : [];
    const panel = doc.getElementById((reference || selected).getAttribute('aria-controls') || '');
    const wrapper = panel && panel.parentElement;
    const header = wrapper && wrapper.firstElementChild !== panel ? wrapper.firstElementChild : null;
    if (!wrapper || !header || !wrapper.parentElement) return null;
    const unselected = tabs.filter(function (t) { return t !== reference; })[0];
    const view = doc.defaultView || window;
    return {
      tab: common,
      tabSelected: tabSelected,
      wrapper: Array.prototype.filter.call(wrapper.classList, keep),
      header: Array.prototype.filter.call(header.classList, keep),
      unselectedColor: unselected ? view.getComputedStyle(unselected).color : 'rgb(139, 146, 154)'
    };
  }

  function ensurePopupStyle(doc) {
    let style = doc.getElementById('fkp-popup-style');
    const css = POPUP_CSS + '\n[data-fkp-deselected] { background: transparent !important; color: ' + st.classes.unselectedColor + ' !important; }';
    if (!style) {
      style = doc.createElement('style');
      style.id = 'fkp-popup-style';
      (doc.head || doc.documentElement).appendChild(style);
    }
    if (style.textContent !== css) style.textContent = css;
  }

  function ensureTab(doc) {
    const list = doc.querySelector('[role="tablist"]');
    if (!list) return null;
    let tab = doc.getElementById('fkp-tab');
    if (!tab) {
      tab = doc.createElement('div');
      tab.id = 'fkp-tab';
      tab.setAttribute('role', 'tab');
      tab.setAttribute('aria-label', 'Community Apps');
      tab.setAttribute('aria-controls', 'fkp-panel');
      tab.innerHTML = APPS_ICON;
      const badge = doc.createElement('span');
      badge.id = 'fkp-badge';
      badge.hidden = true;
      tab.appendChild(badge);
      tab.addEventListener('click', function (event) {
        event.stopPropagation();
        if (!st.tabActive) {
          st.tabActive = true;
          syncPopup();
          notify('tab');
        }
      });
    }
    if (tab.parentElement !== list || list.lastElementChild !== tab) list.appendChild(tab);
    const anyUpdate = apps().some(function (app) { return (app.update && app.update.available) || app.steamvrRestart; });
    const badge = tab.querySelector('#fkp-badge');
    if (badge && badge.hidden === anyUpdate) badge.hidden = !anyUpdate;
    return tab;
  }

  function ensureWrapper(doc) {
    const steamPanel = doc.querySelector('[role="tabpanel"]:not(#fkp-panel)');
    const container = steamPanel && steamPanel.parentElement && steamPanel.parentElement.parentElement;
    if (!container) return null;
    let wrapper = doc.getElementById('fkp-wrapper');
    if (!wrapper) {
      wrapper = doc.createElement('div');
      wrapper.id = 'fkp-wrapper';
      const header = doc.createElement('div');
      header.id = 'fkp-header';
      const back = doc.createElement('button');
      back.id = 'fkp-back';
      back.type = 'button';
      back.setAttribute('aria-label', 'Back');
      back.innerHTML = BACK_ICON;
      back.hidden = true;
      back.addEventListener('click', function (event) {
        event.stopPropagation();
        st.view = LIST_VIEW;
        st.confirm = '';
        st.confirmPcs = false;
        st.confirmAction = '';
        st.confirmSteamvr = '';
        st.drawn = '';
        syncPopup();
        notify('view');
      });
      const title = doc.createElement('span');
      title.id = 'fkp-title';
      title.textContent = 'Community Apps';
      header.appendChild(back);
      header.appendChild(title);
      const panel = doc.createElement('div');
      panel.id = 'fkp-panel';
      panel.setAttribute('role', 'tabpanel');
      panel.addEventListener('click', onPanelClick);
      panel.addEventListener('pointerdown', function () { st.pointerDown = true; });
      ['pointerup', 'pointercancel', 'pointerleave'].forEach(function (name) {
        panel.addEventListener(name, function () {
          if (!st.pointerDown) return;
          st.pointerDown = false;
          setTimeout(syncPopup, 0);
        });
      });
      wrapper.appendChild(header);
      wrapper.appendChild(panel);
      st.drawn = '';
    }
    const wanted = st.classes.wrapper.join(' ');
    if (wrapper.className !== wanted) wrapper.className = wanted;
    const headerClass = st.classes.header.join(' ');
    if (wrapper.firstElementChild.className !== headerClass) wrapper.firstElementChild.className = headerClass;
    if (wrapper.parentElement !== container) container.appendChild(wrapper);
    return wrapper;
  }

  function setFlag(element, name, on) {
    if (on && !element.hasAttribute(name)) element.setAttribute(name, '');
    if (!on && element.hasAttribute(name)) element.removeAttribute(name);
  }

  function applyTabState(doc, tab, wrapper) {
    const active = st.tabActive;
    Array.prototype.forEach.call(wrapper.parentElement.children, function (child) {
      if (child !== wrapper) setFlag(child, 'data-fkp-hidden', active);
    });
    const display = active ? '' : 'none';
    if (wrapper.style.display !== display) wrapper.style.display = display;
    const selected = active ? 'true' : 'false';
    if (tab.getAttribute('aria-selected') !== selected) tab.setAttribute('aria-selected', selected);
    Array.prototype.forEach.call(doc.querySelectorAll('[role="tab"]'), function (other) {
      if (other === tab) return;
      const highlighted = other.getAttribute('aria-selected') === 'true' || other.classList.contains('gpfocus');
      setFlag(other, 'data-fkp-deselected', active && highlighted);
    });
  }

  function element(doc, tag, className, text) {
    const node = doc.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  function iconUrl(app) {
    const key = typeof app.icon === 'string' ? app.icon : '';
    const url = key && Object.prototype.hasOwnProperty.call(st.icons, key) ? st.icons[key] : '';
    return typeof url === 'string' && url.indexOf(ICON_PREFIX) === 0 && /^[A-Za-z0-9+/=]+$/.test(url.slice(ICON_PREFIX.length)) ? url : '';
  }

  function icon(doc, app, extra) {
    const node = element(doc, 'div', 'fkp-icon' + (extra ? ' ' + extra : ''));
    const url = iconUrl(app);
    if (url) {
      node.style.webkitMaskImage = 'url("' + url + '")';
    } else {
      node.innerHTML = APPS_ICON;
      node.style.backgroundColor = 'transparent';
    }
    return node;
  }

  function wideFor(label) {
    return label.length > LONG_LABEL ? ' fkp-wide' : '';
  }

  function button(doc, label, op, app, id, options) {
    const node = element(doc, 'button', 'DialogButton' + (options && options.primary ? ' Primary' : '') + (options && options.className ? ' ' + options.className : ''));
    node.appendChild(element(doc, 'span', 'fkp-label', label));
    node.type = 'button';
    node.setAttribute('data-fkp-op', op);
    node.setAttribute('data-fkp-module', app.id);
    if (id) node.setAttribute('data-fkp-id', id);
    if (options && options.disabled) node.disabled = true;
    return node;
  }

  function toggleRow(doc, label, on, known, busy, op, app, id, disabled) {
    const row = element(doc, 'div', 'fkp-option' + (disabled ? ' fkp-disabled' : ''));
    if (!disabled) {
      row.setAttribute('data-fkp-op', op);
      row.setAttribute('data-fkp-module', app.id);
      if (id) row.setAttribute('data-fkp-id', id);
      row.setAttribute('data-fkp-on', on ? '1' : '0');
    }
    row.appendChild(element(doc, 'div', 'fkp-option-title', label));
    const toggle = element(doc, 'div', 'fkp-toggle' + (on ? ' fkp-checked' : '') + (known ? '' : ' fkp-unknown') + (busy ? ' fkp-working' : ''));
    toggle.setAttribute('role', 'switch');
    toggle.setAttribute('aria-checked', on ? 'true' : 'false');
    toggle.setAttribute('aria-label', label);
    toggle.appendChild(element(doc, 'div', 'fkp-track'));
    toggle.appendChild(element(doc, 'div', 'fkp-knob'));
    row.appendChild(toggle);
    return row;
  }

  function summary(app) {
    if (app.removing) return { text: 'Removing…', tone: '' };
    if (app.problem) return { text: 'Error: ' + app.problem, tone: 'error' };
    if (app.update && app.update.updating) return { text: 'Updating…', tone: '' };
    if (app.steamvrRestarting) return { text: 'Restarting SteamVR…', tone: '' };
    if (app.steamvrRestart) return { text: 'Restart SteamVR to finish the update', tone: 'warn' };
    if (app.update && app.update.available) return { text: 'Version ' + app.update.latest + ' is available', tone: '' };
    const line = (app.lines || [])[0];
    if (line) return line;
    return { text: 'Version ' + (app.version || '?'), tone: '' };
  }

  function pairing() {
    const data = st.data && st.data.pairing;
    return {
      count: data && typeof data.count === 'number' ? data.count : 0,
      note: data && typeof data.note === 'string' ? data.note : '',
      tone: data && typeof data.noteTone === 'string' ? data.noteTone : '',
      busy: !!fresh('#pcs')
    };
  }

  function renderPairing(doc, scroll) {
    const paired = pairing();
    if (paired.count <= 0 && !paired.note && !paired.busy) return;
    const global = { id: '' };
    scroll.appendChild(element(doc, 'div', 'fkp-section', 'Paired PCs'));
    const lines = element(doc, 'div', 'fkp-lines');
    if (paired.busy) {
      lines.appendChild(element(doc, 'div', 'fkp-line fkp-busy', 'Forgetting paired PCs…'));
    } else if (paired.count > 0) {
      lines.appendChild(element(doc, 'div', 'fkp-line', paired.count === 1
        ? '1 paired PC.'
        : paired.count + ' paired PCs.'));
    }
    if (paired.note && !paired.busy) {
      lines.appendChild(element(doc, 'div', 'fkp-line' + (paired.tone ? ' fkp-tone-' + paired.tone : ''), paired.note));
    }
    scroll.appendChild(lines);
    if (paired.busy || paired.count <= 0) return;
    if (!st.confirmPcs) {
      const buttons = element(doc, 'div', 'fkp-buttons');
      buttons.appendChild(button(doc, 'Forget paired PCs…', 'pcs-ask', global, '', { className: 'fkp-wide fkp-danger' }));
      scroll.appendChild(buttons);
      return;
    }
    const card = element(doc, 'div', 'fkp-confirm');
    card.appendChild(element(doc, 'div', 'fkp-confirm-title', 'Forget all paired PCs?'));
    card.appendChild(element(doc, 'div', 'fkp-note', 'They will not be able to install apps until you pair them again.'));
    const buttons = element(doc, 'div', 'fkp-buttons');
    buttons.appendChild(button(doc, 'Forget', 'forget-pcs', global, '', { className: 'fkp-danger' }));
    buttons.appendChild(button(doc, 'Cancel', 'pcs-cancel', global, ''));
    card.appendChild(buttons);
    scroll.appendChild(card);
  }

  function renderList(doc, scroll, list) {
    if (!list.length) {
      const empty = element(doc, 'div', 'fkp-empty');
      empty.appendChild(element(doc, 'div', 'fkp-empty-title', 'No apps installed'));
      empty.appendChild(element(doc, 'div', 'fkp-empty-text', 'No community app has been found.'));
      scroll.appendChild(empty);
      return;
    }
    list.forEach(function (app) {
      const row = element(doc, 'div', 'fkp-app');
      row.setAttribute('data-fkp-op', 'open');
      row.setAttribute('data-fkp-module', app.id);
      row.appendChild(icon(doc, app));
      const text = element(doc, 'div', 'fkp-text');
      text.appendChild(element(doc, 'div', 'fkp-name', app.name));
      const line = summary(app);
      text.appendChild(element(doc, 'div', 'fkp-caption' + (line.tone ? ' fkp-tone-' + line.tone : ''), line.text));
      row.appendChild(text);
      if (app.update && app.update.available && !app.update.updating) row.appendChild(element(doc, 'div', 'fkp-dot'));
      const chevron = element(doc, 'div', 'fkp-chevron');
      chevron.innerHTML = CHEVRON;
      row.appendChild(chevron);
      scroll.appendChild(row);
    });
    renderPairing(doc, scroll);
  }

  function renderUpdates(doc, scroll, app) {
    const update = app.update || {};
    if (!update.enabled) return;
    scroll.appendChild(element(doc, 'div', 'fkp-section', 'Updates'));
    const lines = element(doc, 'div', 'fkp-lines');
    let text = 'Up to date.';
    let tone = '';
    if (update.updating) {
      text = 'Updating' + (update.latest ? ' to ' + update.latest : '') + '… app will restart automatically.';
    } else if (update.available) {
      text = 'Version ' + update.latest + ' is available (you have ' + app.version + ').';
    } else if (update.state === 'error' || update.state === 'failed') {
      text = update.message || 'The update check failed.';
      tone = 'error';
    } else if (!update.checkedText) {
      text = 'Not checked yet.';
    }
    if (!update.updating && update.checkedText && !(update.state === 'error' || update.state === 'failed')) {
      text += ' Checked ' + update.checkedText + '.';
    }
    lines.appendChild(element(doc, 'div', 'fkp-line' + (tone ? ' fkp-tone-' + tone : ''), text));
    if (!update.updating && update.available && update.state === 'failed' && update.message) {
      lines.appendChild(element(doc, 'div', 'fkp-line fkp-tone-error', update.message));
    }
    scroll.appendChild(lines);
    if (!(update.available && !update.updating)) {
      const buttons = element(doc, 'div', 'fkp-buttons');
      const busy = !!(update.checking || update.updating);
      buttons.appendChild(button(doc, update.checking ? 'Checking…' : 'Check for updates', 'check', app, '', { disabled: busy, className: 'fkp-wide' + (update.checking ? ' fkp-busy' : '') }));
      scroll.appendChild(buttons);
    }
    scroll.appendChild(toggleRow(doc, 'Check for updates automatically', !!update.auto, true, !!update.autoBusy, 'auto', app, '', !!update.updating));
  }

  function renderSteamvr(doc, scroll, app) {
    const lines = element(doc, 'div', 'fkp-lines');
    if (app.steamvrRestarting) {
      lines.appendChild(element(doc, 'div', 'fkp-line fkp-busy', 'Restarting SteamVR…'));
      scroll.appendChild(lines);
      return;
    }
    lines.appendChild(element(doc, 'div', 'fkp-line fkp-tone-warn', 'Restart SteamVR to finish the update.'));
    scroll.appendChild(lines);
    if (st.confirmSteamvr !== app.id) {
      const buttons = element(doc, 'div', 'fkp-buttons');
      buttons.appendChild(button(doc, 'Restart SteamVR…', 'steamvr-ask', app, '', { primary: true, className: 'fkp-wide' }));
      scroll.appendChild(buttons);
      return;
    }
    const card = element(doc, 'div', 'fkp-confirm');
    card.appendChild(element(doc, 'div', 'fkp-confirm-title', 'This will close all open VR apps and games.'));
    const answer = element(doc, 'div', 'fkp-buttons');
    answer.appendChild(button(doc, 'Restart', 'restart-steamvr', app, '', { className: 'fkp-danger' }));
    answer.appendChild(button(doc, 'Cancel', 'steamvr-cancel', app, ''));
    card.appendChild(answer);
    scroll.appendChild(card);
  }

  function renderRemove(doc, scroll, app) {
    scroll.appendChild(element(doc, 'div', 'fkp-section', 'Remove'));
    if (app.removing) {
      const lines = element(doc, 'div', 'fkp-lines');
      lines.appendChild(element(doc, 'div', 'fkp-line fkp-busy', 'Removing ' + app.name + '…'));
      scroll.appendChild(lines);
      return;
    }
    const updating = !!(app.update && app.update.updating);
    if (st.confirm !== app.id || updating) {
      const buttons = element(doc, 'div', 'fkp-buttons');
      buttons.appendChild(button(doc, 'Remove…', 'uninstall-ask', app, '', { disabled: updating, className: 'fkp-wide fkp-danger' }));
      scroll.appendChild(buttons);
      return;
    }
    const card = element(doc, 'div', 'fkp-confirm');
    card.appendChild(element(doc, 'div', 'fkp-confirm-title', 'Remove ' + app.name + '?'));
    if (app.hasData !== false) {
      card.appendChild(toggleRow(doc, 'Also delete its settings and data', st.purge, true, false, 'purge', app, '', false));
    }
    const buttons = element(doc, 'div', 'fkp-buttons');
    buttons.appendChild(button(doc, 'Remove', 'uninstall', app, '', { className: 'fkp-danger' }));
    buttons.appendChild(button(doc, 'Cancel', 'uninstall-cancel', app, ''));
    card.appendChild(buttons);
    scroll.appendChild(card);
  }

  function renderDetail(doc, scroll, app) {
    const hero = element(doc, 'div', 'fkp-hero');
    hero.appendChild(icon(doc, app));
    const text = element(doc, 'div', 'fkp-text');
    text.appendChild(element(doc, 'div', 'fkp-name', app.name));
    text.appendChild(element(doc, 'div', 'fkp-caption', 'Version ' + (app.version || '?')));
    hero.appendChild(text);
    scroll.appendChild(hero);

    const lines = element(doc, 'div', 'fkp-lines');
    if (app.problem) lines.appendChild(element(doc, 'div', 'fkp-line fkp-tone-error', 'Error: ' + app.problem));
    (app.lines || []).forEach(function (line) {
      lines.appendChild(element(doc, 'div', 'fkp-line' + (line.tone ? ' fkp-tone-' + line.tone : ''), line.text));
    });
    if (!app.problem && !(app.lines || []).length && app.statusKnown === false && app.hasStatus) {
      lines.appendChild(element(doc, 'div', 'fkp-line fkp-muted', '…'));
    }
    if (lines.firstChild) scroll.appendChild(lines);

    if ((app.steamvrRestart || app.steamvrRestarting) && !app.removing) renderSteamvr(doc, scroll, app);

    const update = app.update || {};
    if (update.enabled && update.available && !update.updating && !app.removing) {
      const offer = element(doc, 'div', 'fkp-buttons');
      offer.appendChild(button(doc, 'Update to ' + update.latest, 'update', app, '', { primary: true, className: 'fkp-wide' }));
      scroll.appendChild(offer);
    }

    if ((app.actions || []).length) {
      const buttons = element(doc, 'div', 'fkp-buttons');
      app.actions.forEach(function (action) {
        const wide = app.actions.length === 1 || action.wide ? ' fkp-wide' : wideFor(action.label);
        buttons.appendChild(button(doc, action.busy ? action.label + '…' : action.label, action.confirm ? 'action-ask' : 'action', app, action.id,
          { primary: action.style === 'primary', disabled: !!action.busy || !!app.removing || !!action.disabled,
            className: (action.busy ? 'fkp-busy' : '') + wide + (action.style === 'danger' ? ' fkp-danger' : '') }));
      });
      scroll.appendChild(buttons);
    }
    const asking = (app.actions || []).filter(function (action) {
      return action.confirm && st.confirmAction === app.id + '/' + action.id && !action.busy && !action.disabled && !app.removing;
    })[0];
    if (asking) {
      const card = element(doc, 'div', 'fkp-confirm');
      card.appendChild(element(doc, 'div', 'fkp-confirm-title', asking.confirm));
      const answer = element(doc, 'div', 'fkp-buttons');
      answer.appendChild(button(doc, asking.label, 'action', app, asking.id,
        { primary: asking.style === 'primary', className: (asking.style === 'primary' ? '' : 'fkp-danger') + wideFor(asking.label) }));
      answer.appendChild(button(doc, 'Cancel', 'action-cancel', app, asking.id, { className: wideFor(asking.label).trim() }));
      card.appendChild(answer);
      scroll.appendChild(card);
    } else if (st.confirmAction.indexOf(app.id + '/') === 0) {
      st.confirmAction = '';
    }
    (app.toggles || []).forEach(function (toggle) {
      scroll.appendChild(toggleRow(doc, toggle.label, !!toggle.on, !!toggle.known, !!toggle.busy, 'toggle', app, toggle.id, !!app.removing || !!toggle.disabled));
    });
    if (app.note && app.note.text) {
      scroll.appendChild(element(doc, 'div', 'fkp-note' + (app.note.tone ? ' fkp-tone-' + app.note.tone : ''), app.note.text));
    }
    renderUpdates(doc, scroll, app);
    renderRemove(doc, scroll, app);
    if (apps().length === 1) renderPairing(doc, scroll);
  }

  function renderPanel(doc) {
    const panel = doc.getElementById('fkp-panel');
    const back = doc.getElementById('fkp-back');
    if (!panel || !back) return;
    if (st.pointerDown && panel.firstChild) return;
    const stale = isStale();
    const list = apps().map(viewApp);
    const view = currentView();
    const app = list.filter(function (a) { return a.id === view; })[0] || null;
    back.hidden = !(app && list.length > 1);
    const signature = JSON.stringify([stale, view, st.confirm, st.purge, st.confirmPcs, st.confirmAction, st.confirmSteamvr, pairing(), stale ? null : (app || list)]);
    if (signature === st.drawn && panel.firstChild) return;
    const oldScroll = panel.querySelector('.fkp-scroll');
    const keepScroll = oldScroll && oldScroll.getAttribute('data-fkp-view') === view ? oldScroll.scrollTop : 0;
    st.drawn = signature;
    const scroll = element(doc, 'div', 'fkp-scroll');
    scroll.setAttribute('data-fkp-view', view);
    if (stale) {
      const empty = element(doc, 'div', 'fkp-empty');
      empty.appendChild(element(doc, 'div', 'fkp-empty-title', 'Not responding'));
      empty.appendChild(element(doc, 'div', 'fkp-empty-text', 'The community app service is not responding. Please wait. If this message persists, please restart the headset.'));
      scroll.appendChild(empty);
    } else if (app) {
      renderDetail(doc, scroll, app);
    } else {
      renderList(doc, scroll, list);
    }
    panel.textContent = '';
    panel.appendChild(scroll);
    scroll.scrollTop = keepScroll;
  }

  function queueAction(action, pendingKey, pendingValue) {
    st.seq += 1;
    action.seq = st.seq;
    action.session = st.session;
    st.queue.push(action);
    if (pendingKey) st.pending.set(pendingKey, Object.assign({ seq: st.seq, at: Date.now() }, pendingValue || {}));
    st.drawn = '';
    syncPopup();
    notify('action');
  }

  function onPanelClick(event) {
    const target = event.target && event.target.closest ? event.target.closest('[data-fkp-op]') : null;
    if (!target || target.disabled) return;
    event.stopPropagation();
    const op = target.getAttribute('data-fkp-op');
    const module = target.getAttribute('data-fkp-module') || '';
    const id = target.getAttribute('data-fkp-id') || '';
    const on = target.getAttribute('data-fkp-on') === '1';
    if (op === 'open') {
      st.view = module;
      st.confirm = '';
      st.confirmPcs = false;
      st.confirmAction = '';
      st.confirmSteamvr = '';
      st.drawn = '';
      syncPopup();
      notify('view');
    } else if (op === 'action') {
      st.confirmAction = '';
      st.confirmSteamvr = '';
      queueAction({ op: 'action', module: module, id: id }, module + '/action/' + id);
    } else if (op === 'action-ask') {
      st.confirmAction = module + '/' + id;
      st.drawn = '';
      syncPopup();
    } else if (op === 'action-cancel') {
      st.confirmAction = '';
      st.confirmSteamvr = '';
      st.drawn = '';
      syncPopup();
    } else if (op === 'steamvr-ask') {
      st.confirmSteamvr = module;
      st.drawn = '';
      syncPopup();
    } else if (op === 'steamvr-cancel') {
      st.confirmSteamvr = '';
      st.drawn = '';
      syncPopup();
    } else if (op === 'restart-steamvr') {
      st.confirmSteamvr = '';
      queueAction({ op: 'restart-steamvr', module: module }, module + '/restart-steamvr');
    } else if (op === 'toggle') {
      queueAction({ op: 'toggle', module: module, id: id, on: !on }, module + '/toggle/' + id, { on: !on });
    } else if (op === 'check') {
      queueAction({ op: 'check', module: module }, module + '/check');
    } else if (op === 'update') {
      queueAction({ op: 'update', module: module }, module + '/update');
    } else if (op === 'auto') {
      queueAction({ op: 'auto', module: module, on: !on }, module + '/auto', { on: !on });
    } else if (op === 'uninstall-ask') {
      st.confirm = module;
      st.purge = false;
      st.drawn = '';
      syncPopup();
    } else if (op === 'purge') {
      st.purge = !st.purge;
      st.drawn = '';
      syncPopup();
    } else if (op === 'uninstall-cancel') {
      st.confirm = '';
      st.drawn = '';
      syncPopup();
    } else if (op === 'uninstall') {
      const purge = st.purge;
      st.confirm = '';
      queueAction({ op: 'uninstall', module: module, purge: purge }, module + '/uninstall');
    } else if (op === 'pcs-ask') {
      st.confirmPcs = true;
      st.drawn = '';
      syncPopup();
    } else if (op === 'pcs-cancel') {
      st.confirmPcs = false;
      st.drawn = '';
      syncPopup();
    } else if (op === 'forget-pcs') {
      st.confirmPcs = false;
      queueAction({ op: 'forget-pcs', module: '' }, '#pcs');
    }
  }

  function syncPopup() {
    if (st.syncing) return;
    st.syncing = true;
    try {
      const doc = st.popupDoc;
      const menu = doc && doc.getElementById('QuickAccess-Menu');
      if (!menu) {
        if (st.popupOpen) {
          st.popupOpen = false;
          st.tabActive = false;
          st.confirm = '';
          st.confirmPcs = false;
          st.confirmAction = '';
          st.confirmSteamvr = '';
          notify('popup-closed');
        }
        return;
      }
      if (!st.popupOpen) {
        st.popupOpen = true;
        notify('popup-opened');
      }
      if (!st.classes) st.classes = learnClasses(doc);
      if (!st.classes) return;
      ensurePopupStyle(doc);
      const tab = ensureTab(doc);
      const wrapper = ensureWrapper(doc);
      if (!tab || !wrapper) return;
      const tabClass = st.classes.tab.concat(st.tabActive ? st.classes.tabSelected : []).join(' ');
      if (tab.className !== tabClass) tab.className = tabClass;
      applyTabState(doc, tab, wrapper);
      if (st.tabActive) renderPanel(doc);
    } finally {
      st.syncing = false;
    }
  }

  function onPopupClickCapture(event) {
    const target = event.target;
    const tab = target && target.closest ? target.closest('[role="tab"]') : null;
    if (tab && tab.id !== 'fkp-tab' && st.tabActive) {
      st.tabActive = false;
      st.confirm = '';
      st.confirmPcs = false;
      st.confirmAction = '';
      st.confirmSteamvr = '';
      syncPopup();
      notify('tab');
    }
  }

  function detachPopup() {
    if (st.popupObserver) {
      try { st.popupObserver.disconnect(); } catch (error) { }
    }
    const doc = st.popupDoc;
    if (doc) {
      try {
        doc.removeEventListener('click', onPopupClickCapture, true);
        ['fkp-tab', 'fkp-wrapper', 'fkp-popup-style'].forEach(function (id) {
          const node = doc.getElementById(id);
          if (node) node.remove();
        });
        Array.prototype.forEach.call(doc.querySelectorAll('[data-fkp-hidden],[data-fkp-deselected]'), function (node) {
          node.removeAttribute('data-fkp-hidden');
          node.removeAttribute('data-fkp-deselected');
        });
      } catch (error) { }
    }
    st.popupObserver = null;
    st.popupDoc = null;
    st.popupWindow = null;
    st.classes = null;
    st.popupOpen = false;
    st.tabActive = false;
    st.drawn = '';
  }

  function attachPopup() {
    const win = findPopupWindow();
    const doc = win ? win.document : null;
    if (doc === st.popupDoc) return;
    const wasOpen = st.popupOpen;
    detachPopup();
    if (wasOpen) notify('popup-closed');
    if (!doc || !doc.body) return;
    st.popupWindow = win;
    st.popupDoc = doc;
    const Observer = win.MutationObserver || MutationObserver;
    st.popupObserver = new Observer(function () { syncPopup(); });
    st.popupObserver.observe(doc.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'aria-selected'] });
    doc.addEventListener('click', onPopupClickCapture, true);
    syncPopup();
  }

  function onVisibilityChange() {
    notify('visibility');
  }

  function onTimer() {
    attachPopup();
    const stale = isStale();
    if (stale !== st.wasStale || st.pending.size) {
      st.wasStale = stale;
      st.drawn = '';
      if (st.popupDoc) syncPopup();
    }
  }

  function report() {
    const out = {
      state: 'ok',
      visible: document.visibilityState === 'visible',
      popupOpen: st.popupOpen,
      tabActive: st.popupOpen && st.tabActive,
      view: currentView(),
      popup: st.popupDoc ? 'attached' : 'missing',
      actions: st.queue
    };
    st.queue = [];
    return JSON.stringify(out);
  }

  function update(data, icons) {
    if (icons && typeof icons === 'object') {
      st.icons = icons;
      st.drawn = '';
    }
    if (data) {
      st.data = data;
      const handled = data.handled || {};
      if (handled.session === st.session) {
        st.pending.forEach(function (entry, key) {
          if (entry.seq <= handled.seq) st.pending.delete(key);
        });
      }
    }
    if (st.data) st.lastData = Date.now();
    attachPopup();
    if (st.popupDoc) syncPopup();
    return report();
  }

  function dispose() {
    clearInterval(st.timer);
    document.removeEventListener('visibilitychange', onVisibilityChange);
    detachPopup();
    if (window.__fkp === api) delete window.__fkp;
    return 'removed';
  }

  document.addEventListener('visibilitychange', onVisibilityChange);
  st.timer = setInterval(onTimer, 1000);

  const api = { version: version, update: update, dispose: dispose };
  window.__fkp = api;
  return api;
})
