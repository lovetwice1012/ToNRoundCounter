/**
 * Statistics Panel Component
 */

import React, { useEffect, useState } from 'react';
import { useAppStore } from '../store/appStore';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer, BarChart, Bar } from 'recharts';

export const StatisticsPanel: React.FC = () => {
    const { client, playerId, playerStats, terrorStats, trends, setPlayerStats, setTerrorStats, setTrends } = useAppStore();
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (client && playerId) {
            loadStatistics();
        }
    }, [client, playerId]);

    const loadStatistics = async () => {
        if (!client || !playerId) return;

        setLoading(true);
        setError(null);
        try {
            // Load player statistics
            const playerData = await client.getPlayerAnalytics(playerId);
            setPlayerStats(playerData);

            // Load terror statistics
            const terrorData = await client.getTerrorAnalytics();
            setTerrorStats(Array.isArray(terrorData) ? terrorData : []);

            // Load trends
            const trendsData = await client.getAnalyticsTrends('day', 30);
            setTrends(Array.isArray(trendsData) ? trendsData : []);
        } catch (error) {
            console.error('Failed to load statistics:', error);
            setError('統計データの読み込みに失敗しました');
        } finally {
            setLoading(false);
        }
    };

    const handleExport = async (format: 'json' | 'csv') => {
        if (!client) return;

        setError(null);
        try {
            const data = await client.exportAnalytics(format, 'rounds');
            const blob = new Blob([data], { type: format === 'json' ? 'application/json' : 'text/csv' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `statistics_${Date.now()}.${format}`;
            a.click();
            URL.revokeObjectURL(url);
        } catch (error) {
            console.error('Failed to export data:', error);
            setError('データのエクスポートに失敗しました');
        }
    };

    if (loading && !playerStats && !terrorStats) {
        return <div className="loading">統計データを読み込んでいます...</div>;
    }

    return (
        <div className="statistics-panel">
            <div className="statistics-header">
                <h2>統計・分析</h2>
                <div className="export-buttons">
                    <button onClick={() => handleExport('json')} className="btn-export" disabled={loading}>
                        JSON出力
                    </button>
                    <button onClick={() => handleExport('csv')} className="btn-export" disabled={loading}>
                        CSV出力
                    </button>
                    <button onClick={loadStatistics} className="btn-refresh" disabled={loading}>
                        {loading ? '更新中...' : '更新'}
                    </button>
                </div>
            </div>

            {error && (
                <div className="error-message">
                    {error}
                    <button onClick={() => setError(null)} className="btn-dismiss">×</button>
                </div>
            )}

            {playerStats && (
                <section className="player-statistics">
                    <h3>プレイヤー統計</h3>
                    <div className="stats-grid">
                        <div className="stat-card">
                            <h4>総ラウンド数</h4>
                            <p className="stat-value">{playerStats.total_rounds}</p>
                        </div>
                        <div className="stat-card">
                            <h4>完了ラウンド数</h4>
                            <p className="stat-value">{playerStats.completed_rounds}</p>
                        </div>
                        <div className="stat-card">
                            <h4>平均生存率</h4>
                            <p className="stat-value">{(playerStats.avg_survival_rate * 100).toFixed(1)}%</p>
                        </div>
                        <div className="stat-card">
                            <h4>平均時間</h4>
                            <p className="stat-value">{playerStats.avg_duration_minutes.toFixed(1)}分</p>
                        </div>
                    </div>
                </section>
            )}

            {trends && trends.length > 0 && (
                <section className="trends-chart">
                    <h3>ラウンド数推移</h3>
                    <ResponsiveContainer width="100%" height={300}>
                        <LineChart data={trends}>
                            <CartesianGrid strokeDasharray="3 3" />
                            <XAxis dataKey="period" />
                            <YAxis />
                            <Tooltip />
                            <Legend />
                            <Line type="monotone" dataKey="round_count" stroke="#8884d8" name="ラウンド数" />
                            <Line type="monotone" dataKey="avg_survival_rate" stroke="#82ca9d" name="平均生存率" />
                        </LineChart>
                    </ResponsiveContainer>
                </section>
            )}

            {terrorStats && terrorStats.length > 0 && (
                <section className="terror-statistics">
                    <h3>テロール出現統計（上位10）</h3>
                    <ResponsiveContainer width="100%" height={300}>
                        <BarChart data={terrorStats.slice(0, 10)}>
                            <CartesianGrid strokeDasharray="3 3" />
                            <XAxis dataKey="terror_name" />
                            <YAxis />
                            <Tooltip />
                            <Legend />
                            <Bar dataKey="appearance_count" fill="#8884d8" name="出現回数" />
                        </BarChart>
                    </ResponsiveContainer>

                    <div className="terror-list">
                        <table>
                            <thead>
                                <tr>
                                    <th>順位</th>
                                    <th>テロール名</th>
                                    <th>出現回数</th>
                                    <th>人気度</th>
                                </tr>
                            </thead>
                            <tbody>
                                {terrorStats.slice(0, 20).map((stat: any, index: number) => (
                                    <tr key={stat.terror_name}>
                                        <td>{index + 1}</td>
                                        <td>{stat.terror_name}</td>
                                        <td>{stat.appearance_count}</td>
                                        <td>{stat.avg_desire_count.toFixed(1)}</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </section>
            )}
        </div>
    );
};
