import React, { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { createRestClient, type AppPrivilege } from '../lib/rest-client';
import { getApiBaseUrl } from '../lib/cloud-url';
import { useAppStore } from '../store/appStore';

const ADMIN_USER_ID = 'yussy5373';

export const AdminAppPrivileges: React.FC = () => {
    const navigate = useNavigate();
    const { sessionToken, restClient, userId, playerId, pushToast } = useAppStore();
    const [loading, setLoading] = useState(false);
    const [appId, setAppId] = useState('');
    const [description, setDescription] = useState('');
    const [selectedScopes, setSelectedScopes] = useState<string[]>([]);
    const [privilegedScopes, setPrivilegedScopes] = useState<string[]>([]);
    const [records, setRecords] = useState<AppPrivilege[]>([]);
    const [error, setError] = useState<string | null>(null);

    const isAdmin = userId === ADMIN_USER_ID || playerId === ADMIN_USER_ID;

    const client = useMemo(() => {
        if (restClient) {
            return restClient;
        }

        const apiUrl = getApiBaseUrl();
        return createRestClient(apiUrl, sessionToken || undefined);
    }, [restClient, sessionToken]);

    const load = async () => {
        if (!isAdmin) {
            return;
        }

        try {
            setLoading(true);
            setError(null);
            const result = await client.listAppPrivileges();
            setRecords(result.app_privileges);
            setPrivilegedScopes(result.privileged_scopes);
        } catch (err: any) {
            const message = err?.message || 'Failed to load app privileges';
            setError(message);
            pushToast({ type: 'error', message });
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        load();
    }, [isAdmin]);

    const toggleScope = (scope: string) => {
        setSelectedScopes((current) =>
            current.includes(scope)
                ? current.filter((item) => item !== scope)
                : [...current, scope]
        );
    };

    const editRecord = (record: AppPrivilege) => {
        setAppId(record.app_id);
        setDescription(record.description || '');
        setSelectedScopes(record.privileged_scopes || []);
    };

    const save = async () => {
        if (!appId.trim()) {
            setError('APPID is required');
            return;
        }

        try {
            setLoading(true);
            setError(null);
            await client.updateAppPrivilege(appId.trim(), selectedScopes, description.trim() || undefined);
            pushToast({ type: 'success', message: 'App privilege saved' });
            setAppId('');
            setDescription('');
            setSelectedScopes([]);
            await load();
        } catch (err: any) {
            const message = err?.message || 'Failed to save app privilege';
            setError(message);
            pushToast({ type: 'error', message });
        } finally {
            setLoading(false);
        }
    };

    const remove = async (targetAppId: string) => {
        try {
            setLoading(true);
            setError(null);
            await client.deleteAppPrivilege(targetAppId);
            pushToast({ type: 'success', message: 'App privilege deleted' });
            await load();
        } catch (err: any) {
            const message = err?.message || 'Failed to delete app privilege';
            setError(message);
            pushToast({ type: 'error', message });
        } finally {
            setLoading(false);
        }
    };

    if (!isAdmin) {
        return (
            <main className="admin-page">
                <section className="admin-panel">
                    <p className="eyebrow">Admin</p>
                    <h1>App Privileges</h1>
                    <div className="error-message">Administrator permission is required.</div>
                    <div className="admin-actions">
                        <button className="btn-secondary" type="button" onClick={() => navigate('/dashboard')}>
                            Dashboard
                        </button>
                    </div>
                </section>
            </main>
        );
    }

    return (
        <main className="admin-page">
            <section className="admin-panel">
                <div>
                    <p className="eyebrow">Admin</p>
                    <h1>App Privileges</h1>
                    <p className="admin-subtitle">Grant privileged Cloud write scopes to specific APPIDs.</p>
                </div>
                <div className="admin-page-actions">
                    <button className="btn-secondary" type="button" onClick={() => navigate('/dashboard')}>
                        Dashboard
                    </button>
                </div>

                {error && <div className="error-message">{error}</div>}

                <div className="admin-editor">
                    <label>
                        <span>APPID</span>
                        <input value={appId} onChange={(event) => setAppId(event.target.value)} placeholder="developer.app.id" />
                    </label>
                    <label>
                        <span>Description</span>
                        <textarea value={description} onChange={(event) => setDescription(event.target.value)} rows={3} />
                    </label>

                    <div className="admin-scope-grid">
                        {privilegedScopes.map((scope) => (
                            <label key={scope} className="admin-scope-option">
                                <input
                                    type="checkbox"
                                    checked={selectedScopes.includes(scope)}
                                    onChange={() => toggleScope(scope)}
                                />
                                <span>{scope}</span>
                            </label>
                        ))}
                    </div>

                    <div className="admin-actions">
                        <button className="btn-secondary" type="button" onClick={() => {
                            setAppId('');
                            setDescription('');
                            setSelectedScopes([]);
                        }}>
                            Clear
                        </button>
                        <button className="btn-primary" type="button" onClick={save} disabled={loading}>
                            Save
                        </button>
                    </div>
                </div>

                <div className="admin-records">
                    {records.map((record) => (
                        <article key={record.app_id} className="admin-record">
                            <div>
                                <strong>{record.app_id}</strong>
                                {record.description && <p>{record.description}</p>}
                                <div className="admin-scope-list">
                                    {record.privileged_scopes.map((scope) => <code key={scope}>{scope}</code>)}
                                </div>
                            </div>
                            <div className="admin-record-actions">
                                <button className="btn-secondary" type="button" onClick={() => editRecord(record)}>Edit</button>
                                <button className="btn-secondary danger" type="button" onClick={() => remove(record.app_id)}>Delete</button>
                            </div>
                        </article>
                    ))}
                    {records.length === 0 && <p className="admin-empty">No privileged apps registered.</p>}
                </div>
            </section>
        </main>
    );
};
