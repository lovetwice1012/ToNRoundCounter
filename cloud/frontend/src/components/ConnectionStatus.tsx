/**
 * Connection Status Component
 */

import React, { useEffect, useState } from 'react';
import { useAppStore } from '../store/appStore';

export const ConnectionStatus: React.FC = () => {
    const { connectionState, playerId, restClient } = useAppStore();
    const [apiConnected, setApiConnected] = useState<boolean>(false);

    useEffect(() => {
        // REST APIの接続状態をチェック
        const checkApiConnection = async () => {
            if (restClient) {
                try {
                    await restClient.healthCheck();
                    setApiConnected(true);
                } catch (error) {
                    setApiConnected(false);
                }
            } else {
                setApiConnected(false);
            }
        };

        checkApiConnection();
        const interval = setInterval(checkApiConnection, 10000); // 10秒ごとにチェック

        return () => clearInterval(interval);
    }, [restClient]);

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
        <div style={{ display: 'flex', alignItems: 'center', gap: '20px' }}>
            {/* WebSocket接続状態 */}
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <div
                    style={{
                        width: '10px',
                        height: '10px',
                        borderRadius: '50%',
                        backgroundColor: getStatusColor(wsConnected),
                        boxShadow: `0 0 8px ${getStatusColor(wsConnected)}`,
                    }}
                />
                <span style={{ color: '#666', fontSize: '14px', fontWeight: 500 }}>
                    {getWebSocketStatusText()}
                </span>
            </div>

            {/* バックエンドAPI接続状態 */}
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <div
                    style={{
                        width: '10px',
                        height: '10px',
                        borderRadius: '50%',
                        backgroundColor: getStatusColor(apiConnected),
                        boxShadow: `0 0 8px ${getStatusColor(apiConnected)}`,
                    }}
                />
                <span style={{ color: '#666', fontSize: '14px', fontWeight: 500 }}>
                    {apiConnected ? 'API接続中' : 'API未接続'}
                </span>
            </div>

            {playerId && (
                <span style={{ 
                    color: '#333', 
                    fontSize: '14px',
                    padding: '4px 12px',
                    backgroundColor: '#f0f0f0',
                    borderRadius: '12px',
                    fontWeight: 500
                }}>
                    {playerId}
                </span>
            )}
        </div>
    );
};
