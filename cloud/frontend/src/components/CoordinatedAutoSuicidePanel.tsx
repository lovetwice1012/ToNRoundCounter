/**
 * Coordinated Auto-Suicide Panel
 */

import React, { useEffect, useMemo, useState } from 'react';
import { useAppStore } from '../store/appStore';

interface CoordinatedEntry {
    id: string;
    terror_name: string;
    round_key: string;
    created_at?: string;
    created_by?: string;
    source?: 'manual' | 'vote';
}

interface CoordinatedPresetEntry {
    terror_name: string;
    round_key: string;
}

interface CoordinatedPreset {
    id: string;
    name: string;
    entries: CoordinatedPresetEntry[];
    created_at?: string;
}

interface CoordinatedState {
    entries: CoordinatedEntry[];
    presets: CoordinatedPreset[];
    skip_all_without_survival_wish: boolean;
    updated_at?: string;
}

const emptyState: CoordinatedState = {
    entries: [],
    presets: [],
    skip_all_without_survival_wish: false,
};

function makeId(prefix: string): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
        return `${prefix}_${crypto.randomUUID()}`;
    }
    return `${prefix}_${Date.now()}_${Math.random().toString(36).slice(2, 10)}`;
}

function normalizeKey(terrorName: string, roundKey: string): string {
    return `${terrorName.trim().toLocaleLowerCase()}::${roundKey.trim().toLocaleLowerCase()}`;
}

function isWildcardValue(value: string): boolean {
    const normalized = value.trim().toLocaleLowerCase();
    return !normalized
        || normalized === '*'
        || normalized === 'all'
        || normalized === 'any'
        || normalized === 'all terrors'
        || normalized === 'all rounds'
        || normalized === '全'
        || normalized === '全部'
        || normalized === '全て'
        || normalized === 'すべて'
        || normalized === '全テラー'
        || normalized === '全ラウンド';
}

function normalizeTargetInput(value: string): string {
    const trimmed = value.trim();
    return isWildcardValue(trimmed) ? '' : trimmed;
}

function hasScopedTarget(terrorName: string, roundKey: string): boolean {
    return !isWildcardValue(terrorName) || !isWildcardValue(roundKey);
}

function normalizeState(raw: any): CoordinatedState {
    const entries = Array.isArray(raw?.entries)
        ? raw.entries
            .filter((entry: any) => {
                const entryTerrorName = typeof entry?.terror_name === 'string' ? entry.terror_name.trim() : '';
                const entryRoundKey = typeof entry?.round_key === 'string' ? entry.round_key.trim() : '';
                return hasScopedTarget(entryTerrorName, entryRoundKey);
            })
            .map((entry: any) => ({
                id: typeof entry.id === 'string' && entry.id.trim() ? entry.id : makeId('entry'),
                terror_name: normalizeTargetInput(typeof entry.terror_name === 'string' ? entry.terror_name : ''),
                round_key: normalizeTargetInput(typeof entry.round_key === 'string' ? entry.round_key : ''),
                created_at: typeof entry.created_at === 'string' ? entry.created_at : undefined,
                created_by: typeof entry.created_by === 'string' ? entry.created_by : undefined,
                source: entry.source === 'vote' ? 'vote' : 'manual',
            }))
        : [];

    const presets = Array.isArray(raw?.presets)
        ? raw.presets
            .filter((preset: any) => typeof preset?.name === 'string' && preset.name.trim().length > 0)
            .map((preset: any) => ({
                id: typeof preset.id === 'string' && preset.id.trim() ? preset.id : makeId('preset'),
                name: String(preset.name).trim(),
                entries: Array.isArray(preset.entries)
                    ? preset.entries
                        .filter((entry: any) => {
                            const entryTerrorName = typeof entry?.terror_name === 'string' ? entry.terror_name.trim() : '';
                            const entryRoundKey = typeof entry?.round_key === 'string' ? entry.round_key.trim() : '';
                            return hasScopedTarget(entryTerrorName, entryRoundKey);
                        })
                        .map((entry: any) => ({
                            terror_name: normalizeTargetInput(typeof entry.terror_name === 'string' ? entry.terror_name : ''),
                            round_key: normalizeTargetInput(typeof entry.round_key === 'string' ? entry.round_key : ''),
                        }))
                    : [],
                created_at: typeof preset.created_at === 'string' ? preset.created_at : undefined,
            }))
        : [];

    return {
        entries,
        presets,
        skip_all_without_survival_wish: Boolean(raw?.skip_all_without_survival_wish),
        updated_at: typeof raw?.updated_at === 'string' ? raw.updated_at : undefined,
    };
}

export const CoordinatedAutoSuicidePanel: React.FC = () => {
    const { client, currentInstance, pushToast, touchSyncTime } = useAppStore();
    const [state, setState] = useState<CoordinatedState>(emptyState);
    const [terrorName, setTerrorName] = useState('');
    const [roundKey, setRoundKey] = useState('');
    const [presetName, setPresetName] = useState('');
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const instanceId = currentInstance?.instance_id || '';

    const loadState = async () => {
        if (!client || !instanceId) {
            return;
        }

        setLoading(true);
        setError(null);
        try {
            const result = await client.getCoordinatedAutoSuicideState(instanceId);
            setState(normalizeState(result));
            touchSyncTime();
        } catch (loadError) {
            console.error('Failed to load coordinated auto-suicide state:', loadError);
            setError('統率自動自殺リストの読み込みに失敗しました');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        if (!client || !instanceId) {
            setState(emptyState);
            return;
        }

        loadState();
    }, [client, instanceId]);

    useEffect(() => {
        if (!client || !instanceId) {
            return;
        }

        const unsubscribe = client.onCoordinatedAutoSuicideUpdated((data) => {
            const targetInstanceId = typeof data?.instance_id === 'string' ? data.instance_id : instanceId;
            if (targetInstanceId !== instanceId) {
                return;
            }
            setState(normalizeState(data?.state ?? data));
            touchSyncTime();
        });

        return () => {
            unsubscribe();
        };
    }, [client, instanceId, touchSyncTime]);

    const persistState = async (nextState: CoordinatedState, successMessage?: string) => {
        if (!client || !instanceId) {
            return;
        }

        setLoading(true);
        setError(null);
        try {
            const updated = await client.updateCoordinatedAutoSuicideState(instanceId, nextState);
            setState(normalizeState(updated));
            touchSyncTime();
            if (successMessage) {
                pushToast({ type: 'success', message: successMessage });
            }
        } catch (saveError) {
            console.error('Failed to update coordinated auto-suicide state:', saveError);
            setError('統率自動自殺リストの保存に失敗しました');
            pushToast({ type: 'error', message: '統率自動自殺リストの保存に失敗しました。' });
        } finally {
            setLoading(false);
        }
    };

    const duplicateKeySet = useMemo(() => new Set(state.entries.map((entry) => normalizeKey(entry.terror_name, entry.round_key))), [state.entries]);

    const handleAddEntry = async () => {
        const trimmedTerrorName = normalizeTargetInput(terrorName);
        const trimmedRoundKey = normalizeTargetInput(roundKey);

        if (!hasScopedTarget(trimmedTerrorName, trimmedRoundKey)) {
            setError('テラー名かラウンド名のどちらかを入力してください');
            return;
        }

        if (duplicateKeySet.has(normalizeKey(trimmedTerrorName, trimmedRoundKey))) {
            setError('同じラウンドとテラーの組み合わせはすでに登録されています');
            return;
        }

        const nextEntry: CoordinatedEntry = {
            id: makeId('entry'),
            terror_name: trimmedTerrorName,
            round_key: trimmedRoundKey,
            created_at: new Date().toISOString(),
            source: 'manual',
        };

        await persistState({
            ...state,
            entries: [...state.entries, nextEntry],
        }, '統率自動自殺リストに追加しました。');
        setTerrorName('');
        setRoundKey('');
    };

    const handleRemoveEntry = async (entryId: string) => {
        await persistState({
            ...state,
            entries: state.entries.filter((entry) => entry.id !== entryId),
        }, '統率自動自殺リストを更新しました。');
    };

    const handleToggleSkipAll = async (checked: boolean) => {
        await persistState({
            ...state,
            skip_all_without_survival_wish: checked,
        }, checked ? '生存希望なし全スキップモードを有効化しました。' : '生存希望なし全スキップモードを無効化しました。');
    };

    const handleSavePreset = async () => {
        const trimmedName = presetName.trim();
        if (!trimmedName) {
            setError('プリセット名を入力してください');
            return;
        }

        const presetEntries = state.entries.map((entry) => ({
            terror_name: entry.terror_name,
            round_key: entry.round_key,
        }));

        const nextPreset: CoordinatedPreset = {
            id: makeId('preset'),
            name: trimmedName,
            entries: presetEntries,
            created_at: new Date().toISOString(),
        };

        await persistState({
            ...state,
            presets: [...state.presets, nextPreset],
        }, '現在のリストをプリセット保存しました。');
        setPresetName('');
    };

    const handleApplyPreset = async (presetId: string) => {
        const preset = state.presets.find((candidate) => candidate.id === presetId);
        if (!preset) {
            return;
        }

        const nextEntries = preset.entries.map((entry) => ({
            id: makeId('entry'),
            terror_name: entry.terror_name,
            round_key: entry.round_key,
            created_at: new Date().toISOString(),
            source: 'manual' as const,
        }));

        await persistState({
            ...state,
            entries: nextEntries,
        }, `プリセット「${preset.name}」を適用しました。`);
    };

    const handleDeletePreset = async (presetId: string) => {
        await persistState({
            ...state,
            presets: state.presets.filter((preset) => preset.id !== presetId),
        }, 'プリセットを削除しました。');
    };

    return (
        <section className="coordinated-auto-suicide-panel">
            <div className="selection-header">
                <div>
                    <h2>統率自動自殺リスト</h2>
                    <p className="description">このインスタンスだけで共有されるスキップ対象です。投票で Skip になったラウンドもここに追加されます。</p>
                </div>
                <button type="button" className="btn-start-voting" onClick={() => loadState()} disabled={loading || !instanceId}>
                    {loading ? '同期中...' : '再読込'}
                </button>
            </div>

            {error && (
                <div className="error-message">
                    {error}
                    <button onClick={() => setError(null)} className="btn-dismiss" type="button">×</button>
                </div>
            )}

            {!instanceId ? (
                <p className="empty-message">インスタンス参加中のみ編集できます。</p>
            ) : (
                <>
                    <div className="add-section coordinated-grid">
                        <label className="field-block">
                            <span>テラー名</span>
                            <input
                                className="input-search"
                                value={terrorName}
                                onChange={(event) => setTerrorName(event.target.value)}
                                placeholder="例: Ao Oni / 空欄なら全テラー"
                            />
                        </label>
                        <label className="field-block">
                            <span>ラウンド名</span>
                            <input
                                className="input-round-key"
                                value={roundKey}
                                onChange={(event) => setRoundKey(event.target.value)}
                                placeholder="空欄なら全ラウンド共通"
                            />
                        </label>
                        <button type="button" className="btn-start-voting" onClick={handleAddEntry} disabled={loading}>
                            追加
                        </button>
                    </div>

                    <div className="list-section">
                        <label className="toggle-row" htmlFor="skip-all-without-wish">
                            <input
                                id="skip-all-without-wish"
                                type="checkbox"
                                checked={state.skip_all_without_survival_wish}
                                onChange={(event) => handleToggleSkipAll(event.target.checked)}
                                disabled={loading}
                            />
                            <span>生存希望なし全スキップモード</span>
                        </label>
                        <p className="description">有効時、統率自動自殺がオンのクライアントは生存希望が無いテラー取得後 1 秒で自動自殺します。</p>
                    </div>

                    <div className="list-section">
                        <h3>共有リスト</h3>
                        {state.entries.length === 0 ? (
                            <p className="empty-message">登録はまだありません。</p>
                        ) : (
                            <table className="coordinated-table">
                                <thead>
                                    <tr>
                                        <th>テラー</th>
                                        <th>ラウンド</th>
                                        <th>由来</th>
                                        <th>操作</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {state.entries.map((entry) => (
                                        <tr key={entry.id}>
                                            <td>{entry.terror_name || '全テラー'}</td>
                                            <td>{entry.round_key || '全ラウンド'}</td>
                                            <td>{entry.source === 'vote' ? 'Vote' : 'Manual'}</td>
                                            <td>
                                                <button type="button" className="btn-inline-danger" onClick={() => handleRemoveEntry(entry.id)} disabled={loading}>
                                                    削除
                                                </button>
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        )}
                    </div>

                    <div className="list-section coordinated-grid presets-grid">
                        <div>
                            <h3>プリセット保存</h3>
                            <label className="field-block">
                                <span>プリセット名</span>
                                <input
                                    className="input-search"
                                    value={presetName}
                                    onChange={(event) => setPresetName(event.target.value)}
                                    placeholder="例: 深夜固定編成"
                                />
                            </label>
                            <button type="button" className="btn-start-voting" onClick={handleSavePreset} disabled={loading || state.entries.length === 0}>
                                現在のリストを保存
                            </button>
                        </div>

                        <div>
                            <h3>保存済みプリセット</h3>
                            {state.presets.length === 0 ? (
                                <p className="empty-message">プリセットはまだありません。</p>
                            ) : (
                                <div className="preset-list">
                                    {state.presets.map((preset) => (
                                        <div key={preset.id} className="preset-card">
                                            <div>
                                                <strong>{preset.name}</strong>
                                                <p className="description">{preset.entries.length} 件</p>
                                            </div>
                                            <div className="list-actions">
                                                <button type="button" className="btn-start-voting" onClick={() => handleApplyPreset(preset.id)} disabled={loading}>
                                                    適用
                                                </button>
                                                <button type="button" className="btn-inline-danger" onClick={() => handleDeletePreset(preset.id)} disabled={loading}>
                                                    削除
                                                </button>
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </div>
                    </div>
                </>
            )}
        </section>
    );
};
