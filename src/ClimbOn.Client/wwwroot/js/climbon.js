// Browser access for the client: the page language (C62) and the language setting sent at registration (S104).
// No business rules (C7).
window.climbOn = {
    storageKey: 'climbon.language',
    browserLanguage: () => navigator.language,
    storedLanguage: () => {
        try { return localStorage.getItem(window.climbOn.storageKey); } catch { return null; }
    },
    storeLanguage: (tag) => {
        try { localStorage.setItem(window.climbOn.storageKey, tag); } catch { }
    },
    setPageLanguage: (tag) => { document.documentElement.lang = tag; }
};
