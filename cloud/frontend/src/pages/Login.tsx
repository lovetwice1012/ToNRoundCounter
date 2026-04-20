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
    const {
        setClient,
        setRestClient,
        setSession,
        setConnected,
        setConnectionState,
        sessionToken,
        playerId,
        pushToast,
        touchSyncTime,
    } = useAppStore();
    const [serverUrl] = useState((import.meta as any).env?.VITE_WS_URL || 'ws://localhost:8080/ws');
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
            
            // BUG FIX #8 (MEDIUM): Capture and call event unsubscriber
            // Previously: onConnectionStateChange subscription was never unregistered; accumulated callbacks if function retried.
            // Now: Call returned unsubscriber so old callback is removed when client is stored.
            const unsubscribe = client.onConnectionStateChange((state) => {
                setConnectionState(state);
                setConnected(state === 'connected');
            });
            // Note: Unsubscriber called after client stored in setClient() to avoid stale closure.

            // Connect
            await client.connect();
            
            // Login with one-time token
            const session = await client.loginWithOneTimeToken(token, '1.0.0');
            
            // Update REST client with session token
            restClient.setSessionToken(session.session_token);
            
            // Save to store (after this, the connection state callback will be handled by Dashboard useEffect)
            setClient(client);
            // Clean up the temporary subscription now that client is in store
            unsubscribe();
            
            setSession(session.session_token, session.player_id, session.user_id);
            
            // Clear URL parameters
            window.history.replaceState({}, document.title, '/login');
            
            // Navigate to dashboard
            navigate('/dashboard');
        } catch (err: any) {
            console.error('One-time token login failed:', err);
            setError(err.message || 'ワンタイムトークンログインに失敗しました');
            pushToast({ type: 'error', message: 'ワンタイムトークン認証に失敗しました。' });
        } finally {
            setLoading(false);
        }
    };

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
            
            // BUG FIX #8 (MEDIUM): Capture and call event unsubscriber
            // Previously: onConnectionStateChange subscription was never unregistered; accumulated callbacks if reconnect retried.
            // Now: Call returned unsubscriber so old callback is removed when client is stored.
            const unsubscribe = client.onConnectionStateChange((state) => {
                setConnectionState(state);
                setConnected(state === 'connected');
            });

            // Connect
            await client.connect();

            // CRITICAL: actually re-authenticate the new socket on the server.
            // Without validateSession() the WS reopens but the server treats it
            // as anonymous, so every subsequent RPC fails with AUTH_REQUIRED and
            // the dashboard appears broken.
            if (sessionToken && playerId) {
                try {
                    const validated = await client.validateSession(sessionToken, playerId);
                    setSession(validated.session_token, validated.player_id, validated.user_id);
                    restClient.setSessionToken(validated.session_token);
                    touchSyncTime();
                } catch (validateErr: any) {
                    console.error('Session validation failed during auto-reconnect:', validateErr);
                    // Stale session — drop it and fall back to login screen.
                    unsubscribe();  // Clean up subscription
                    try { client.disconnect(); } catch { /* ignore */ }
                    useAppStore.getState().logout();
                    setAutoReconnecting(false);
                    setWaitingForToken(true);
                    pushToast({ type: 'info', message: 'セッションが期限切れです。アプリから再ログインしてください。' });
                    return;
                }
            }

            // Save authenticated client (after this, the connection state callback will be handled by Dashboard useEffect)
            setClient(client);
            // Clean up the temporary subscription now that client is in store
            unsubscribe();
            
            // Navigate to dashboard
            navigate('/dashboard');
            pushToast({ type: 'success', message: '再接続に成功しました。' });
        } catch (err: any) {
            console.error('Auto-reconnect failed:', err);
            // 失敗した場合はログイン画面を表示
            setAutoReconnecting(false);
            setWaitingForToken(true);
            pushToast({ type: 'error', message: '自動再接続に失敗しました。' });
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="login-page nebula-bg">
            <div className="glow-orb glow-orb-1" />
            <div className="glow-orb glow-orb-2" />

            <div className="login-container glass-panel">
                <p className="eyebrow">ToNRoundCounter</p>
                <h1>Cloud Mission Control</h1>
                <p className="subtitle">リアルタイム同期・監視・投票を1つの画面で。</p>

                {waitingForToken && !loading && !error && (
                    <div className="waiting-for-login elevate-card">
                        <div className="waiting-icon">
                            <svg width="64" height="64" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                                <circle cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="2" opacity="0.2"/>
                                <path d="M12 2 A10 10 0 0 1 22 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
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
                    <div className="loading-state elevate-card">
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

                <div className="login-info elevate-card">
                    <h3>主な機能</h3>
                    <div className="feature-grid">
                        <div className="feature-chip">リアルタイム同期</div>
                        <div className="feature-chip">インスタンス監視</div>
                        <div className="feature-chip">投票の即時反映</div>
                        <div className="feature-chip">統計の可視化</div>
                        <div className="feature-chip">設定の同期</div>
                        <div className="feature-chip">障害の早期検知</div>
                    </div>
                    <p className="server-hint">接続先: {serverUrl}</p>
                </div>
            </div>
        </div>
    );
};
