/**
 * Connection Status Component
 */

import React, { useEffect, useState } from 'react';
import { useAppStore } from '../store/appStore';

export const ConnectionStatus: React.FC = () => {
    const {
        connectionState,
        playerId,
        setApiLatencyMs,
        apiLatencyMs,
        wsLatencyMs,
        lastSyncAt,
    } = useAppStore();
    const [apiConnected, setApiConnected] = useState<boolean>(false);

    useEffect(() => {
        let cancelled = false;
        // REST APIの接続状態をチェック (always read latest restClient from store to avoid restart churn)
        const checkApiConnection = async () => {
            const current = useAppStore.getState().restClient;
            if (!current) {
                if (!cancelled) setApiConnected(false);
                setApiLatencyMs(null);
                return;
            }
            try {
                const startedAt = performance.now();
                await current.healthCheck();
                if (!cancelled) {
                    setApiConnected(true);
                    setApiLatencyMs(Math.round(performance.now() - startedAt));
                }
            } catch {
                if (!cancelled) setApiConnected(false);
                setApiLatencyMs(null);
            }
        };

        checkApiConnection();
        const interval = setInterval(checkApiConnection, 15000);

        return () => {
            cancelled = true;
            clearInterval(interval);
        };
    }, [setApiLatencyMs]);

    const getStatusColor = (connected: boolean) => {
        return connected ? '#4caf50' : '#f44336';
    };

    const getWebSocketStatusText = () => {
        switch (connectionState) {
            case 'connected':
                return 'WebSocket接続中';
            case 'reconnecting':
                return 'WebSocket再接続中...';
            case 'disconnected':
                return 'WebSocket未接続';
            default:
                return 'WebSocket不明';
        }
    };

    const wsConnected = connectionState === 'connected';

    return (
        <div className="connection-status-grid">
            <div className="status-chip">
                <span className="status-dot" style={{ backgroundColor: getStatusColor(wsConnected), boxShadow: `0 0 8px ${getStatusColor(wsConnected)}` }} />
                <div>
                    <p>WebSocket</p>
                    <strong>{getWebSocketStatusText()}</strong>
                </div>
            </div>

            <div className="status-chip">
                <span className="status-dot" style={{ backgroundColor: getStatusColor(apiConnected), boxShadow: `0 0 8px ${getStatusColor(apiConnected)}` }} />
                <div>
                    <p>REST API</p>
                    <strong>{apiConnected ? '接続中' : '未接続'}</strong>
                </div>
            </div>

            <div className="status-chip">
                <div>
                    <p>Latency</p>
                    <strong>
                        WS {wsLatencyMs ?? '-'}ms / API {apiLatencyMs ?? '-'}ms
                    </strong>
                </div>
            </div>

            {playerId && (
                <div className="status-chip">
                    <div>
                        <p>Player</p>
                        <strong>{playerId}</strong>
                    </div>
                </div>
            )}

            <div className="status-chip">
                <div>
                    <p>Last Sync</p>
                    <strong>{lastSyncAt ? new Date(lastSyncAt).toLocaleTimeString() : '-'}</strong>
                </div>
            </div>
        </div>
    );
};
