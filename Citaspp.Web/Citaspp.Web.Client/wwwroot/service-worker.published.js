self.importScripts("./service-worker-assets.js");
self.addEventListener("install", event => event.waitUntil(onInstall()));
self.addEventListener("activate", event => event.waitUntil(onActivate()));
self.addEventListener("fetch", event => {
    if (event.request.method === "GET" && new URL(event.request.url).origin === self.location.origin) {
        event.respondWith(onFetch(event));
    }
});

const cacheNamePrefix = "citaspp-offline-";
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const includedAssets = [
    /\.dll$/,
    /\.wasm$/,
    /\.html$/,
    /\.js$/,
    /\.json$/,
    /\.webmanifest$/,
    /\.css$/,
    /\.woff2?$/,
    /\.png$/,
    /\.svg$/,
    /\.ico$/,
    /\.dat$/
];
const baseUrl = new URL(self.registration.scope);

async function onInstall() {
    const cache = await caches.open(cacheName);
    const assets = self.assetsManifest.assets
        .filter(asset => includedAssets.some(pattern => pattern.test(asset.url)))
        .filter(asset => !/^service-worker\.js$/.test(asset.url))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: "no-cache" }));
    await cache.addAll(assets);
    await cache.add(new Request(baseUrl.href, { cache: "no-cache" }));
    await self.skipWaiting();
}

async function onActivate() {
    const keys = await caches.keys();
    await Promise.all(keys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
    await self.clients.claim();
}

async function onFetch(event) {
    const cache = await caches.open(cacheName);
    const cached = await cache.match(event.request);
    if (cached) {
        return cached;
    }

    try {
        const response = await fetch(event.request);
        if (response.ok && event.request.mode === "navigate") {
            await cache.put(event.request, response.clone());
        }
        return response;
    } catch (error) {
        if (event.request.mode === "navigate") {
            const appShell = await cache.match(baseUrl.href);
            if (appShell) {
                return appShell;
            }
        }
        throw error;
    }
}
