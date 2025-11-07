/**
 * Voting Panel Component
 */

import React, { useEffect, useState } from 'react';
import { useAppStore } from '../store/appStore';

export const VotingPanel: React.FC = () => {
    const { client, activeVoting, setActiveVoting, currentInstance, playerId } = useAppStore();
    const [hasVoted, setHasVoted] = useState(false);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (!client) return;

        // Subscribe to voting events
        const unsubscribeStarted = client.onVotingStarted((data) => {
            setActiveVoting(data);
            setHasVoted(false);
            setError(null);
        });

        const unsubscribeResolved = client.onVotingResolved((data) => {
            setActiveVoting(null);
            setHasVoted(false);
            setError(null);
            alert(`投票結果: ${data.final_decision}`);
        });

        return () => {
            unsubscribeStarted();
            unsubscribeResolved();
        };
    }, [client]);

    const handleVote = async (decision: 'Proceed' | 'Cancel') => {
        if (!client || !activeVoting || !playerId) return;

        setLoading(true);
        setError(null);
        try {
            await client.submitVote(activeVoting.campaign_id, playerId, decision);
            setHasVoted(true);
        } catch (error) {
            console.error('Failed to vote:', error);
            setError('投票に失敗しました');
        } finally {
            setLoading(false);
        }
    };

    const handleStartVoting = async () => {
        if (!client || !currentInstance) return;

        const terrorName = prompt('テロール名を入力してください:');
        if (!terrorName) return;

        setLoading(true);
        setError(null);
        try {
            const expiresAt = new Date(Date.now() + 60000); // 60秒後
            await client.startVoting(currentInstance.instance_id, terrorName, expiresAt);
        } catch (error) {
            console.error('Failed to start voting:', error);
            setError('投票の開始に失敗しました');
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
                        <button 
                            onClick={handleStartVoting} 
                            className="btn-start-voting"
                            disabled={loading}
                        >
                            {loading ? '開始中...' : '投票を開始'}
                        </button>
                    )}
                </div>
            ) : (
                <div className="active-voting">
                    <div className="voting-info">
                        <h3>テロール: {activeVoting.terror_name}</h3>
                        <p>期限: {new Date(activeVoting.expires_at).toLocaleTimeString()}</p>
                        <p>ステータス: {activeVoting.status}</p>
                    </div>

                    {!hasVoted ? (
                        <div className="voting-buttons">
                            <button
                                onClick={() => handleVote('Proceed')}
                                className="btn-vote btn-proceed"
                                disabled={loading}
                            >
                                {loading ? '送信中...' : '実行する (Proceed)'}
                            </button>
                            <button
                                onClick={() => handleVote('Cancel')}
                                className="btn-vote btn-cancel"
                                disabled={loading}
                            >
                                {loading ? '送信中...' : 'キャンセル (Cancel)'}
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
