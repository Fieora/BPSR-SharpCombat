(function(){
  // A simple wrapper that uses the preload-exposed API (electronUpdater)
  window.electronUpdaterClient = {
    getVersion: function(){
      if (window.electronUpdater && window.electronUpdater.getVersion) return window.electronUpdater.getVersion();
      if (window.electronUpdaterClient && window.electronUpdaterClient._shimGetVersion) return window.electronUpdaterClient._shimGetVersion();
      return Promise.resolve(null);
    },
    checkForUpdates: function(){
      if (window.electronUpdater && window.electronUpdater.checkForUpdates) return window.electronUpdater.checkForUpdates();
      return Promise.resolve({ error: 'not-available' });
    },
    downloadUpdate: function(){
      if (window.electronUpdater && window.electronUpdater.downloadUpdate) return window.electronUpdater.downloadUpdate();
      return Promise.resolve({ error: 'not-available' });
    },
    installUpdate: function(){
      if (window.electronUpdater && window.electronUpdater.installUpdate) return window.electronUpdater.installUpdate();
      return Promise.resolve({ error: 'not-available' });
    },
    onProgress: function(cb){
      if (window.electronUpdater && window.electronUpdater.onProgress) return window.electronUpdater.onProgress(cb);
      // shim: listen to ipc messages if available
      if (window && window.addEventListener) {
        const h = (e) => { if (e && e.detail) cb(e.detail); };
        window.addEventListener('electron-updater-progress', h);
        return () => window.removeEventListener('electron-updater-progress', h);
      }
      return () => {};
    }
    ,
    // Register a DotNet callback for progress updates: dotNetRef.invokeMethodAsync(methodName, data)
    registerDotNetProgress: function(dotNetRef, methodName){
      try {
        if (!dotNetRef || !methodName) {
          console.warn('registerDotNetProgress called with null args');
          return null;
        }
        // DotNetObjectReference should expose invokeMethodAsync; ensure it exists before calling
        if (typeof dotNetRef.invokeMethodAsync !== 'function') {
          console.warn('dotNetRef does not expose invokeMethodAsync');
          return null;
        }
        const unsub = window.electronUpdaterClient.onProgress(function(data){
          try { dotNetRef.invokeMethodAsync(methodName, data); } catch (e) { console.warn('dotnet invoke failed', e); }
        });
        return unsub;
      } catch (ex) {
        console.warn('registerDotNetProgress error', ex);
        return null;
      }
    }
  };
})();
