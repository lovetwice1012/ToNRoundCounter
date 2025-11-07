/**
 * Player States Component
 */

import React, { useEffect, useState } from 'react';
import { useAppStore } from '../store/appStore';

export const PlayerStates: React.FC = () => {
    const { client, playerStates, currentInstance } = useAppStore();
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (!client || !currentInstance) return;

        setError(null);

        // Subscribe to player state updates
        const unsubscribe = client.onPlayerStateUpdate((data) => {
            try {
                useAppStore.getState().updatePlayerState(data.player_id, data);
            } catch (err) {
                console.error('Failed to update player state:', err);
                setError('プレイヤー状態の更新に失敗しました');
            }
        });

        return () => unsubscribe();
    }, [client, currentInstance]);

    const playerStatesArray = Array.from(playerStates.entries());

    return (
        <div className="player-states">
            <h2>プレイヤー状態</h2>
            
            {error && (
                <div className="error-message">
                    {error}
                    <button onClick={() => setError(null)} className="btn-dismiss">×</button>
                </div>
            )}

            {!currentInstance ? (
                <p>インスタンスに参加していません</p>
            ) : playerStatesArray.length === 0 ? (
                <p>プレイヤー情報がありません</p>
            ) : (
                <div className="player-states-grid">
                    {playerStatesArray.map(([playerId, state]) => (
                        <div key={playerId} className="player-state-card">
                            <h3>{playerId}</h3>
                            <div className="player-state-info">
                                <p>状態: {state.state}</p>
                                {state.health !== undefined && (
                                    <p>体力: {state.health}</p>
                                )}
                                {state.position && (
                                    <p>
                                        位置: ({state.position.x.toFixed(1)}, {state.position.y.toFixed(1)}, {state.position.z.toFixed(1)})
                                    </p>
                                )}
                                <p>更新: {new Date(state.timestamp).toLocaleTimeString()}</p>
                            </div>
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
};
