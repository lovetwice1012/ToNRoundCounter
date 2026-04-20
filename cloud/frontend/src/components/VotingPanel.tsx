/**
 * Voting Panel Component
 */

import React, { useEffect, useState } from 'react';
import { useAppStore } from '../store/appStore';

export const VotingPanel: React.FC = () => {
    const { client, activeVoting, setActiveVoting, currentInstance, playerId, pushToast, touchSyncTime } = useAppStore();
    const [hasVoted, setHasVoted] = useState(false);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [terrorName, setTerrorName] = useState('');
    const [roundKey, setRoundKey] = useState('');

    useEffect(() => {
        if (!client) return;

        // Subscribe to voting events
        const unsubscribeStarted = client.onVotingStarted((data) => {
            if (!data || !data.campaign_id) {
                console.warn('Ignoring malformed voting.started event:', data);
                return;
            }
            setActiveVoting(data);
            setHasVoted(Boolean(data.my_vote));
            setError(null);
            touchSyncTime();
            pushToast({ type: 'info', message: `新しい投票が開始されました: ${data.terror_name}` });
        });

        const unsubscribeResolved = client.onVotingResolved((data) => {
            setActiveVoting(null);
            setHasVoted(false);
            setError(null);
            const decision = data?.final_decision || 'Cancel';
            touchSyncTime();
            pushToast({ type: 'success', message: `投票結果: ${decision}` });
        });

        return () => {
            unsubscribeStarted();
            unsubscribeResolved();
        };
    }, [client]);

    // Fetch any active voting whenever the instance changes (or this panel
    // mounts after a voting has already started). Without this, broadcasts
    // dispatched before the dashboard subscribed are lost forever.
    useEffect(() => {
        if (!client || !currentInstance?.instance_id) {
            return;
        }
        let cancelled = false;
        const instanceId = currentInstance.instance_id;
        client
            .getActiveVotingCampaign(instanceId)
            .then((campaign: any) => {
                if (cancelled) return;
                if (campaign && campaign.campaign_id) {
                    setActiveVoting(campaign);
                    setHasVoted(Boolean(campaign.my_vote));
                } else {
                    setActiveVoting(null);
                    setHasVoted(false);
                }
            })
            .catch((err: any) => {
                console.warn('Failed to fetch active voting:', err);
            });
        return () => {
            cancelled = true;
        };
    }, [client, currentInstance?.instance_id, setActiveVoting]);

    const handleVote = async (decision: 'Continue' | 'Skip') => {
        if (!client || !activeVoting || !playerId) return;

        setLoading(true);
        setError(null);
        try {
            await client.submitVote(activeVoting.campaign_id, playerId, decision);
            setHasVoted(true);
            setActiveVoting({ ...activeVoting, my_vote: decision });
            touchSyncTime();
            pushToast({ type: 'success', message: `${decision} に投票しました。` });
        } catch (error) {
            console.error('Failed to vote:', error);
            setError('投票に失敗しました');
            pushToast({ type: 'error', message: '投票に失敗しました。' });
        } finally {
            setLoading(false);
        }
    };

    const handleStartVoting = async () => {
        if (!client || !currentInstance) return;
        const trimmedTerrorName = terrorName.trim();
        const trimmedRoundKey = roundKey.trim();
        if (!trimmedTerrorName) {
            setError('テラー名を入力してください');
            return;
        }

        setLoading(true);
        setError(null);
        try {
            const expiresAt = new Date(Date.now() + 60000); // 60秒後
            await client.startVoting(currentInstance.instance_id, trimmedTerrorName, expiresAt, trimmedRoundKey || undefined);
            setTerrorName('');
            setRoundKey('');
            touchSyncTime();
            pushToast({ type: 'success', message: '投票を開始しました。' });
        } catch (error) {
            console.error('Failed to start voting:', error);
            setError('投票の開始に失敗しました');
            pushToast({ type: 'error', message: '投票の開始に失敗しました。' });
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="voting-panel">
            <h2>統率自動自殺投票</h2>

            {error && (
                <div className="error-message">
                    {error}
                    <button onClick={() => setError(null)} className="btn-dismiss">×</button>
                </div>
            )}

            {!activeVoting ? (
                <div className="no-voting">
                    <p>現在、進行中の投票はありません</p>
                    {currentInstance && (
                        <div className="add-form">
                            <input
                                value={terrorName}
                                onChange={(event) => setTerrorName(event.target.value)}
                                className="input-search"
                                placeholder="テラー名"
                            />
                            <input
                                value={roundKey}
                                onChange={(event) => setRoundKey(event.target.value)}
                                className="input-round-key"
                                placeholder="ラウンド名 (任意)"
                            />
                            <button 
                                onClick={handleStartVoting} 
                                className="btn-start-voting"
                                disabled={loading}
                                type="button"
                            >
                                {loading ? '開始中...' : '投票を開始'}
                            </button>
                        </div>
                    )}
                </div>
            ) : (
                <div className="active-voting">
                    <div className="voting-info">
                        <h3>テラー: {activeVoting.terror_name}</h3>
                        <p>対象ラウンド: {activeVoting.round_key || '現在のラウンド'}</p>
                        <p>期限: {new Date(activeVoting.expires_at).toLocaleTimeString()}</p>
                        <p>ステータス: {activeVoting.status}</p>
                        <p>票数: 続行 {activeVoting.continue_count ?? activeVoting.proceed_count ?? 0} / スキップ {activeVoting.skip_count ?? activeVoting.cancel_count ?? 0}</p>
                    </div>

                    {!hasVoted ? (
                        <div className="voting-buttons">
                            <button
                                onClick={() => handleVote('Continue')}
                                className="btn-vote btn-proceed"
                                disabled={loading}
                                type="button"
                            >
                                {loading ? '送信中...' : '続行 (Continue)'}
                            </button>
                            <button
                                onClick={() => handleVote('Skip')}
                                className="btn-vote btn-cancel"
                                disabled={loading}
                                type="button"
                            >
                                {loading ? '送信中...' : 'スキップ (Skip)'}
                            </button>
                        </div>
                    ) : (
                        <p className="voted-message">投票済み - 結果を待っています...</p>
                    )}
                </div>
            )}
        </div>
    );
};
