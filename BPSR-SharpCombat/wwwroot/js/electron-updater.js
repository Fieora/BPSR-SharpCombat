(function () {
  window.registerUpdateHandler = function (dotNetRef) {
    if (window.electron && window.electron.onUpdateStatus) {
      window.electron.onUpdateStatus((status, info) => {
        // Pass status and info to Blazor
        // We wrap the call in a try-catch to avoid crashing if the component is disposed
        try {
          dotNetRef.invokeMethodAsync('OnUpdateStatus', status, info);
        } catch (e) {
          console.error("Error invoking OnUpdateStatus", e);
        }
      });
    }
  };
})();
