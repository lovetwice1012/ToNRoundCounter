/**
 * Login Page Component
 */

import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAppStore } from '../store/appStore';
import { ToNRoundCloudClient } from '../lib/websocket-client';
import { createRestClient } from '../lib/rest-client';

export const Login: React.FC = () => {
    const navigate = useNavigate();
    const { setClient, setRestClient, setSession, setConnected, setConnectionState, sessionToken, playerId } = useAppStore();
    const [serverUrl, setServerUrl] = useState('ws://localhost:3000/ws');
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [autoReconnecting, setAutoReconnecting] = useState(false);
    const [waitingForToken, setWaitingForToken] = useState(false);

    // ワンタイムトークンでのログインをチェック
    useEffect(() => {
        const params = new URLSearchParams(window.location.search);
        const token = params.get('token');
        
        if (token) {
            setWaitingForToken(false);
            loginWithOneTimeToken(token);
            return;
        }

        // 既存のセッションで自動再接続を試みる
        if (sessionToken && playerId && !autoReconnecting) {
            setAutoReconnecting(true);
            reconnectWithExistingSession();
            return;
        }

        // トークンもセッションもない場合は待機状態
        setWaitingForToken(true);
    }, []);

    const loginWithOneTimeToken = async (token: string) => {
        try {
            setLoading(true);
            setError(null);

            // Create REST client
            const apiUrl = (import.meta as any).env?.VITE_API_URL || 'http://localhost:8080';
            const restClient = createRestClient(apiUrl);
            setRestClient(restClient);

            // Create WebSocket client
            const client = new ToNRoundCloudClient(serverUrl);
            
            // Connection state callback
            client.onConnectionStateChange((state) => {
                setConnectionState(state);
                setConnected(state === 'connected');
            });

            // Connect
            await client.connect();
            
            // Login with one-time token
            const session = await client.loginWithOneTimeToken(token, '1.0.0');
            
            // Update REST client with session token
            restClient.setSessionToken(session.session_token);
            
            // Save to store
            setClient(client);
            setSession(session.session_token, session.player_id);
            
            // Clear URL parameters
            window.history.replaceState({}, document.title, '/login');
            
            // Navigate to dashboard
            navigate('/dashboard');
        } catch (err: any) {
            console.error('One-time token login failed:', err);
            setError(err.message || 'ワンタイムトークンログインに失敗しました');
        } finally {
            setLoading(false);
        }
    };

    // 既存のセッションで自動再接続を試みる
    useEffect(() => {
        if (sessionToken && playerId && !autoReconnecting) {
            setAutoReconnecting(true);
            reconnectWithExistingSession();
        }
    }, []);

    const reconnectWithExistingSession = async () => {
        try {
            setLoading(true);
            setError(null);

            // Create REST client
            const apiUrl = (import.meta as any).env?.VITE_API_URL || 'http://localhost:8080';
            const restClient = createRestClient(apiUrl, sessionToken || undefined);
            setRestClient(restClient);

            // Create WebSocket client
            const client = new ToNRoundCloudClient(serverUrl);
            
            // セッショントークンを設定
            if (sessionToken && playerId) {
                client.setSessionToken(sessionToken, playerId);
            }
            
            // Connection state callback
            client.onConnectionStateChange((state) => {
                setConnectionState(state);
                setConnected(state === 'connected');
            });

            // Connect
            await client.connect();
            
            // セッショントークンが有効かチェック (ここでは単純にクライアントを保存)
            setClient(client);
            
            // Navigate to dashboard
            navigate('/dashboard');
        } catch (err: any) {
            console.error('Auto-reconnect failed:', err);
            // 失敗した場合はログイン画面を表示
            setAutoReconnecting(false);
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="login-page">
            <div className="login-container">
                <h1>ToNRoundCounter Cloud</h1>
                <p className="subtitle">クラウドダッシュボード</p>

                {waitingForToken && !loading && !error && (
                    <div className="waiting-for-login">
                        <div className="waiting-icon">
                            <svg width="64" height="64" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                                <circle cx="12" cy="12" r="10" stroke="#667eea" strokeWidth="2" opacity="0.2"/>
                                <path d="M12 2 A10 10 0 0 1 22 12" stroke="#667eea" strokeWidth="2" strokeLinecap="round">
                                    <animateTransform
                                        attributeName="transform"
                                        type="rotate"
                                        from="0 12 12"
                                        to="360 12 12"
                                        dur="1s"
                                        repeatCount="indefinite"
                                    />
                                </path>
                            </svg>
                        </div>
                        <h2>アプリからログインしてください</h2>
                        <p className="instruction">
                            ToNRoundCounterアプリを起動し、<br/>
                            クラウド設定からダッシュボードにログインしてください。
                        </p>
                        <div className="instruction-steps">
                            <ol>
                                <li>ToNRoundCounterアプリを起動</li>
                                <li>設定画面を開く</li>
                                <li>「クラウドダッシュボードを開く」をクリック</li>
                            </ol>
                        </div>
                    </div>
                )}

                {(loading || autoReconnecting) && (
                    <div className="loading-state">
                        <div className="spinner"></div>
                        <p>{autoReconnecting ? '自動再接続中...' : '接続中...'}</p>
                    </div>
                )}

                {error && (
                    <div className="error-message">
                        {error}
                        <button onClick={() => setError(null)} className="btn-dismiss">×</button>
                    </div>
                )}

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
