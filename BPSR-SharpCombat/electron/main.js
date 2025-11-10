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
    console.log('Renderer requested closing all windows');
    const all = BrowserWindow.getAllWindows();
    for (const w of all) {
      try { w.close(); } catch (ex) { console.error('Error closing window:', ex); }
    }
    // After closing windows, quit the app so cleanup handlers run (cross-platform)
    try { app.quit(); } catch (ex) { console.error('Error calling app.quit():', ex); }
  } catch (ex) {
    console.error('Error in app:close-window handler:', ex);
  }
  return { ok: true };
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
        // Best-effort: send a kill to the child process. This is cross-platform.
        serverProcess.kill();

        // Wait a short period for it to exit
        const start = Date.now();
        while (serverProcess && (Date.now() - start) < 2000) {
          if (serverProcess.exitCode !== null) break;
          await new Promise((res) => setTimeout(res, 100));
        }
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

  const options = {
    width: 800,
    height: 600,
    frame: false,
    transparent: true,
    alwaysOnTop: true,
    skipTaskbar: false,
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

  // when window moves or resizes, debounce saving
  let saveTimer = null;
  const scheduleSave = () => {
    if (saveTimer) clearTimeout(saveTimer);
    saveTimer = setTimeout(() => {
      try {
        const b = mainWindow.getBounds();
        saveWindowState({ bounds: b });
      } catch (ex) { }
    }, 500);
  };

  mainWindow.on('move', scheduleSave);
  mainWindow.on('resize', scheduleSave);
  mainWindow.on('close', () => {
    try {
      const b = mainWindow.getBounds();
      saveWindowState({ bounds: b });
    } catch (ex) { }
  });

  // If APP_URL is explicitly provided (dev mode), just load that and skip local-server logic
  const appUrl = process.env.APP_URL;
  if (appUrl) {
    console.log('APP_URL detected, loading:', appUrl);
    mainWindow.loadURL(appUrl);
    return;
  }

  const appIndex = path.join(__dirname, 'app', 'index.html');
  const serverDir = path.join(__dirname, 'server');
  const serverUrl = process.env.APP_URL || 'http://localhost:5000';

  if (fs.existsSync(appIndex)) {
    // Serve the Blazor published files from the local 'app' folder using a custom protocol
    protocol.registerFileProtocol('app', (request, callback) => {
      const url = request.url.replace('app:///', '');
      const decoded = decodeURI(url);
      const filePath = path.join(__dirname, 'app', decoded);
      callback({ path: filePath });
    });

    // Load the index.html from the app folder via our protocol
    mainWindow.loadURL('app:///index.html');
  } else if (fs.existsSync(serverDir)) {
    // If a published server exists in electron/server, try to start it and wait until it responds
    try {
      // find executable in serverDir
      const files = fs.readdirSync(serverDir);
      let exeName;
      if (process.platform === 'win32') {
        exeName = files.find(f => f.toLowerCase().endsWith('.exe'));
      } else {
        // pick the first non-directory file (likely the runtime-executable produced by dotnet publish -r)
        exeName = files.find(f => fs.statSync(path.join(serverDir, f)).isFile());
      }

      if (exeName) {
        const exePath = path.join(serverDir, exeName);
        console.log('Starting server executable:', exePath);
        serverProcess = spawn(exePath, [], { cwd: serverDir, detached: false });
        serverProcess.stdout?.on('data', d => console.log(`[server] ${d.toString()}`));
        serverProcess.stderr?.on('data', d => console.error(`[server-err] ${d.toString()}`));
        serverProcess.on('exit', (code) => console.log('Server process exited with', code));

        // Wait until server responds, then load it (or fallback to local static assets)
        waitForServer(serverUrl, 20000).then(async () => {
          try {
            const probe = await probeUrl(serverUrl);
            const body = (probe.body || '').toLowerCase();
            const isError = (probe.statusCode >= 400) || body.includes('an unhandled exception') || body.includes('ambiguousmatchexception');
            if (!isError) {
              mainWindow.loadURL(serverUrl);
            } else if (fs.existsSync(appIndex)) {
              console.warn('Server root appears to be returning an error page; falling back to local app index');
              mainWindow.loadURL('app:///index.html');
            } else {
              mainWindow.loadURL(serverUrl);
            }
          } catch (ex) {
            console.error('Error probing server URL, loading server URL anyway:', ex);
            mainWindow.loadURL(serverUrl);
          }
        }).catch((err) => {
          console.error('Server did not start in time:', err);
          // Fallback: still try to load the URL or local app if available
          if (fs.existsSync(appIndex)) mainWindow.loadURL('app:///index.html'); else mainWindow.loadURL(serverUrl);
        });
      } else {
        console.warn('No executable found in server folder; falling back to server URL');
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
    try { serverProcess.kill(); } catch { }
  }
  if (process.platform !== 'darwin') app.quit();
});

app.on('quit', function () {
  if (serverProcess) {
    try { serverProcess.kill(); } catch { }
  }
});
