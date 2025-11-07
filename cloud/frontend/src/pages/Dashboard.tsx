/**
 * Main Dashboard Component
 */

import React, { useEffect, useState } from 'react';
import { useAppStore } from '../store/appStore';
import { ConnectionStatus } from '../components/ConnectionStatus';
import { InstanceList } from '../components/InstanceList';
import { PlayerStates } from '../components/PlayerStates';
import { VotingPanel } from '../components/VotingPanel';
import { StatisticsPanel } from '../components/StatisticsPanel';
import { WishedTerrorPanel } from '../components/WishedTerrorPanel';

export const Dashboard: React.FC = () => {
    const {
        client,
        connected,
        connectionState,
        currentInstance,
    } = useAppStore();

    const [activeTab, setActiveTab] = useState<'overview' | 'instances' | 'players' | 'stats' | 'settings'>('overview');

    useEffect(() => {
        // Auto-refresh data when connected
        if (connected && client) {
            const interval = setInterval(async () => {
                try {
                    // Refresh instances
                    const instances = await client.listInstances();
                    useAppStore.getState().setInstances(instances);
                } catch (error) {
                    console.error('Failed to refresh data:', error);
                }
            }, 5000);

            return () => clearInterval(interval);
        }
    }, [connected, client]);

    return (
        <div className="dashboard">
            <header className="dashboard-header">
                <h1>ToNRoundCounter Cloud Dashboard</h1>
                <ConnectionStatus />
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
                                    <p>ID: {currentInstance.instance_id}</p>
                                    <p>プレイヤー数: {currentInstance.current_player_count}/{currentInstance.max_players}</p>
                                    <p>ステータス: {currentInstance.status}</p>
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
