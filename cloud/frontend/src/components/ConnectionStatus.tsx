/**
 * Connection Status Component
 */

import React from 'react';
import { useAppStore } from '../store/appStore';

export const ConnectionStatus: React.FC = () => {
    const { connectionState, playerId } = useAppStore();

    const getStatusColor = () => {
        switch (connectionState) {
            case 'connected':
                return '#4caf50';
            case 'reconnecting':
                return '#ff9800';
            case 'disconnected':
                return '#f44336';
            default:
                return '#999';
        }
    };

    const getStatusText = () => {
        switch (connectionState) {
            case 'connected':
                return '接続中';
            case 'reconnecting':
                return '再接続中...';
            case 'disconnected':
                return '未接続';
            default:
                return '不明';
        }
    };

    return (
        <div className="connection-status" style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <div
                className="status-indicator"
                style={{
                    width: '12px',
                    height: '12px',
                    borderRadius: '50%',
                    backgroundColor: getStatusColor(),
                }}
            />
            <span className="status-text">{getStatusText()}</span>
            {playerId && <span className="player-id">プレイヤー: {playerId}</span>}
        </div>
    );
};
