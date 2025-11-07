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
        }
    }, [client]);

    const loadInstances = async () => {
        if (!client) return;
        setLoading(true);
        setError(null);
        try {
            const list = await client.listInstances();
            useAppStore.getState().setInstances(list.instances);
        } catch (error) {
            console.error('Failed to load instances:', error);
            setError('繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ縺ｮ隱ｭ縺ｿ霎ｼ縺ｿ縺ｫ螟ｱ謨励＠縺ｾ縺励◆');
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
            alert('繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ繧剃ｽ懈・縺励∪縺励◆: ' + instance.instance_id);
        } catch (error) {
            console.error('Failed to create instance:', error);
            setError('繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ縺ｮ菴懈・縺ｫ螟ｱ謨励＠縺ｾ縺励◆');
        }
    };

    const handleJoinInstance = async (instanceId: string) => {
        if (!client) return;
        setError(null);
        try {
            await client.joinInstance(instanceId);
            const instanceDetails = await client.getInstance(instanceId);
            setCurrentInstance(instanceDetails);
            await loadInstances();
            alert('繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ縺ｫ蜿ょ刈縺励∪縺励◆');
        } catch (error) {
            console.error('Failed to join instance:', error);
            setError('繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ縺ｸ縺ｮ蜿ょ刈縺ｫ螟ｱ謨励＠縺ｾ縺励◆');
        }
    };

    const handleLeaveInstance = async (instanceId: string) => {
        if (!client) return;
        setError(null);
        try {
            await client.leaveInstance(instanceId);
            setCurrentInstance(null);
            await loadInstances();
            alert('繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ縺九ｉ髮｢閼ｱ縺励∪縺励◆');
        } catch (error) {
            console.error('Failed to leave instance:', error);
            setError('繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ縺九ｉ縺ｮ髮｢閼ｱ縺ｫ螟ｱ謨励＠縺ｾ縺励◆');
        }
    };

    const handleUpdateInstance = async (instanceId: string) => {
        if (!client) return;
        const newMaxPlayers = prompt('譁ｰ縺励＞譛螟ｧ繝励Ξ繧､繝､繝ｼ謨ｰ繧貞・蜉帙＠縺ｦ縺上□縺輔＞:', '6');
        if (!newMaxPlayers) return;

        setError(null);
        try {
            await client.updateInstance(instanceId, {
                max_players: parseInt(newMaxPlayers, 10),
            });
            await loadInstances();
            alert('繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ繧呈峩譁ｰ縺励∪縺励◆');
        } catch (error) {
            console.error('Failed to update instance:', error);
            setError('繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ縺ｮ譖ｴ譁ｰ縺ｫ螟ｱ謨励＠縺ｾ縺励◆');
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
            alert('繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ繧貞炎髯､縺励∪縺励◆');
        } catch (error) {
            console.error('Failed to delete instance:', error);
            setError('繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ縺ｮ蜑企勁縺ｫ螟ｱ謨励＠縺ｾ縺励◆');
        }
    };

    if (loading && instances.length === 0) {
        return <div className="loading">繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ繧定ｪｭ縺ｿ霎ｼ繧薙〒縺・∪縺・..</div>;
    }

    return (
        <div className="instance-list">
            <div className="instance-list-header">
                <h2>繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ荳隕ｧ</h2>
                <button onClick={handleCreateInstance} className="btn-create" disabled={loading}>
                    譁ｰ隕丈ｽ懈・
                </button>
            </div>

            {error && (
                <div className="error-message">
                    {error}
                    <button onClick={() => setError(null)} className="btn-dismiss">閉じる</button>
                </div>
            )}

            <div className="instance-grid">
                {instances.length === 0 ? (
                    <p>繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ縺後≠繧翫∪縺帙ｓ</p>
                ) : (
                    instances.map((instance: any) => (
                        <div key={instance.instance_id} className="instance-card">
                            <h3>繧､繝ｳ繧ｹ繧ｿ繝ｳ繧ｹ {instance.instance_id.substring(0, 8)}</h3>
                            <div className="instance-info">
                                <p>繝励Ξ繧､繝､繝ｼ: {(instance.member_count ?? instance.members?.length ?? 0)}/{instance.max_players}</p>
                                <p>繧ｹ繝・・繧ｿ繧ｹ: {(instance.status || 'ACTIVE')}</p>
                                <p>菴懈・譌･譎・ {new Date(instance.created_at).toLocaleString()}</p>
                            </div>
                            <div className="instance-actions">
                                {currentInstance?.instance_id === instance.instance_id ? (
                                    <>
                                        <button
                                            onClick={() => handleLeaveInstance(instance.instance_id)}
                                            className="btn-leave"
                                        >
                                            髮｢閼ｱ
                                        </button>
                                        <button
                                            onClick={() => handleUpdateInstance(instance.instance_id)}
                                            className="btn-update"
                                        >
                                            邱ｨ髮・
                                        </button>
                                        <button
                                            onClick={() => handleDeleteInstance(instance.instance_id)}
                                            className="btn-delete"
                                        >
                                            蜑企勁
                                        </button>
                                    </>
                                ) : (
                                    <button
                                        onClick={() => handleJoinInstance(instance.instance_id)}
                                        className="btn-join"
                                        disabled={(instance.member_count ?? instance.members?.length ?? 0) >= instance.max_players}
                                    >
                                        蜿ょ刈
                                    </button>
                                )}
                            </div>
                        </div>
                    ))
                )}
            </div>
        </div>
    );
};



