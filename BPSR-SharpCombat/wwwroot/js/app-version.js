window.getAppVersion = function () {
    if (window.electron && window.electron.getAppVersionSync) {
        const version = window.electron.getAppVersionSync();
        console.log('[app-version] sync version:', version);
        return version;
    }
    console.warn('[app-version] electron API not available');
    return null;
};
