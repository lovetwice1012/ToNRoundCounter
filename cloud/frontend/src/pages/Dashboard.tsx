/**
 * Main Dashboard Component
 */

import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAppStore } from '../store/appStore';
import { ConnectionStatus } from '../components/ConnectionStatus';
import { InstanceList } from '../components/InstanceList';
import { PlayerStates } from '../components/PlayerStates';
import { VotingPanel } from '../components/VotingPanel';
import { StatisticsPanel } from '../components/StatisticsPanel';
import { CoordinatedAutoSuicidePanel } from '../components/CoordinatedAutoSuicidePanel';
import { WishedTerrorPanel } from '../components/WishedTerrorPanel';
import { createRestClient } from '../lib/rest-client';
import { getApiBaseUrl, getWebSocketUrl } from '../lib/cloud-url';
import type { LoginDevice } from '../lib/websocket-client';

const ADMIN_USER_ID = 'yussy5373';

export const Dashboard: React.FC = () => {
    const navigate = useNavigate();
    const {
        client,
        connected,
        connectionState,
        currentInstance,
        sessionToken,
        playerId,
        userId,
        setClient,
        setRestClient,
        setConnected,
        setConnectionState,
        setCurrentInstance,
        setInstances,
        setWsLatencyMs,
        touchSyncTime,
        lastSyncAt,
        pushToast,
        logout,
    } = useAppStore();

    const [activeTab, setActiveTab] = useState<'overview' | 'instances' | 'players' | 'stats' | 'settings'>('overview');
    const [recentDevices, setRecentDevices] = useState<LoginDevice[]>([]);
    const isAdmin = userId === ADMIN_USER_ID || playerId === ADMIN_USER_ID;

    // Ref so subscription callbacks can read the latest currentInstance without
    // being listed in the subscription useEffect's dependency array.  Adding
    // the full object would cause every refreshSnapshot() → setCurrentInstance()
    // call to tear down and rebuild all subscriptions and reset the polling timer.
    const currentInstanceRef = useRef(currentInstance);
    useEffect(() => {
        currentInstanceRef.current = currentInstance;
    }, [currentInstance]);

    const refreshSnapshot = useCallback(async (targetClient: any) => {
        if (!targetClient) {
            return;
        }

        const startedAt = performance.now();
        const [instance, instances, clientStatus] = await Promise.all([
            targetClient.getCurrentInstance().catch(() => null),
            targetClient.listInstances().catch(() => []),
            targetClient.getClientStatus().catch(() => null),
        ]);

        setCurrentInstance(instance || null);
        setInstances(Array.isArray(instances) ? instances : []);
        setRecentDevices(Array.isArray(clientStatus?.recent_devices) ? clientStatus.recent_devices : []);
        setWsLatencyMs(Math.round(performance.now() - startedAt));
        touchSyncTime();
    }, [setCurrentInstance, setInstances, setWsLatencyMs, touchSyncTime]);

    // セッションからの自動再接続
    useEffect(() => {
        if (sessionToken && playerId && !client) {
            const reconnect = async () => {
                try {
                    // REST Clientの初期化
                    const apiUrl = getApiBaseUrl();
                    const newRestClient = createRestClient(apiUrl, sessionToken);
                    setRestClient(newRestClient);

                    // WebSocket Clientの初期化
                    const wsUrl = getWebSocketUrl();
                    const { ToNRoundCloudClient } = await import('../lib/websocket-client');
                    const newClient = new ToNRoundCloudClient(wsUrl);

                    newClient.onConnectionStateChange((state) => {
                        setConnectionState(state);
                        setConnected(state === 'connected');
                        // WS クライアントが再認証不可と判断した場合、ログイン画面へ遷移
                        if (state === 'auth-required') {
                            logout();
                            navigate('/login');
                            pushToast({ type: 'error', message: 'セッションの有効期限が切れました。再ログインしてください。' });
                        }
                    });

                    await newClient.connect();

                    // セッションを検証して認証
                    const validated = await newClient.validateSession(sessionToken, playerId);

                    // validateSession may rotate the session token — persist
                    // the freshly-issued token so subsequent REST/WS calls use
                    // the current value instead of the (possibly expired) one
                    // we started the reconnect with.
                    useAppStore.getState().setSession(validated.session_token, validated.player_id, validated.user_id);
                    newRestClient.setSessionToken(validated.session_token);

                    setClient(newClient);

                    await refreshSnapshot(newClient);
                    pushToast({ type: 'success', message: '接続を復元しました。' });
                } catch (error) {
                    console.error('自動再接続に失敗しました:', error);
                    // セッションが無効な場合はログアウト
                    logout();
                    navigate('/login');
                    pushToast({ type: 'error', message: 'セッションを復元できませんでした。再ログインしてください。' });
                }
            };

            reconnect();
        }
    }, [sessionToken, playerId, client, setClient, setRestClient, setConnected, setConnectionState, logout, navigate, refreshSnapshot, pushToast]);

    const handleLogout = async () => {
        if (client) {
            try {
                await client.disconnect();
            } catch (error) {
                console.error('Logout error:', error);
            }
        }
        logout();
        pushToast({ type: 'info', message: 'ログアウトしました。' });
        navigate('/login');
    };

    useEffect(() => {
        // Auto-refresh data when connected
        if (connected && client) {
            refreshSnapshot(client).catch((error) => {
                console.error('[Dashboard] Failed to load initial data:', error);
            });

            // WebSocketイベントを購読
            // Callbacks read currentInstanceRef.current (not the closed-over
            // `currentInstance`) so the effect does not depend on currentInstance.
            const unsubscribeJoined = client.onInstanceMemberJoined((data) => {
                if (currentInstanceRef.current && data.instance_id === currentInstanceRef.current.instance_id) {
                    refreshSnapshot(client).catch(() => undefined);
                }
            });

            const unsubscribeLeft = client.onInstanceMemberLeft((data) => {
                if (currentInstanceRef.current && data.instance_id === currentInstanceRef.current.instance_id) {
                    refreshSnapshot(client).catch(() => undefined);
                } else if (data.player_id === client.userId) {
                    // 自分が退出した場合
                    // サーバーは ws.userId（内部 DB ID）を player_id として broadcast するため
                    // store の playerId（VRChat プレイヤー ID）ではなく client.userId で照合する。
                    setCurrentInstance(null);
                }
            });

            const unsubscribeUpdated = client.onInstanceUpdated(() => {
                refreshSnapshot(client).catch(() => undefined);
            });

            const unsubscribeDeleted = client.onInstanceDeleted((data) => {
                if (currentInstanceRef.current && data.instance_id === currentInstanceRef.current.instance_id) {
                    setCurrentInstance(null);
                }
                refreshSnapshot(client).catch(() => undefined);
            });

            // 定期的なポーリング（フォールバック）— currentInstance も含めて更新する
            const interval = setInterval(async () => {
                try {
                    const startedAt = performance.now();
                    const [instance, instances, clientStatus] = await Promise.all([
                        client.getCurrentInstance().catch(() => null),
                        client.listInstances().catch(() => []),
                        client.getClientStatus().catch(() => null),
                    ]);
                    setCurrentInstance(instance || null);
                    setInstances(Array.isArray(instances) ? instances : []);
                    setRecentDevices(Array.isArray(clientStatus?.recent_devices) ? clientStatus.recent_devices : []);
                    setWsLatencyMs(Math.round(performance.now() - startedAt));
                    touchSyncTime();
                } catch (error) {
                    console.error('Failed to refresh data:', error);
                    setInstances([]);
                    setWsLatencyMs(null);
                }
            }, 8000);

            return () => {
                clearInterval(interval);
                unsubscribeJoined();
                unsubscribeLeft();
                unsubscribeUpdated();
                unsubscribeDeleted();
            };
        }
    }, [
        connected,
        client,
        // currentInstance is intentionally NOT in this dep array — callbacks
        // read it via currentInstanceRef.current to prevent subscription churn.
        // playerId is also omitted — self-left check uses client.userId directly.
        refreshSnapshot,
        setCurrentInstance,
        setInstances,
        setWsLatencyMs,
        touchSyncTime,
    ]);

    const syncLabel = useMemo(() => {
        if (!lastSyncAt) {
            return '未同期';
        }
        return new Date(lastSyncAt).toLocaleTimeString();
    }, [lastSyncAt]);

    const navItems: Array<{ key: typeof activeTab; label: string; desc: string }> = [
        { key: 'overview', label: 'Overview', desc: '現在の状況を監視' },
        { key: 'instances', label: 'Instances', desc: 'インスタンス一覧' },
        { key: 'players', label: 'Players', desc: '状態と更新時刻' },
        { key: 'stats', label: 'Analytics', desc: '統計と推移' },
        { key: 'settings', label: 'Preferences', desc: '希望テラー管理' },
    ];

    const formatMemory = (memoryMb?: number) => {
        if (!memoryMb || memoryMb <= 0) {
            return '-';
        }
        return memoryMb >= 1024
            ? `${Math.round(memoryMb / 102.4) / 10} GB`
            : `${memoryMb} MB`;
    };

    return (
        <div className="dashboard-page nebula-bg">
            <aside className="dashboard-sidebar glass-panel">
                <div className="brand-block">
                    <p className="eyebrow">Cloud Mission Control</p>
                    <h1>ToNRoundCounter</h1>
                    <p className="brand-sub">リアルタイム同期の監視センター</p>
                </div>

                <div className="sidebar-metrics">
                    <div className="metric-tile">
                        <span>Connection</span>
                        <strong>{connectionState}</strong>
                    </div>
                    <div className="metric-tile">
                        <span>Session</span>
                        <strong>{playerId || 'anonymous'}</strong>
                    </div>
                    <div className="metric-tile">
                        <span>Last Sync</span>
                        <strong>{syncLabel}</strong>
                    </div>
                </div>

                <nav className="dashboard-nav-side">
                    {navItems.map((item) => (
                        <button
                            key={item.key}
                            className={`nav-tile ${activeTab === item.key ? 'active' : ''}`}
                            onClick={() => setActiveTab(item.key)}
                            type="button"
                        >
                            <span>{item.label}</span>
                            <small>{item.desc}</small>
                        </button>
                    ))}
                    {isAdmin && (
                        <button
                            className="nav-tile admin-nav-tile"
                            onClick={() => navigate('/admin/app-privileges')}
                            type="button"
                        >
                            <span>App Privileges</span>
                            <small>特権スコープ管理</small>
                        </button>
                    )}
                </nav>

                <button onClick={handleLogout} className="btn-logout" type="button">
                    ログアウト
                </button>
            </aside>

            <main className="dashboard-content">
                <header className="dashboard-header glass-panel">
                    <div>
                        <p className="eyebrow">Dashboard</p>
                        <h2>Operations Overview</h2>
                    </div>
                    <ConnectionStatus />
                </header>

                {!connected && (
                    <div className="connection-warning">
                        <p>サーバーに接続されていません。</p>
                        <p>状態: {connectionState}</p>
                    </div>
                )}

                {activeTab === 'overview' && (
                    <div className="overview-tab">
                        <section className="overview-section">
                            <h2>現在のインスタンス</h2>
                            {currentInstance ? (
                                <div className="current-instance">
                                    <p><strong>インスタンスID:</strong> {currentInstance.instance_id}</p>
                                    <p><strong>プレイヤー数:</strong> {currentInstance.member_count || currentInstance.current_player_count || 0}/{currentInstance.max_players}</p>
                                    <p><strong>ステータス:</strong> {currentInstance.status || 'ACTIVE'}</p>
                                    {Array.isArray(currentInstance.members) && currentInstance.members.length > 0 && (
                                        <div>
                                            <p><strong>メンバー:</strong></p>
                                            <ul>
                                                {currentInstance.members
                                                    .filter((member: any) => member && member.player_id)
                                                    .map((member: any) => (
                                                    <li key={member.player_id}>
                                                        {member.player_name || member.player_id || '不明なプレイヤー'}
                                                        <span style={{ marginLeft: '10px', color: '#888', fontSize: '0.9em' }}>
                                                            (参加: {member.joined_at ? new Date(member.joined_at).toLocaleString() : '不明'})
                                                        </span>
                                                    </li>
                                                ))}
                                            </ul>
                                        </div>
                                    )}
                                </div>
                            ) : (
                                <p className="empty-message">インスタンスに参加していません</p>
                            )}
                        </section>

                        <section className="overview-section">
                            <h2>最近のログイン端末</h2>
                            {recentDevices.length > 0 ? (
                                <div className="device-list">
                                    {recentDevices.map((device) => (
                                        <article className="device-card" key={`${device.id}_${device.session_id}`}>
                                            <div className="device-card-header">
                                                <div>
                                                    <strong>{device.device_name || 'Unknown device'}</strong>
                                                    <p>{device.client_type} / v{device.client_version}</p>
                                                </div>
                                                <span className={`device-badge ${device.connected ? 'online' : ''}`}>
                                                    {device.connected ? '接続中' : '履歴'}
                                                </span>
                                            </div>
                                            <dl className="device-details">
                                                <div>
                                                    <dt>OS</dt>
                                                    <dd>{device.os_description || '-'}</dd>
                                                </div>
                                                <div>
                                                    <dt>CPU</dt>
                                                    <dd>{device.processor_name || '-'}</dd>
                                                </div>
                                                <div>
                                                    <dt>GPU</dt>
                                                    <dd>{device.gpu_name || '-'}</dd>
                                                </div>
                                                <div>
                                                    <dt>Memory</dt>
                                                    <dd>{formatMemory(device.memory_mb)}</dd>
                                                </div>
                                                <div>
                                                    <dt>IP</dt>
                                                    <dd>{device.ip_address || '-'}</dd>
                                                </div>
                                                <div>
                                                    <dt>Login</dt>
                                                    <dd>{device.logged_in_at ? new Date(device.logged_in_at).toLocaleString() : '-'}</dd>
                                                </div>
                                            </dl>
                                        </article>
                                    ))}
                                </div>
                            ) : (
                                <p className="empty-message">ログイン端末の履歴はまだありません</p>
                            )}
                        </section>

                        {currentInstance && <VotingPanel />}
                        {currentInstance && <PlayerStates />}
                    </div>
                )}

                {activeTab === 'instances' && (
                    <div className="instances-tab">
                        <InstanceList />
                    </div>
                )}

                {activeTab === 'players' && (
                    <div className="players-tab">
                        <PlayerStates />
                    </div>
                )}

                {activeTab === 'stats' && (
                    <div className="stats-tab">
                        <StatisticsPanel />
                    </div>
                )}

                {activeTab === 'settings' && (
                    <div className="settings-tab">
                        <CoordinatedAutoSuicidePanel />
                        <WishedTerrorPanel />
                    </div>
                )}
            </main>
        </div>
    );
};
