/**
 * Player States Component
 */

import React, { useEffect, useState, useRef } from 'react';
import { useAppStore } from '../store/appStore';

export const PlayerStates: React.FC = () => {
    const { client, playerStates, currentInstance } = useAppStore();
    const [error, setError] = useState<string | null>(null);
    const [loading, setLoading] = useState(false);
    const loadedInstanceRef = useRef<string | null>(null);

    useEffect(() => {
        if (!client || !currentInstance) {
            setLoading(false);
            return;
        }

        // 既に読み込み済みのインスタンスなら再読み込みしない
        if (loadedInstanceRef.current === currentInstance.instance_id) {
            return;
        }

        setError(null);
        setLoading(true);
        loadedInstanceRef.current = currentInstance.instance_id;

        // 初回データ取得
        const loadPlayerStates = async () => {
            try {
                const states = await client.getAllPlayerStates(currentInstance.instance_id);
                console.log('Loaded player states:', states);
                
                // すべてのプレイヤー状態をストアに保存
                states.forEach((state: any) => {
                    useAppStore.getState().updatePlayerState(state.player_id, state);
                });
            } catch (err) {
                console.error('Failed to load player states:', err);
                setError('プレイヤー状態の取得に失敗しました');
            } finally {
                setLoading(false);
            }
        };

        loadPlayerStates();

        // Subscribe to player state updates
        const unsubscribe = client.onPlayerStateUpdate((data) => {
            try {
                console.log('Player state updated:', data);
                // プレイヤー状態を更新（変更があった部分のみ再レンダリングされる）
                useAppStore.getState().updatePlayerState(data.player_state.player_id, data.player_state);
            } catch (err) {
                console.error('Failed to update player state:', err);
                setError('プレイヤー状態の更新に失敗しました');
            }
        });

        return () => unsubscribe();
    }, [client, currentInstance?.instance_id]); // currentInstance全体ではなくinstance_idのみ監視

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
            ) : loading ? (
                <p>読み込み中...</p>
            ) : playerStatesArray.length === 0 ? (
                <p>プレイヤー情報がありません</p>
            ) : (
                <div className="player-states-grid">
                    {playerStatesArray.map(([playerId, state]) => (
                        <PlayerStateCard key={playerId} playerId={playerId} state={state} />
                    ))}
                </div>
            )}
        </div>
    );
};

// メモ化されたプレイヤーカードコンポーネント - 状態が変わった時のみ再レンダリング
const PlayerStateCard = React.memo<{ playerId: string; state: any }>(({ playerId, state }) => {
    // プレイヤー名の隣に表示するアイテム情報
    const playerName = state.player_name || playerId;
    const hasItem = state.items && state.items.length > 0;
    const itemName = hasItem ? state.items[0] : '';
    const itemDisplay = hasItem ? `(${itemName})` : '';
    
    return (
        <div className="player-state-card">
            <h3>{playerName}{itemDisplay}</h3>
            <div className="player-state-info">
                {state.velocity !== undefined && (
                    <p>
                        速度: {state.velocity.toFixed(2)} m/s
                        {state.afk_duration !== undefined && state.afk_duration >= 3 && (
                            <span style={{ marginLeft: '8px', color: '#ff9800' }}>
                                (AFK: {Math.floor(state.afk_duration)}秒)
                            </span>
                        )}
                    </p>
                )}
                {state.damage !== undefined && (
                    <p>ダメージ: {state.damage}</p>
                )}
                {/* アイテム表示を追加 */}
                <p>アイテム: {hasItem ? itemName : 'なし'}</p>
                {state.is_alive !== undefined && (
                    <p>生存: {state.is_alive ? '✓' : '✗'}</p>
                )}
                <p className="timestamp">更新: {state.timestamp ? new Date(state.timestamp).toLocaleTimeString() : '不明'}</p>
            </div>
        </div>
    );
}, (prevProps, nextProps) => {
    // 状態が変わっていない場合は再レンダリングしない
    return JSON.stringify(prevProps.state) === JSON.stringify(nextProps.state);
});
