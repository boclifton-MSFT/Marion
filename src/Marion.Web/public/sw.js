// Empty service worker stub to prevent 404 errors
// This can be safely deleted if not using PWA features
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', () => self.clients.claim());
