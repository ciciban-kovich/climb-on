// N21: the only offline behaviour is loading the cached client shell. API requests are never cached.
self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall()));
self.addEventListener('activate', event => event.waitUntil(onActivate()));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'shell-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const included = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.svg$/, /\.blat$/, /\.dat$/, /\.webmanifest$/];
const excluded = [/^service-worker\.js$/];
const base = '/';
const baseUrl = new URL(base, self.origin);
const manifestUrls = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall() {
    const requests = self.assetsManifest.assets
        .filter(asset => included.some(pattern => pattern.test(asset.url)))
        .filter(asset => !excluded.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(requests));
}

async function onActivate() {
    const keys = await caches.keys();
    await Promise.all(keys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    const request = event.request;
    if (request.method !== 'GET' || new URL(request.url).pathname.startsWith('/api/')) {
        return fetch(request);
    }

    // A navigation to a client route loads the shell; the client fetches everything else from the api.
    const shellRequest = request.mode === 'navigate' && !manifestUrls.some(url => url === request.url)
        ? 'index.html'
        : request;
    const cache = await caches.open(cacheName);
    return (await cache.match(shellRequest)) ?? fetch(request);
}
