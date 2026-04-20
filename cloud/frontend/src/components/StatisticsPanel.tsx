/**
 * Statistics Panel Component
 */

import React, { useEffect, useRef, useState } from 'react';
import { useAppStore } from '../store/appStore';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer, BarChart, Bar, PieChart, Pie, Cell } from 'recharts';

const ROUND_TYPE_COLORS = ['#8884d8', '#82ca9d', '#ffc658', '#ff7f50', '#a28fd0', '#ff6b6b', '#4ecdc4', '#45b7d1', '#96ceb4', '#d4a574'];

export const StatisticsPanel: React.FC = () => {
    const {
        client,
        playerId,
        playerStats,
        terrorStats,
        roundTypeStats,
        trends,
        setPlayerStats,
        setTerrorStats,
        setRoundTypeStats,
        setTrends,
        touchSyncTime,
        pushToast,
    } = useAppStore();
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const loadStatisticsRef = useRef<() => Promise<void>>(() => Promise.resolve());
    const refreshTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

    useEffect(() => {
        if (client && playerId) {
            loadStatistics();
        }
    }, [client, playerId]);

    // BUG FIX: Round statistics on the dashboard never refreshed after the
    // initial load because no event was wired up. Subscribe to the new
    // `round.reported` stream emitted by the backend after a successful
    // round.report, and also poll periodically as a safety net for missed
    // events (e.g. WS reconnect) similar to PlayerStates.
    useEffect(() => {
        if (!client || !playerId) {
            return;
        }
        const scheduleReload = () => {
            if (refreshTimerRef.current) {
                clearTimeout(refreshTimerRef.current);
            }
            // Debounce bursts of round events (e.g. round end + terror death
            // arriving close together) into a single statistics reload.
            refreshTimerRef.current = setTimeout(() => {
                refreshTimerRef.current = null;
                loadStatisticsRef.current().catch(() => { /* errors handled inside */ });
            }, 750);
        };
        const unsubscribe = client.onRoundReported(() => {
            scheduleReload();
        });
        const interval = setInterval(() => {
            loadStatisticsRef.current().catch(() => { /* errors handled inside */ });
        }, 60000);
        return () => {
            unsubscribe();
            clearInterval(interval);
            if (refreshTimerRef.current) {
                clearTimeout(refreshTimerRef.current);
                refreshTimerRef.current = null;
            }
        };
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
            const parsedTerrorStats = Array.isArray(terrorData)
                ? terrorData
                : Array.isArray((terrorData as any)?.terror_stats)
                    ? (terrorData as any).terror_stats
                    : [];
            setTerrorStats(parsedTerrorStats);

            // Load round type statistics
            const roundTypeData = await client.getRoundTypeAnalytics();
            const parsedRoundTypeStats = Array.isArray(roundTypeData)
                ? roundTypeData
                : Array.isArray((roundTypeData as any)?.round_types)
                    ? (roundTypeData as any).round_types
                    : [];
            setRoundTypeStats(parsedRoundTypeStats);

            // Load trends
            const trendsData = await client.getAnalyticsTrends('day', 30);
            const parsedTrends = Array.isArray(trendsData)
                ? trendsData
                : Array.isArray((trendsData as any)?.trends)
                    ? (trendsData as any).trends
                    : [];
            setTrends(parsedTrends);
            touchSyncTime();
        } catch (error) {
            console.error('Failed to load statistics:', error);
            setError('統計データの読み込みに失敗しました');
            pushToast({ type: 'error', message: '統計データの読み込みに失敗しました。' });
        } finally {
            setLoading(false);
        }
    };

    // Always keep the ref pointed at the latest closure so subscriptions/intervals
    // call the up-to-date function (with current client/playerId/setters).
    loadStatisticsRef.current = loadStatistics;

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
            pushToast({ type: 'success', message: `${format.toUpperCase()} を出力しました。` });
        } catch (error) {
            console.error('Failed to export data:', error);
            setError('データのエクスポートに失敗しました');
            pushToast({ type: 'error', message: 'エクスポートに失敗しました。' });
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
                            <h4>個人生存率</h4>
                            <p className="stat-value">{((Number(playerStats.personal_survival_rate) || 0) * 100).toFixed(1)}%</p>
                            <p className="stat-detail">{playerStats.personal_survived ?? 0} / {playerStats.personal_total_rounds ?? 0} ラウンド</p>
                        </div>
                        <div className="stat-card">
                            <h4>平均ラウンド生存率</h4>
                            <p className="stat-value">{((Number(playerStats.avg_survival_rate) || 0) * 100).toFixed(1)}%</p>
                        </div>
                        <div className="stat-card">
                            <h4>平均時間</h4>
                            <p className="stat-value">{(Number(playerStats.avg_duration_minutes) || 0).toFixed(1)}分</p>
                        </div>
                    </div>
                    {playerStats.total_rounds === 0 && (
                        <p className="stats-empty-hint">
                            デスクトップアプリでクラウド同期を有効にしてラウンドをプレイすると、ここに統計が表示されます。
                        </p>
                    )}
                </section>
            )}

            {trends && trends.length > 0 ? (
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
            ) : !loading && playerStats && (
                <section className="trends-chart stats-empty-section">
                    <h3>ラウンド数推移</h3>
                    <p className="stats-empty-hint">ラウンドデータが蓄積されるとグラフが表示されます。</p>
                </section>
            )}

            {roundTypeStats && roundTypeStats.length > 0 ? (
                <section className="round-type-statistics">
                    <h3>ラウンド種類別統計</h3>
                    <div className="round-type-chart-row">
                        <ResponsiveContainer width="100%" height={300}>
                            <PieChart>
                                <Pie
                                    data={roundTypeStats.slice(0, 10)}
                                    dataKey="round_count"
                                    nameKey="round_key"
                                    cx="50%"
                                    cy="50%"
                                    outerRadius={100}
                                    label={({ round_key, round_share }) => `${round_key} (${((round_share || 0) * 100).toFixed(0)}%)`}
                                >
                                    {roundTypeStats.slice(0, 10).map((_: any, i: number) => (
                                        <Cell key={i} fill={ROUND_TYPE_COLORS[i % ROUND_TYPE_COLORS.length]} />
                                    ))}
                                </Pie>
                                <Tooltip formatter={(value: any, name: string) => [value, 'ラウンド数']} />
                            </PieChart>
                        </ResponsiveContainer>
                    </div>
                    <div className="round-type-list">
                        <table>
                            <thead>
                                <tr>
                                    <th>ラウンド種類</th>
                                    <th>回数</th>
                                    <th>割合</th>
                                    <th>個人生存率</th>
                                    <th>平均ラウンド生存率</th>
                                    <th>平均時間</th>
                                </tr>
                            </thead>
                            <tbody>
                                {roundTypeStats.map((stat: any) => (
                                    <tr key={stat.round_key}>
                                        <td>{stat.round_key}</td>
                                        <td>{stat.round_count}</td>
                                        <td>{((Number(stat.round_share) || 0) * 100).toFixed(1)}%</td>
                                        <td>{((Number(stat.personal_survival_rate) || 0) * 100).toFixed(1)}%</td>
                                        <td>{((Number(stat.avg_survival_rate) || 0) * 100).toFixed(1)}%</td>
                                        <td>{(Number(stat.avg_duration_minutes) || 0).toFixed(1)}分</td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </section>
            ) : !loading && playerStats && (
                <section className="round-type-statistics stats-empty-section">
                    <h3>ラウンド種類別統計</h3>
                    <p className="stats-empty-hint">ラウンドデータが蓄積されると種類別の統計が表示されます。</p>
                </section>
            )}

            {terrorStats && terrorStats.length > 0 ? (
                <section className="terror-statistics">
                    <h3>テラー出現統計（上位10）</h3>
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
                                    <th>テラー名</th>
                                    <th>出現回数</th>
                                    <th>遭遇率（全体）</th>
                                    <th>生存率（個人）</th>
                                    <th>ラウンド種類別遭遇率</th>
                                    <th>人気度</th>
                                </tr>
                            </thead>
                            <tbody>
                                {terrorStats.slice(0, 20).map((stat: any, index: number) => {
                                    const rtRates: any[] = Array.isArray(stat.round_type_encounter_rates)
                                        ? stat.round_type_encounter_rates
                                        : [];
                                    const hasPersonal = stat.personal_survival_rate_with_terror !== null
                                        && stat.personal_survival_rate_with_terror !== undefined;
                                    return (
                                        <tr key={stat.terror_name}>
                                            <td>{index + 1}</td>
                                            <td>{stat.terror_name}</td>
                                            <td>{stat.appearance_count}</td>
                                            <td>{((Number(stat.encounter_rate) || 0) * 100).toFixed(1)}%</td>
                                            <td className="stat-survival-cell">
                                                {hasPersonal
                                                    ? <>{((Number(stat.personal_survival_rate_with_terror)) * 100).toFixed(1)}%<span className="stat-sub">{stat.personal_survived_with_terror}/{stat.personal_encounters}</span></>
                                                    : <span className="stat-na">-</span>}
                                            </td>
                                            <td className="stat-rt-cell">
                                                {rtRates.length === 0
                                                    ? <span className="stat-na">-</span>
                                                    : <ul className="rt-rate-list">
                                                        {rtRates.slice(0, 4).map((rt: any) => (
                                                            <li key={rt.round_key}>
                                                                <span className="rt-name">{rt.round_key}</span>
                                                                <span className="rt-rate">{((Number(rt.encounter_rate_in_type) || 0) * 100).toFixed(1)}%</span>
                                                            </li>
                                                        ))}
                                                    </ul>}
                                            </td>
                                            <td>{Number(stat.avg_desire_count || 0).toFixed(1)}</td>
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                    </div>
                </section>
            ) : !loading && playerStats && (
                <section className="terror-statistics stats-empty-section">
                    <h3>テラー出現統計</h3>
                    <p className="stats-empty-hint">テラー出現データが蓄積されると統計が表示されます。</p>
                </section>
            )}
        </div>
    );
};
