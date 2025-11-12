const { app, BrowserWindow, protocol, ipcMain } = require('electron');
const path = require('path');
const fs = require('fs');
const { spawn } = require('child_process');
const http = require('http');

const WINDOW_STATE_FILE = 'window-state.json';

// Register privileged schemes before app is ready (required by Electron)
protocol.registerSchemesAsPrivileged([{ scheme: 'app', privileges: { secure: true, standard: true } }]);

let mainWindow;
let serverProcess = null;
// When true, window close handlers will NOT remove tracked entries from disk.
// This is used when the renderer requests a full app close so we preserve the
// list of tracked windows to reopen on next launch.
let suppressTrackedWindowRemoval = false;

// Helper: kill a process tree cross-platform. On Windows use taskkill, on *nix kill the process group.
function killProcessTreePid(pid) {
  return new Promise((resolve) => {
    if (!pid || typeof pid !== 'number') return resolve();
    try {
      if (process.platform === 'win32') {
        // Use taskkill to terminate the process and its children.
        const killer = spawn('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
        killer.on('exit', () => resolve());
        killer.on('error', () => resolve());
      } else {
        try {
          // Negative PID kills the process group when the child was spawned with detached: true
          process.kill(-pid, 'SIGTERM');
          resolve();
        } catch (ex) {
          // Fallback: try to kill the pid directly
          try { process.kill(pid, 'SIGTERM'); } catch (_) { }
          resolve();
        }
      }
    } catch (ex) {
      resolve();
    }
  });
}

async function killServerProcess() {
  try {
    if (!serverProcess) return;
    const pid = serverProcess.pid;
    try {
      // First try a polite kill on the child (best-effort)
      serverProcess.kill();
    } catch (ex) { }

    // Wait shortly for exit
    const start = Date.now();
    while (serverProcess && (Date.now() - start) < 2000) {
      if (serverProcess.exitCode !== null) break;
      // If the child has a 'killed' flag, break
      if (serverProcess.killed) break;
      // eslint-disable-next-line no-await-in-loop
      await new Promise((r) => setTimeout(r, 100));
    }

    // If still running, kill the whole tree (platform-specific)
    if (serverProcess && serverProcess.exitCode === null) {
      await killProcessTreePid(pid);
    }
  } catch (ex) {
    console.error('Error while killing server process tree:', ex);
  } finally {
    serverProcess = null;
  }
}

function waitForServer(url, timeoutMs = 20000) {
  return new Promise((resolve, reject) => {
    const start = Date.now();
    const check = () => {
      http.get(url, (_) => {
        resolve();
      }).on('error', () => {
        if (Date.now() - start > timeoutMs) {
          reject(new Error('Timeout waiting for server to start'));
        } else {
          setTimeout(check, 500);
        }
      });
    };
    check();
  });
}

function probeUrl(url, timeoutMs = 5000) {
  return new Promise((resolve) => {
    try {
      const req = http.get(url, (res) => {
        let data = '';
        res.setEncoding('utf8');
        res.on('data', (chunk) => {
          if (data.length < 4096) data += chunk; // only buffer a small prefix
        });
        res.on('end', () => {
          resolve({ statusCode: res.statusCode || 0, body: data });
        });
      });
      req.on('error', () => resolve({ statusCode: 0, body: '' }));
      req.setTimeout(timeoutMs, () => {
        req.destroy();
        resolve({ statusCode: 0, body: '' });
      });
    } catch (ex) {
      resolve({ statusCode: 0, body: '' });
    }
  });
}

// Write a tiny startup log in userData so installed apps can surface spawn paths/errors
function writeStartupLog(msg) {
  try {
    const logDir = app && app.getPath ? app.getPath('userData') : __dirname;
    const p = path.join(logDir, 'startup.log');
    const line = (new Date()).toISOString() + ' ' + String(msg) + '\n';
    try { fs.mkdirSync(path.dirname(p), { recursive: true }); } catch (e) { }
    fs.appendFileSync(p, line, 'utf8');
  } catch (ex) {
    try { console.error('Failed to write startup log:', ex); } catch (_) { }
  }
}

function getWindowStateFilePath() {
  return path.join(app.getPath('userData'), WINDOW_STATE_FILE);
}

function loadWindowState() {
  try {
    const p = getWindowStateFilePath();
    if (fs.existsSync(p)) {
      const raw = fs.readFileSync(p, 'utf8');
      return JSON.parse(raw);
    }
  } catch (ex) {
    console.error('Failed to read window state:', ex);
  }
  return null;
}

function saveWindowState(state) {
  try {
    const p = getWindowStateFilePath();
    fs.mkdirSync(path.dirname(p), { recursive: true });
    fs.writeFileSync(p, JSON.stringify(state, null, 2), 'utf8');
  } catch (ex) {
    console.error('Failed to write window state:', ex);
  }
}

// Helper: update (or insert) a tracked window's url in persisted state
function updateTrackedWindowUrl(id, url) {
  try {
    if (!id) return;
    const s = loadWindowState() || {};
    s.windows = s.windows || [];
    const idx = s.windows.findIndex(w => String(w.id) === String(id));
    if (idx >= 0) {
      s.windows[idx].url = url;
    } else {
      // best-effort: insert an entry so navigation is persisted even if we didn't
      // previously track bounds. Bounds will be saved on move/resize later.
      s.windows.push({ id: id, url: url });
    }
    saveWindowState(s);
  } catch (ex) {
    // swallow errors - not critical
  }
}

// Expose IPC handlers for renderer to query/update window state
ipcMain.handle('window-state:get', async () => {
  return loadWindowState();
});

ipcMain.handle('window-state:set', async (_, state) => {
  saveWindowState(state);
});

// New: Close all windows request (renderer asks main to close windows only)
ipcMain.handle('app:close-window', async () => {
  try {
    console.log('Renderer requested closing all windows (preserve tracked windows)');
    // Prevent per-window close handlers from removing tracked entries so they
    // will be reopened on next launch. We restore the flag only if quitting
    // fails for some reason.
    suppressTrackedWindowRemoval = true;
    const all = BrowserWindow.getAllWindows();
    for (const w of all) {
      try { w.close(); } catch (ex) { console.error('Error closing window:', ex); }
    }
    // After closing windows, quit the app so cleanup handlers run (cross-platform)
    try { app.quit(); } catch (ex) { console.error('Error calling app.quit():', ex); }
    // If app.quit() didn't exit for some reason, reset the flag after a short delay
    setTimeout(() => { suppressTrackedWindowRemoval = false; }, 2000);
  } catch (ex) {
    console.error('Error in app:close-window handler:', ex);
  }
  return { ok: true };
});

// New: Close current window only (not the whole app)
ipcMain.handle('app:close-current-window', async (event) => {
  try {
    console.log('Renderer requested closing current window');
    const window = BrowserWindow.fromWebContents(event.sender);
    if (window) {
      window.close();
    }
  } catch (ex) {
    console.error('Error in app:close-current-window handler:', ex);
  }
  return { ok: true };
});

// New: Open a new window
ipcMain.handle('app:open-new-window', async (_, url, options = {}) => {
  try {
    console.log('Opening new window:', url, options);
    
    const windowOptions = {
      width: options.width || 900,
      height: options.height || 700,
      title: options.title || 'Settings',
      frame: false,
      transparent: true,
      // explicit fully-transparent background to avoid white fill on some platforms
      backgroundColor: '#00000000',
      alwaysOnTop: true,
      resizable: true,
      webPreferences: {
        preload: path.join(__dirname, 'preload.js'),
        contextIsolation: true,
      }
    };

    const newWindow = new BrowserWindow(windowOptions);
    // Try to apply the same always-on-top / fullscreen visibility rules as mainWindow
    try {
      if (typeof newWindow.setAlwaysOnTop === 'function') {
        newWindow.setAlwaysOnTop(true, 'screen-saver');
      }
      if (typeof newWindow.setVisibleOnAllWorkspaces === 'function') {
        try { newWindow.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true }); } catch (_) { newWindow.setVisibleOnAllWorkspaces(true); }
      }
      if (typeof newWindow.setFocusable === 'function') newWindow.setFocusable(true);
    } catch (ex) { console.error('Failed to set always-on-top/visibility for new window:', ex); }
    
    // Configure always-on-top behavior
    try {
      if (typeof newWindow.setAlwaysOnTop === 'function') {
        newWindow.setAlwaysOnTop(true, 'screen-saver');
      }
      if (typeof newWindow.setVisibleOnAllWorkspaces === 'function') {
        try { 
          newWindow.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true }); 
        } catch (_) { 
          newWindow.setVisibleOnAllWorkspaces(true); 
        }
      }
      if (typeof newWindow.setFocusable === 'function') {
        newWindow.setFocusable(true);
      }
    } catch (ex) {
      console.error('Failed to configure new window:', ex);
    }

    newWindow.loadURL(url);
    // assign id to window for tracking
    try {
      const state = loadWindowState() || {};
      state.windows = state.windows || [];
      const id = (Date.now()).toString();
      newWindow.__trackedId = id;
      const b = newWindow.getBounds();
      state.windows.push({ id: id, url: url, x: b.x, y: b.y, width: b.width, height: b.height, title: windowOptions.title || '' });
      saveWindowState(state);
    } catch (ex) { console.error('Failed to persist new window state:', ex); }

    // persist navigations (in-page pushState and full navigations)
    try {
      newWindow.webContents.on('did-navigate', (e, u) => { try { updateTrackedWindowUrl(newWindow.__trackedId, u); } catch (_) { } });
      newWindow.webContents.on('did-navigate-in-page', (e, u) => { try { updateTrackedWindowUrl(newWindow.__trackedId, u); } catch (_) { } });
    } catch (ex) { }

    // save bounds on move/resize for this new window
    try {
      let saveTimer = null;
      const scheduleSave = () => {
        if (saveTimer) clearTimeout(saveTimer);
        saveTimer = setTimeout(() => {
          try {
            const s = loadWindowState() || {};
            s.windows = s.windows || [];
            const b = newWindow.getBounds();
            const idx = s.windows.findIndex(w => w.id === newWindow.__trackedId);
            if (idx >= 0) {
              s.windows[idx].x = b.x; s.windows[idx].y = b.y; s.windows[idx].width = b.width; s.windows[idx].height = b.height;
            }
            saveWindowState(s);
          } catch (ex) { }
        }, 500);
      };
      newWindow.on('move', scheduleSave);
      newWindow.on('resize', scheduleSave);
      newWindow.on('close', () => {
        try {
          if (!suppressTrackedWindowRemoval) {
            const s = loadWindowState() || {};
            s.windows = s.windows || [];
            s.windows = s.windows.filter(w => w.id !== newWindow.__trackedId);
            saveWindowState(s);
          }
        } catch (ex) { }
      });
    } catch (ex) { }

    return { ok: true };
  } catch (ex) {
    console.error('Error opening new window:', ex);
    return { ok: false, error: ex.message };
  }
});

// Close a specific window by tracked id (if found)
ipcMain.handle('app:close-window-id', async (_, id) => {
  try {
    const all = BrowserWindow.getAllWindows();
    for (const w of all) {
      try {
        if (w.__trackedId && String(w.__trackedId) === String(id)) {
          w.close();
          // remove from persisted state
          try {
            const s = loadWindowState() || {};
            s.windows = s.windows || [];
            s.windows = s.windows.filter(x => String(x.id) !== String(id));
            saveWindowState(s);
          } catch (ex) { }
          return { ok: true };
        }
      } catch (ex) { }
    }
  } catch (ex) {
    console.error('Error closing window by id:', ex);
  }
  return { ok: false };
});

// Handle app close request from renderer: simplified cross-platform shutdown
ipcMain.handle('app:close', async () => {
  console.log('Renderer requested app close - simplified shutdown');
  try {
    // If we did not spawn a serverProcess, try an HTTP shutdown to the loaded origin (best-effort)
    if (!serverProcess && mainWindow) {
      try {
        const loaded = mainWindow.webContents.getURL();
        if (loaded && (loaded.startsWith('http://') || loaded.startsWith('https://'))) {
          try {
            const u = new URL(loaded);
            const shutdownUrl = `${u.protocol}//${u.hostname}${u.port ? ':' + u.port : ''}/api/host/shutdown`;
            console.log('Attempting HTTP shutdown request to', shutdownUrl);
            const req = http.request(shutdownUrl, { method: 'POST', timeout: 1500 }, (res) => {
              console.log('Shutdown endpoint responded with', res.statusCode);
            });
            req.on('error', () => { /* ignore */ });
            req.on('timeout', () => { req.destroy(); });
            req.end();
            // give the server a moment to begin shutdown
            await new Promise((res) => setTimeout(res, 700));
          } catch (ex) {
            console.error('HTTP shutdown attempt failed:', ex);
          }
        }
      } catch (ex) {
        console.error('Error while attempting HTTP shutdown:', ex);
      }
    }

    if (serverProcess) {
      try {
        console.log('Attempting to stop spawned server process (pid=' + serverProcess.pid + ')');
        await killServerProcess();
      } catch (ex) {
        console.error('Error stopping server process:', ex);
      }
    }
  } catch (ex) {
    console.error('Error during simplified app close handler:', ex);
  } finally {
    // Clear reference and quit the app
    serverProcess = null;
    try { app.quit(); } catch (ex) { console.error('Error quitting app:', ex); }
  }

  return { ok: true };
});

function createWindow() {
  // Try to load saved state
  const saved = loadWindowState();

  // Use the same base options as the "new window" path so the main window
  // isn't treated specially. This makes behavior (movability, always-on-top,
  // visibility) consistent between main and newly created windows.
  const options = {
    width: 800,
    height: 600,
    title: 'BPSR',
    frame: false,
    transparent: true,
    // explicit fully-transparent background to avoid white fill on some platforms
    backgroundColor: '#00000000',
    alwaysOnTop: true,
    resizable: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
    }
  };

  if (saved && saved.bounds) {
    options.x = saved.bounds.x;
    options.y = saved.bounds.y;
    options.width = saved.bounds.width;
    options.height = saved.bounds.height;
  }

  mainWindow = new BrowserWindow(options);

  // mark mainWindow with an id if the saved state included windows, else use a generated id
  try {
    // Use a stable saved mainId when available; otherwise generate a unique
    // id so it cannot collide with tracked child windows that might have
    // been saved using a literal 'main' value in older runs.
    if (saved && saved.mainId) mainWindow.__trackedId = saved.mainId;
    else mainWindow.__trackedId = 'main-' + Date.now().toString();
  } catch (ex) { }

  // Debug: log the main window tracked id so we can see if it collides with
  // any reopened tracked windows in runtime logs.
  try { console.log('Main window tracked id:', mainWindow.__trackedId); } catch (e) { }

  // If we have a saved windows entry for the main window, prefer restoring its last URL
  let initialSavedUrl = null;
  try {
    const ss = loadWindowState();
    if (ss && Array.isArray(ss.windows)) {
      const found = ss.windows.find(w => String(w.id) === String(ss.mainId) || String(w.id) === String(mainWindow.__trackedId));
      if (found && found.url) initialSavedUrl = found.url;
    }
  } catch (ex) { }

  // Attach navigation listeners so any route changes (pushState / in-page) are persisted
  try {
    const onNav = (event, url) => {
      try { updateTrackedWindowUrl(mainWindow.__trackedId, url); } catch (_) { }
    };
    mainWindow.webContents.on('did-navigate', onNav);
    mainWindow.webContents.on('did-navigate-in-page', onNav);
  } catch (ex) { }

  // Ensure the window stays above fullscreen apps/games when possible.
  // Use the highest z-order level supported by Electron and make the window visible on all workspaces
  // including fullscreen. Wrap in try/catch for compatibility with older Electron versions.
  try {
    // 'screen-saver' is the highest level on macOS and works on Windows to get above fullscreen apps in many cases
    if (typeof mainWindow.setAlwaysOnTop === 'function') {
      mainWindow.setAlwaysOnTop(true, 'screen-saver');
    }

    // Ensure the window is visible on all workspaces and can appear over fullscreen apps
    if (typeof mainWindow.setVisibleOnAllWorkspaces === 'function') {
      // Second argument object with visibleOnFullScreen is supported in modern Electron
      try { mainWindow.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true }); } catch (_) { mainWindow.setVisibleOnAllWorkspaces(true); }
    }

    // Also make sure the window stays focusable and on top
    if (typeof mainWindow.setFocusable === 'function') mainWindow.setFocusable(true);
  } catch (ex) {
    console.error('Failed to configure always-on-top/fullscreen visibility:', ex);
  }

  // when window moves or resizes, debounce saving
  let saveTimer = null;
  const scheduleSave = () => {
    if (saveTimer) clearTimeout(saveTimer);
    saveTimer = setTimeout(() => {
      try {
        const b = mainWindow.getBounds();
        // persist bounds as top-level and keep other windows array if present
        try {
          const s = loadWindowState() || {};
          s.bounds = b;
          // ensure mainId persists
          if (!s.mainId) s.mainId = mainWindow.__trackedId || 'main';
          saveWindowState(s);
        } catch (ex) {
          saveWindowState({ bounds: b });
        }
      } catch (ex) { }
    }, 500);
  };

  mainWindow.on('move', scheduleSave);
  mainWindow.on('resize', scheduleSave);
  mainWindow.on('close', () => {
    try {
      const b = mainWindow.getBounds();
      try {
        const s = loadWindowState() || {};
        s.bounds = b;
        if (!s.mainId) s.mainId = mainWindow.__trackedId || 'main';
        saveWindowState(s);
      } catch (ex) {
        saveWindowState({ bounds: b });
      }
    } catch (ex) { }
  });

  // Delay reopening tracked extra windows until the main window finishes loading.
  // This ensures any server-based content or local files are available before child
  // windows attempt to load their URLs. Previously reopening happened immediately
  // and could fail when the server wasn't ready, resulting in only the primary
  // window successfully showing.
  try {
    mainWindow.webContents.once('did-finish-load', () => {
      try {
        const savedState = loadWindowState();
        if (savedState && Array.isArray(savedState.windows)) {
          for (const w of savedState.windows) {
            try {
              // If a saved tracked window uses the same id as the main window,
              // skip reopening it to avoid creating a duplicate that can
              // overlay or conflict with the primary window.
              if (String(w.id) === String(mainWindow.__trackedId)) {
                console.warn('Skipping reopening tracked window because id matches main window:', w.id);
                continue;
              }
              console.log('Reopening tracked window:', w.url, 'id=', w.id);
              const nw = new BrowserWindow({
                x: w.x,
                y: w.y,
                width: w.width || 900,
                height: w.height || 700,
                frame: false,
                transparent: true,
                // explicit fully-transparent background so reopened windows don't show white
                backgroundColor: '#00000000',
                alwaysOnTop: true,
                resizable: true,
                webPreferences: { preload: path.join(__dirname, 'preload.js'), contextIsolation: true }
              });
              // Mirror main window visibility/always-on-top behavior for reopened windows
              try {
                if (typeof nw.setAlwaysOnTop === 'function') nw.setAlwaysOnTop(true, 'screen-saver');
                if (typeof nw.setVisibleOnAllWorkspaces === 'function') {
                  try { nw.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true }); } catch (_) { nw.setVisibleOnAllWorkspaces(true); }
                }
                if (typeof nw.setFocusable === 'function') nw.setFocusable(true);
              } catch (ex) { console.error('Failed to set always-on-top/visibility for reopened window:', ex); }
              nw.__trackedId = w.id;
              nw.loadURL(w.url);

              // save bounds on move/resize
              let t = null;
              const sched = () => { if (t) clearTimeout(t); t = setTimeout(() => {
                try {
                  const s = loadWindowState() || {};
                  s.windows = s.windows || [];
                  const idx = s.windows.findIndex(x => x.id === nw.__trackedId);
                  if (idx >= 0) {
                    const b = nw.getBounds();
                    s.windows[idx].x = b.x; s.windows[idx].y = b.y; s.windows[idx].width = b.width; s.windows[idx].height = b.height;
                    saveWindowState(s);
                  }
                } catch (e) {}
              }, 500); };
              nw.on('move', sched); nw.on('resize', sched);

              nw.on('close', () => {
                try {
                  if (!suppressTrackedWindowRemoval) {
                    const s = loadWindowState() || {};
                    s.windows = s.windows || [];
                    s.windows = s.windows.filter(x => x.id !== nw.__trackedId);
                    saveWindowState(s);
                  }
                } catch (e) {}
              });
              // persist navigations for reopened windows as well
              try {
                nw.webContents.on('did-navigate', (e, u) => { try { updateTrackedWindowUrl(nw.__trackedId, u); } catch (_) { } });
                nw.webContents.on('did-navigate-in-page', (e, u) => { try { updateTrackedWindowUrl(nw.__trackedId, u); } catch (_) { } });
              } catch (ex) { }
            } catch (ex) { console.error('Failed to reopen tracked window', ex); }
          }
        }
      } catch (ex) { console.error('Error while reopening tracked windows:', ex); }
    });
  } catch (ex) { console.error('Error scheduling reopen of tracked windows:', ex); }

  // If APP_URL is explicitly provided (dev mode), just load that and skip local-server logic
  const appUrl = process.env.APP_URL;
  if (appUrl) {
    console.log('APP_URL detected, loading:', appUrl);
    mainWindow.loadURL(initialSavedUrl || appUrl);
    return;
  }

  const appIndex = path.join(__dirname, 'app', 'index.html');
  const serverUrl = process.env.APP_URL || 'http://localhost:5000';

  // Locate possible server folders when running from source or from a packaged installer.
  // When packaged with asar, executables should be placed into the asarUnpack area
  // and will be available at process.resourcesPath + '/app.asar.unpacked/...'.
  const resourcePath = process.resourcesPath || __dirname;
  const serverCandidates = [
    path.join(__dirname, 'server'),
    path.join(resourcePath, 'server'),
    path.join(resourcePath, 'app', 'server'),
    path.join(resourcePath, 'app.asar.unpacked', 'server'),
    path.join(resourcePath, 'app.asar.unpacked')
  ];

  let serverDir = null;
  const candExists = {};
  for (const cand of serverCandidates) {
    try {
      const exists = !!fs.existsSync(cand);
      candExists[cand] = exists;
      if (exists && !serverDir) serverDir = cand;
    } catch (ex) { candExists[cand] = false; }
  }
  try { writeStartupLog('Server candidate existence: ' + JSON.stringify(candExists)); } catch (_) { }

  if (fs.existsSync(appIndex)) {
    // Serve the Blazor published files from the local 'app' folder using a custom protocol
    protocol.registerFileProtocol('app', (request, callback) => {
      const url = request.url.replace('app:///', '');
      const decoded = decodeURI(url);
      const filePath = path.join(__dirname, 'app', decoded);
      callback({ path: filePath });
    });

    // Load the index.html from the app folder via our protocol
    mainWindow.loadURL(initialSavedUrl || 'app:///index.html');
  } else if (serverDir) {
    // If a published server exists in electron/server, try to start it and wait until it responds
    try {
      // find executable in serverDir (be tolerant about where dotnet published the files)
      let exeName = null;
      // Prefer looking inside unpacked ASAR paths first
      const unpackedServerDir = path.join(resourcePath, 'app.asar.unpacked', 'server');
      const unpackedRoot = path.join(resourcePath, 'app.asar.unpacked');
      const trySearchDirs = [unpackedServerDir, unpackedRoot, serverDir, path.join(resourcePath, 'server'), path.join(resourcePath, 'app', 'server')];
      try { writeStartupLog('Searching for executables in dirs: ' + JSON.stringify(trySearchDirs)); } catch (_) { }
      for (const d of trySearchDirs) {
        try {
          if (!d || !fs.existsSync(d)) continue;
          const files = fs.readdirSync(d);
          if (process.platform === 'win32') {
            const f = files.find(x => x.toLowerCase().endsWith('.exe'));
            if (f) { exeName = f; serverDir = d; break; }
          } else {
            const f = files.find(x => fs.statSync(path.join(d, x)).isFile());
            if (f) { exeName = f; serverDir = d; break; }
          }
        } catch (ex) { /* ignore directory read errors */ }
      }

      // If we didn't find an executable directly under serverDir, try some fallback locations
      const fallbackRoots = [resourcePath, path.join(resourcePath, 'app'), path.join(resourcePath, 'app.asar.unpacked')];
      if (!exeName) {
        for (const root of fallbackRoots) {
          try {
            const list = fs.existsSync(root) ? fs.readdirSync(root) : [];
            if (process.platform === 'win32') {
              const f = list.find(x => x.toLowerCase().endsWith('.exe'));
              if (f) { exeName = f; serverDir = root; break; }
            } else {
              const f = list.find(x => fs.statSync(path.join(root, x)).isFile());
              if (f) { exeName = f; serverDir = root; break; }
            }
          } catch (_) { }
        }
      }

        if (exeName) {
        // Prefer unpacked ASAR paths first to avoid spawning files inside app.asar
        const tryPaths = [
          path.join(resourcePath, 'app.asar.unpacked', 'server', exeName),
          path.join(resourcePath, 'app.asar.unpacked', exeName),
          path.join(serverDir || '', exeName),
          path.join(resourcePath, 'server', exeName),
          path.join(resourcePath, exeName),
          path.join(resourcePath, 'app', 'server', exeName)
        ];
        try { writeStartupLog('Candidate exe tryPaths: ' + JSON.stringify(tryPaths)); } catch (_) { }
        let exePath = null;
        for (const p of tryPaths) {
          try { if (fs.existsSync(p)) { exePath = p; break; } } catch (_) { }
        }
        if (!exePath) {
          console.error('Could not locate server executable from candidates:', tryPaths);
          throw new Error('Server executable not found for spawn');
        }

        console.log('Starting server executable:', exePath);
        writeStartupLog('Attempting to spawn server executable: ' + exePath);
        // Use a process group on non-Windows so we can kill the whole tree later.
        const spawnOpts = { cwd: path.dirname(exePath), detached: (process.platform !== 'win32'), stdio: 'pipe', windowsHide: true };
        try {
          if (!fs.existsSync(exePath)) throw new Error('Executable not found (existsSync returned false)');
          serverProcess = spawn(exePath, [], spawnOpts);
        } catch (spawnErr) {
          // Log and persist the error for installed app debugging
          console.error('Failed to spawn server executable directly:', spawnErr && spawnErr.message ? spawnErr.message : spawnErr);
          writeStartupLog('Spawn error: ' + (spawnErr && spawnErr.stack ? spawnErr.stack : String(spawnErr)));
          // On Windows, try a cmd.exe fallback which sometimes succeeds when CreateProcess fails
          if (process.platform === 'win32') {
            try {
              console.log('Attempting cmd.exe fallback to start server');
              writeStartupLog('Attempting cmd.exe fallback for: ' + exePath);
              serverProcess = spawn('cmd.exe', ['/c', exePath], spawnOpts);
            } catch (fallbackErr) {
              console.error('cmd.exe fallback failed:', fallbackErr);
              writeStartupLog('cmd.exe fallback error: ' + (fallbackErr && fallbackErr.stack ? fallbackErr.stack : String(fallbackErr)));
            }
          }
        }

        if (serverProcess) {
          serverProcess.stdout?.on('data', d => console.log(`[server] ${d.toString()}`));
          serverProcess.stderr?.on('data', d => console.error(`[server-err] ${d.toString()}`));
          serverProcess.on('exit', (code) => console.log('Server process exited with', code));
          serverProcess.on('error', (err) => {
            console.error('Server process emitted error event:', err);
            writeStartupLog('Server process error event: ' + (err && err.stack ? err.stack : String(err)));
          });
        } else {
          console.error('Server process was not created (spawn returned null/undefined)');
          writeStartupLog('Server process was not created for path: ' + exePath);
        }

        // Wait until server responds, then load it (or fallback to local static assets)
        waitForServer(serverUrl, 20000).then(async () => {
          try {
            const probe = await probeUrl(serverUrl);
            const body = (probe.body || '').toLowerCase();
            const isError = (probe.statusCode >= 400) || body.includes('an unhandled exception') || body.includes('ambiguousmatchexception');
            if (!isError) {
              mainWindow.loadURL(initialSavedUrl || serverUrl);
            } else if (fs.existsSync(appIndex)) {
              console.warn('Server root appears to be returning an error page; falling back to local app index');
              mainWindow.loadURL(initialSavedUrl || 'app:///index.html');
            } else {
              mainWindow.loadURL(initialSavedUrl || serverUrl);
            }
          } catch (ex) {
            console.error('Error probing server URL, loading server URL anyway:', ex);
            mainWindow.loadURL(initialSavedUrl || serverUrl);
          }
        }).catch((err) => {
          console.error('Server did not start in time:', err);
          // Fallback: still try to load the URL or local app if available
          if (fs.existsSync(appIndex)) mainWindow.loadURL(initialSavedUrl || 'app:///index.html'); else mainWindow.loadURL(initialSavedUrl || serverUrl);
        });
        } else {
          console.warn('No executable found in server folder; falling back to server URL');
          try {
            // Log directory listings to help debugging where files landed
            const resList = fs.existsSync(resourcePath) ? fs.readdirSync(resourcePath) : [];
            writeStartupLog('resourcePath listing: ' + JSON.stringify(resList));
            const unpackedList = fs.existsSync(unpackedServerDir) ? fs.readdirSync(unpackedServerDir) : [];
            writeStartupLog('unpacked server listing: ' + JSON.stringify(unpackedList));
            const serverDirList = serverDir && fs.existsSync(serverDir) ? fs.readdirSync(serverDir) : [];
            writeStartupLog('serverDir listing: ' + JSON.stringify(serverDirList));
          } catch (ex) { /* ignore logging errors */ }
          mainWindow.loadURL(serverUrl);
      }
    } catch (ex) {
      console.error('Error while trying to start server:', ex);
      mainWindow.loadURL(serverUrl);
    }
  } else {
    // Fallback: load a locally running server (use APP_URL env or default http://localhost:5000)
    console.log('No local app folder or server folder found. Loading server URL:', serverUrl);
    mainWindow.loadURL(serverUrl);
  }

  // Optional: make window click-through but still allow interactive areas to receive events
  // mainWindow.setIgnoreMouseEvents(true, { forward: true });

  mainWindow.on('closed', function () {
    mainWindow = null;
  });
}

app.whenReady().then(() => {
  createWindow();

  app.on('activate', function () {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', function () {
  if (serverProcess) {
    try { killServerProcess().catch(() => {}); } catch { }
  }
  if (process.platform !== 'darwin') app.quit();
});

app.on('quit', function () {
  if (serverProcess) {
    try { killServerProcess().catch(() => {}); } catch { }
  }
});




