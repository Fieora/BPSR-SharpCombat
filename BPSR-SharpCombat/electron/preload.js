// preload.js - empty for now, provided for secure contextIsolation usage
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('electron', {
  getWindowState: () => ipcRenderer.invoke('window-state:get'),
  setWindowState: (state) => ipcRenderer.invoke('window-state:set', state),
  // Version API (sync)
  getAppVersionSync: () => {
    try {
      // Use relative path which works better with asar/webpack/etc
      const pkg = require('./package.json');
      return pkg.version || null;
    } catch (e) {
      console.error('getAppVersionSync error:', e);
      return null;
    }
  },
  // Update API
  checkForUpdates: () => ipcRenderer.invoke('check-for-updates'),
  quitAndInstall: () => ipcRenderer.invoke('quit-and-install'),
  onUpdateStatus: (callback) => ipcRenderer.on('update-status', (event, status, info) => callback(status, info)),
  // Expose a small app control API so the renderer can request a graceful shutdown.
  appControl: {
    close: () => ipcRenderer.invoke('app:close'),
    closeWindow: () => ipcRenderer.invoke('app:close-window'),
    closeCurrentWindow: () => ipcRenderer.invoke('app:close-current-window'),
    openNewWindow: (url, options) => ipcRenderer.invoke('app:open-new-window', url, options),
    closeWindowById: (id) => ipcRenderer.invoke('app:close-window-id', id)
  }
});

// Also expose a simple global function name for easier Blazor IJS calls
contextBridge.exposeInMainWorld('closeApp', () => {
  console.log('preload: closeApp invoked');
  return ipcRenderer.invoke('app:close');
});

contextBridge.exposeInMainWorld('closeAllWindows', () => {
  console.log('preload: closeAllWindows invoked');
  return ipcRenderer.invoke('app:close-window');
});

