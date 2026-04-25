export function getApiBaseUrl(): string {
    const configured = (import.meta as any).env?.VITE_API_URL;
    if (typeof configured === 'string' && configured.trim()) {
        return configured.trim().replace(/\/$/, '');
    }

    return '';
}

export function getWebSocketUrl(): string {
    const configured = (import.meta as any).env?.VITE_WS_URL;
    if (typeof configured === 'string' && configured.trim()) {
        return configured.trim();
    }

    if (typeof window !== 'undefined' && window.location?.host) {
        const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
        return `${protocol}//${window.location.host}/ws`;
    }

    return 'wss://toncloud.sprink.cloud/ws';
}
