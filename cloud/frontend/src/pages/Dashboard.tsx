/**
 * Main Dashboard Component
 */

import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAppStore } from '../store/appStore';
import { ConnectionStatus } from '../components/ConnectionStatus';
import { InstanceList } from '../components/InstanceList';
import { PlayerStates } from '../components/PlayerStates';
import { VotingPanel } from '../components/VotingPanel';
import { StatisticsPanel } from '../components/StatisticsPanel';
import { WishedTerrorPanel } from '../components/WishedTerrorPanel';
import { createRestClient } from '../lib/rest-client';

export const Dashboard: React.FC = () => {
    const navigate = useNavigate();
    const {
        client,
        restClient,
        connected,
        connectionState,
        currentInstance,
        sessionToken,
        playerId,
        setClient,
        setRestClient,
        setConnected,
        setConnectionState,
        logout,
    } = useAppStore();

    const [activeTab, setActiveTab] = useState<'overview' | 'instances' | 'players' | 'stats' | 'settings'>('overview');

    // セッションからの自動再接続
    useEffect(() => {
        if (sessionToken && playerId && !client) {
            const reconnect = async () => {
                try {
                    // REST Clientの初期化
                    const apiUrl = (import.meta as any).env?.VITE_API_URL || 'http://localhost:8080';
                    const newRestClient = createRestClient(apiUrl, sessionToken);
                    setRestClient(newRestClient);

                    // WebSocket Clientの初期化
                    const wsUrl = (import.meta as any).env?.VITE_WS_URL || 'ws://localhost:8080/ws';
                    const { ToNRoundCloudClient } = await import('../lib/websocket-client');
                    const newClient = new ToNRoundCloudClient(wsUrl);
                    
                    newClient.onConnectionStateChange((state) => {
                        setConnectionState(state);
                        setConnected(state === 'connected');
                    });
                    
                    await newClient.connect();
                    
                    // セッションを検証して認証
                    await newClient.validateSession(sessionToken, playerId);
                    
                    setClient(newClient);
                    
                    console.log('自動再接続に成功しました');
                } catch (error) {
                    console.error('自動再接続に失敗しました:', error);
                    // セッションが無効な場合はログアウト
                    logout();
                    navigate('/login');
                }
            };
            
            reconnect();
        }
    }, [sessionToken, playerId, client, setClient, setRestClient, setConnected, setConnectionState, logout, navigate]);

    const handleLogout = async () => {
        if (client) {
            try {
                await client.disconnect();
            } catch (error) {
                console.error('Logout error:', error);
            }
        }
        logout();
        navigate('/login');
    };

    useEffect(() => {
        // Auto-refresh data when connected
        if (connected && client) {
            console.log('[Dashboard] Connected, loading initial data...');
            console.log('[Dashboard] PlayerId:', playerId);
            console.log('[Dashboard] SessionToken:', sessionToken ? 'present' : 'missing');
            
            // 初回データ取得
            const loadInitialData = async () => {
                try {
                    console.log('[Dashboard] Calling getCurrentInstance...');
                    // 現在のインスタンスを取得
                    const instance = await client.getCurrentInstance();
                    console.log('[Dashboard] Current instance:', instance);
                    useAppStore.getState().setCurrentInstance(instance);

                    console.log('[Dashboard] Calling listInstances...');
                    // インスタンス一覧を取得
                    const instances = await client.listInstances();
                    console.log('[Dashboard] Instances:', instances);
                    useAppStore.getState().setInstances(Array.isArray(instances) ? instances : []);
                } catch (error) {
                    console.error('[Dashboard] Failed to load initial data:', error);
                    // エラー詳細をログ出力
                    if (error instanceof Error) {
                        console.error('[Dashboard] Error message:', error.message);
                        console.error('[Dashboard] Error stack:', error.stack);
                    }
                }
            };

            loadInitialData();

            // WebSocketイベントを購読
            const unsubscribeJoined = client.onInstanceMemberJoined((data) => {
                console.log('Member joined:', data);
                // 現在のインスタンスを更新
                if (currentInstance && data.instance_id === currentInstance.instance_id) {
                    loadInitialData();
                }
            });

            const unsubscribeLeft = client.onInstanceMemberLeft((data) => {
                console.log('Member left:', data);
                // 現在のインスタンスを更新
                if (currentInstance && data.instance_id === currentInstance.instance_id) {
                    loadInitialData();
                } else if (data.player_id === playerId) {
                    // 自分が退出した場合
                    useAppStore.getState().setCurrentInstance(null);
                }
            });

            const unsubscribeUpdated = client.onInstanceUpdated((data) => {
                console.log('Instance updated:', data);
                loadInitialData();
            });

            const unsubscribeDeleted = client.onInstanceDeleted((data) => {
                console.log('Instance deleted:', data);
                // 削除されたインスタンスが現在のインスタンスの場合
                if (currentInstance && data.instance_id === currentInstance.instance_id) {
                    useAppStore.getState().setCurrentInstance(null);
                }
                loadInitialData();
            });

            // 定期的なポーリング（フォールバック）
            const interval = setInterval(async () => {
                try {
                    // Refresh instances
                    const instances = await client.listInstances();
                    // 配列であることを保証
                    useAppStore.getState().setInstances(Array.isArray(instances) ? instances : []);
                } catch (error) {
                    console.error('Failed to refresh data:', error);
                    // エラー時も空配列をセット
                    useAppStore.getState().setInstances([]);
                }
            }, 10000); // 10秒ごと

            return () => {
                clearInterval(interval);
                unsubscribeJoined();
                unsubscribeLeft();
                unsubscribeUpdated();
                unsubscribeDeleted();
            };
        }
    }, [connected, client, currentInstance, playerId]);

    return (
        <div className="dashboard-page">
            <header className="dashboard-header">
                <h1>ToNRoundCounter Cloud Dashboard</h1>
                <div style={{ display: 'flex', alignItems: 'center', gap: '20px' }}>
                    <ConnectionStatus />
                    <button 
                        onClick={handleLogout} 
                        className="btn-logout"
                        style={{
                            padding: '8px 16px',
                            backgroundColor: '#f44336',
                            color: 'white',
                            border: 'none',
                            borderRadius: '6px',
                            fontWeight: 500,
                            cursor: 'pointer',
                            fontSize: '14px',
                            transition: 'all 0.2s ease'
                        }}
                        onMouseEnter={(e) => e.currentTarget.style.backgroundColor = '#da190b'}
                        onMouseLeave={(e) => e.currentTarget.style.backgroundColor = '#f44336'}
                    >
                        ログアウト
                    </button>
                </div>
            </header>

            <nav className="dashboard-nav">
                <button
                    className={activeTab === 'overview' ? 'active' : ''}
                    onClick={() => setActiveTab('overview')}
                >
                    概要
                </button>
                <button
                    className={activeTab === 'instances' ? 'active' : ''}
                    onClick={() => setActiveTab('instances')}
                >
                    インスタンス
                </button>
                <button
                    className={activeTab === 'players' ? 'active' : ''}
                    onClick={() => setActiveTab('players')}
                >
                    プレイヤー
                </button>
                <button
                    className={activeTab === 'stats' ? 'active' : ''}
                    onClick={() => setActiveTab('stats')}
                >
                    統計
                </button>
                <button
                    className={activeTab === 'settings' ? 'active' : ''}
                    onClick={() => setActiveTab('settings')}
                >
                    設定
                </button>
            </nav>

            <main className="dashboard-content">
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
                                    {currentInstance.members && currentInstance.members.length > 0 && (
                                        <div>
                                            <p><strong>メンバー:</strong></p>
                                            <ul>
                                                {currentInstance.members.map((member: any) => (
                                                    <li key={member.player_id}>
                                                        {member.player_name || member.player_id}
                                                        <span style={{ marginLeft: '10px', color: '#888', fontSize: '0.9em' }}>
                                                            (参加: {new Date(member.joined_at).toLocaleString()})
                                                        </span>
                                                    </li>
                                                ))}
                                            </ul>
                                        </div>
                                    )}
                                </div>
                            ) : (
                                <p>インスタンスに参加していません</p>
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
                        <WishedTerrorPanel />
                    </div>
                )}
            </main>
        </div>
    );
};
