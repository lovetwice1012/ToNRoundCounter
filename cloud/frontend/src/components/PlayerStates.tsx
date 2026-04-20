/**
 * Player States Component
 */

import React, { useEffect, useState, useRef } from 'react';
import { useAppStore } from '../store/appStore';

function normalizeItemList(state: any): string[] {
    if (Array.isArray(state?.items)) {
        const items = state.items.filter((s: any) => typeof s === 'string' && s.length > 0);
        if (items.length > 0) {
            return items;
        }
    }

    if (typeof state?.current_item === 'string' && state.current_item.length > 0 && state.current_item !== 'None') {
        return [state.current_item];
    }

    return [];
}

export const PlayerStates: React.FC = () => {
    const { client, playerStates, currentInstance, touchSyncTime } = useAppStore();
    const [error, setError] = useState<string | null>(null);
    const [loading, setLoading] = useState(false);
    const loadedInstanceRef = useRef<string | null>(null);
    // 現在のテラーに対する生存希望者 (player_id の集合)
    const [desirePlayerIds, setDesirePlayerIds] = useState<Set<string>>(new Set());
    const [currentTerrorName, setCurrentTerrorName] = useState<string | null>(null);

    useEffect(() => {
        if (!client || !currentInstance) {
            setLoading(false);
            return;
        }

        const instanceId = currentInstance.instance_id;
        const isFirstLoadForInstance = loadedInstanceRef.current !== instanceId;

        if (isFirstLoadForInstance) {
            setError(null);
            setLoading(true);
            loadedInstanceRef.current = instanceId;
        }

        // 初回データ取得 (and periodic refresh)
        const loadPlayerStates = async () => {
            try {
                const states = await client.getAllPlayerStates(instanceId);
                console.log('Loaded player states:', states);

                // すべてのプレイヤー状態をストアに保存
                states.forEach((state: any) => {
                    useAppStore.getState().updatePlayerState(state.player_id, state);
                });
                touchSyncTime();
                setError(null);
            } catch (err) {
                console.error('Failed to load player states:', err);
                setError('プレイヤー状態の取得に失敗しました');
            } finally {
                if (isFirstLoadForInstance) setLoading(false);
            }
        };

        loadPlayerStates();

        // Periodic fallback refresh in case live `player.state.updated`
        // broadcasts are missed (e.g. dashboard opened before the C# client
        // joined the instance, transient WS reconnects, etc.).
        const refreshInterval = setInterval(loadPlayerStates, 5000);

        // Subscribe to player state updates
        const unsubscribe = client.onPlayerStateUpdate((data) => {
            try {
                console.log('Player state updated:', data);
                const payload = data?.data ?? data;
                const ps = payload?.player_state ?? payload;
                if (!ps || !ps.player_id) {
                    console.warn('Skipping malformed player_state update:', data);
                    return;
                }
                useAppStore.getState().updatePlayerState(ps.player_id, ps);
                touchSyncTime();
            } catch (err) {
                console.error('Failed to update player state:', err);
                setError('プレイヤー状態の更新に失敗しました');
            }
        });

        return () => {
            clearInterval(refreshInterval);
            unsubscribe();
        };
    }, [client, currentInstance?.instance_id, touchSyncTime]); // currentInstance全体ではなくinstance_idのみ監視

    // 生存希望者 (threat.announced) を購読
    useEffect(() => {
        if (!client || !currentInstance) {
            setDesirePlayerIds(new Set());
            setCurrentTerrorName(null);
            return;
        }

        const unsubscribe = client.onThreatAnnounced((data) => {
            try {
                // backend は { stream, data: { instance_id, terror_name, round_key, desire_players }, timestamp } の data 部分を渡す
                const payload = data?.data ?? data;
                if (!payload || payload.instance_id !== currentInstance.instance_id) {
                    return;
                }
                const ids = new Set<string>(
                    Array.isArray(payload.desire_players)
                        ? payload.desire_players.map((p: any) => p?.player_id).filter(Boolean)
                        : []
                );
                setDesirePlayerIds(ids);
                if (typeof payload.terror_name === 'string' && payload.terror_name.length > 0) {
                    setCurrentTerrorName(payload.terror_name);
                }
            } catch (err) {
                console.error('Failed to handle threat.announced:', err);
            }
        });

        return () => {
            unsubscribe();
        };
    }, [client, currentInstance?.instance_id]);

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
                        <PlayerStateCard
                            key={playerId}
                            playerId={playerId}
                            state={state}
                            isDesirePlayer={desirePlayerIds.has(playerId)}
                            currentTerrorName={currentTerrorName}
                        />
                    ))}
                </div>
            )}
        </div>
    );
};

// メモ化されたプレイヤーカードコンポーネント - 状態が変わった時のみ再レンダリング
const PlayerStateCard = React.memo<{
    playerId: string;
    state: any;
    isDesirePlayer: boolean;
    currentTerrorName: string | null;
}>(({ playerId, state, isDesirePlayer, currentTerrorName }) => {
    // プレイヤー名の隣に表示するアイテム情報 (複数アイテム対応)
    const playerName = state.player_name || playerId;
    const itemList = normalizeItemList(state);
    const hasItem = itemList.length > 0;
    const itemDisplay = hasItem ? `(${itemList.join(', ')})` : '';

    return (
        <div
            className="player-state-card"
            style={isDesirePlayer ? { borderLeft: '4px solid #4caf50' } : undefined}
        >
            <h3>
                {playerName}{itemDisplay}
                {isDesirePlayer && (
                    <span
                        title={currentTerrorName ? `${currentTerrorName} の生存希望者` : '生存希望者'}
                        style={{ marginLeft: '8px', color: '#4caf50', fontSize: '0.85em' }}
                    >
                        💚 生存希望
                    </span>
                )}
            </h3>
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
                {/* アイテム表示 (全件) */}
                <p>アイテム: {hasItem ? itemList.join(', ') : 'なし'}</p>
                {state.is_alive !== undefined && (
                    <p>生存: {state.is_alive ? '✓' : '✗'}</p>
                )}
                <p className="timestamp">更新: {state.timestamp ? new Date(state.timestamp).toLocaleTimeString() : '不明'}</p>
            </div>
        </div>
    );
}, (prevProps, nextProps) => {
    const prev = prevProps.state || {};
    const next = nextProps.state || {};
    const prevItems = normalizeItemList(prev).join('|');
    const nextItems = normalizeItemList(next).join('|');
    return prevProps.playerId === nextProps.playerId
        && prevProps.isDesirePlayer === nextProps.isDesirePlayer
        && prevProps.currentTerrorName === nextProps.currentTerrorName
        && prev.timestamp === next.timestamp
        && prev.velocity === next.velocity
        && prev.afk_duration === next.afk_duration
        && prev.damage === next.damage
        && prev.is_alive === next.is_alive
        && prev.player_name === next.player_name
        && prevItems === nextItems;
});
