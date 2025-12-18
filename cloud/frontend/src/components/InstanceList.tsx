/**
 * Instance List Component
 */

import React, { useEffect, useState } from 'react';
import { useAppStore } from '../store/appStore';

export const InstanceList: React.FC = () => {
    const { client, instances, currentInstance, setCurrentInstance } = useAppStore();
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (client) {
            loadInstances();
            
            // インスタンス更新イベントの購読
            const unsubscribeUpdated = client.onInstanceUpdated((data) => {
                console.log('Instance updated:', data);
                loadInstances();
            });
            
            // インスタンス削除イベントの購読
            const unsubscribeDeleted = client.onInstanceDeleted((data) => {
                console.log('Instance deleted:', data);
                loadInstances();
            });
            
            return () => {
                unsubscribeUpdated();
                unsubscribeDeleted();
            };
        }
    }, [client]);

    const loadInstances = async () => {
        if (!client) return;
        setLoading(true);
        setError(null);
        try {
            const list = await client.listInstances();
            // 配列であることを保証
            useAppStore.getState().setInstances(Array.isArray(list) ? list : []);
        } catch (error) {
            console.error('Failed to load instances:', error);
            setError('インスタンスの読み込みに失敗しました');
            // エラー時は空配列をセット
            useAppStore.getState().setInstances([]);
        } finally {
            setLoading(false);
        }
    };

    const handleCreateInstance = async () => {
        if (!client) return;
        setError(null);
        try {
            const instance = await client.createInstance(6);
            await loadInstances();
            alert('インスタンスを作成しました: ' + instance.instance_id);
        } catch (error) {
            console.error('Failed to create instance:', error);
            setError('インスタンスの作成に失敗しました');
        }
    };

    const handleJoinInstance = async (instanceId: string) => {
        if (!client) return;
        setError(null);
        try {
            await client.joinInstance(instanceId);
            const instances = await client.listInstances();
            const joined = instances.find((i: any) => i.instance_id === instanceId);
            setCurrentInstance(joined);
            alert('インスタンスに参加しました');
        } catch (error) {
            console.error('Failed to join instance:', error);
            setError('インスタンスへの参加に失敗しました');
        }
    };

    const handleLeaveInstance = async (instanceId: string) => {
        if (!client) return;
        setError(null);
        try {
            await client.leaveInstance(instanceId);
            setCurrentInstance(null);
            await loadInstances();
            alert('インスタンスから離脱しました');
        } catch (error) {
            console.error('Failed to leave instance:', error);
            setError('インスタンスからの離脱に失敗しました');
        }
    };

    const handleUpdateInstance = async (instanceId: string) => {
        if (!client) return;
        const newMaxPlayers = prompt('新しい最大プレイヤー数を入力してください:', '6');
        if (!newMaxPlayers) return;

        setError(null);
        try {
            await client.updateInstance(instanceId, {
                max_players: parseInt(newMaxPlayers, 10),
            });
            await loadInstances();
            alert('インスタンスを更新しました');
        } catch (error) {
            console.error('Failed to update instance:', error);
            setError('インスタンスの更新に失敗しました');
        }
    };

    const handleDeleteInstance = async (instanceId: string) => {
        if (!client) return;
        if (!confirm('本当にこのインスタンスを削除しますか？')) return;

        setError(null);
        try {
            await client.deleteInstance(instanceId);
            if (currentInstance?.instance_id === instanceId) {
                setCurrentInstance(null);
            }
            await loadInstances();
            alert('インスタンスを削除しました');
        } catch (error) {
            console.error('Failed to delete instance:', error);
            setError('インスタンスの削除に失敗しました');
        }
    };

    if (loading && instances.length === 0) {
        return <div className="loading">インスタンスを読み込んでいます...</div>;
    }

    return (
        <div className="instance-list">
            <div className="instance-list-header">
                <h2>インスタンス一覧</h2>
                {/* VRChat does not allow remote instance creation - disabled for UI */}
                {/* <button onClick={handleCreateInstance} className="btn-create" disabled={loading}>
                    新規作成
                </button> */}
            </div>

            {error && (
                <div className="error-message">
                    {error}
                    <button onClick={() => setError(null)} className="btn-dismiss">×</button>
                </div>
            )}

            <div className="instance-grid">
                {!Array.isArray(instances) || instances.length === 0 ? (
                    <p>インスタンスがありません</p>
                ) : (
                    instances.map((instance: any) => (
                        <div key={instance.instance_id} className="instance-card">
                            <h3>インスタンス {instance.instance_id.substring(0, 20)}...</h3>
                            <div className="instance-info">
                                <p>プレイヤー: {instance.member_count}/{instance.max_players}</p>
                                <p>作成日時: {new Date(instance.created_at).toLocaleString()}</p>
                            </div>
                            {/* Instance join/leave/update/delete disabled for web UI */}
                            {/* VRChat instances are controlled by the desktop app */}
                            {/* <div className="instance-actions">
                                {currentInstance?.instance_id === instance.instance_id ? (
                                    <>
                                        <button
                                            onClick={() => handleLeaveInstance(instance.instance_id)}
                                            className="btn-leave"
                                        >
                                            離脱
                                        </button>
                                        <button
                                            onClick={() => handleUpdateInstance(instance.instance_id)}
                                            className="btn-update"
                                        >
                                            編集
                                        </button>
                                        <button
                                            onClick={() => handleDeleteInstance(instance.instance_id)}
                                            className="btn-delete"
                                        >
                                            削除
                                        </button>
                                    </>
                                ) : (
                                    <button
                                        onClick={() => handleJoinInstance(instance.instance_id)}
                                        className="btn-join"
                                        disabled={instance.member_count >= instance.max_players}
                                    >
                                        参加
                                    </button>
                                )}
                            </div> */}
                        </div>
                    ))
                )}
            </div>
        </div>
    );
};
