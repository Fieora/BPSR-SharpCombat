// preload.js - empty for now, provided for secure contextIsolation usage
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('electron', {
  getWindowState: () => ipcRenderer.invoke('window-state:get'),
  setWindowState: (state) => ipcRenderer.invoke('window-state:set', state),
  // Expose a small app control API so the renderer can request a graceful shutdown.
  appControl: {
    close: () => ipcRenderer.invoke('app:close'),
    closeWindow: () => ipcRenderer.invoke('app:close-window'),
    closeCurrentWindow: () => ipcRenderer.invoke('app:close-current-window'),
    openNewWindow: (url, options) => ipcRenderer.invoke('app:open-new-window', url, options)
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
