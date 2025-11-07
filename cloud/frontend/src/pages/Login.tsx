/**
 * Login Page Component
 */

import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAppStore } from '../store/appStore';
import { ToNRoundCloudClient } from '../lib/websocket-client';

export const Login: React.FC = () => {
    const navigate = useNavigate();
    const { setClient, setSession, setConnected, setConnectionState } = useAppStore();
    const [playerId, setPlayerIdInput] = useState('');
    const [serverUrl, setServerUrl] = useState('ws://localhost:3000/ws');
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const handleLogin = async (e: React.FormEvent) => {
        e.preventDefault();
        
        if (!playerId.trim()) {
            setError('プレイヤーIDを入力してください');
            return;
        }

        setLoading(true);
        setError(null);

        try {
            // Create client
            const client = new ToNRoundCloudClient(serverUrl);
            
            // Connection state callback
            client.onConnectionStateChange((state) => {
                setConnectionState(state);
                setConnected(state === 'connected');
            });

            // Connect
            await client.connect();
            
            // Login
            const session = await client.login(playerId, '1.0.0');
            
            // Save to store
            setClient(client);
            setSession(session.session_token, session.player_id);
            
            // Navigate to dashboard
            navigate('/dashboard');
        } catch (err: any) {
            console.error('Login failed:', err);
            setError(err.message || 'ログインに失敗しました');
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="login-page">
            <div className="login-container">
                <h1>ToNRoundCounter Cloud</h1>
                <p className="subtitle">クラウドダッシュボードにログイン</p>

                <form onSubmit={handleLogin} className="login-form">
                    <div className="form-group">
                        <label htmlFor="server-url">サーバーURL</label>
                        <input
                            id="server-url"
                            type="text"
                            value={serverUrl}
                            onChange={(e) => setServerUrl(e.target.value)}
                            placeholder="ws://localhost:3000/ws"
                            disabled={loading}
                        />
                    </div>

                    <div className="form-group">
                        <label htmlFor="player-id">プレイヤーID</label>
                        <input
                            id="player-id"
                            type="text"
                            value={playerId}
                            onChange={(e) => setPlayerIdInput(e.target.value)}
                            placeholder="your_player_id"
                            disabled={loading}
                            autoFocus
                        />
                    </div>

                    {error && (
                        <div className="error-message">
                            {error}
                        </div>
                    )}

                    <button
                        type="submit"
                        className="btn-login"
                        disabled={loading}
                    >
                        {loading ? '接続中...' : 'ログイン'}
                    </button>
                </form>

                <div className="login-info">
                    <h3>機能一覧</h3>
                    <ul>
                        <li>✅ リアルタイムインスタンス管理</li>
                        <li>✅ プレイヤー状態同期</li>
                        <li>✅ 統率自動自殺投票システム</li>
                        <li>✅ 統計・分析ダッシュボード</li>
                        <li>✅ リモート設定同期</li>
                        <li>✅ ステータス監視</li>
                    </ul>
                </div>
            </div>
        </div>
    );
};
