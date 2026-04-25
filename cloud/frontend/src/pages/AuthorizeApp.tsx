/**
 * External app authorization page.
 */

import React, { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { createRestClient } from '../lib/rest-client';
import { getApiBaseUrl } from '../lib/cloud-url';
import { useAppStore } from '../store/appStore';

function buildDeniedRedirect(redirectUri: string, state?: string | null): string {
    const callback = new URL(redirectUri);
    callback.searchParams.set('error', 'access_denied');
    if (state) {
        callback.searchParams.set('state', state);
    }

    return callback.toString();
}

export const AuthorizeApp: React.FC = () => {
    const [params] = useSearchParams();
    const { sessionToken, restClient, playerId, pushToast } = useAppStore();
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const appId = params.get('app_id')?.trim() || '';
    const appName = params.get('app_name')?.trim() || appId || '外部アプリ';
    const redirectUri = params.get('redirect_uri')?.trim() || '';
    const state = params.get('state');
    const requestedScopes = useMemo(() => {
        const scopeParam = params.get('scope') || params.get('scopes') || '';
        return scopeParam
            .split(/[\s,]+/)
            .map((scope) => scope.trim())
            .filter(Boolean);
    }, [params]);

    const redirectPreview = useMemo(() => {
        if (!redirectUri) {
            return '';
        }

        try {
            const parsed = new URL(redirectUri);
            return `${parsed.protocol}//${parsed.host || parsed.pathname}`;
        } catch {
            return redirectUri;
        }
    }, [redirectUri]);

    const invalidRequest = !appId || !redirectUri;

    const getRestClient = () => {
        if (restClient) {
            return restClient;
        }

        const apiUrl = getApiBaseUrl();
        return createRestClient(apiUrl, sessionToken || undefined);
    };

    const approve = async () => {
        if (invalidRequest) {
            setError('APPID または callback URL が不足しています。');
            return;
        }

        try {
            setLoading(true);
            setError(null);

            const result = await getRestClient().authorizeExternalApp({
                app_id: appId,
                redirect_uri: redirectUri,
                state: state || undefined,
                scopes: requestedScopes,
            });

            window.location.assign(result.redirect_uri);
        } catch (err: any) {
            const message = err?.message || 'アプリの許可に失敗しました。';
            setError(message);
            pushToast({ type: 'error', message });
        } finally {
            setLoading(false);
        }
    };

    const deny = () => {
        try {
            window.location.assign(buildDeniedRedirect(redirectUri, state));
        } catch {
            setError('callback URL が不正なため、拒否結果を返せません。');
        }
    };

    return (
        <div className="authorize-page">
            <section className="authorize-panel">
                <p className="eyebrow">App Authorization</p>
                <h1>外部アプリへのアクセスを許可しますか？</h1>

                <div className="authorize-summary">
                    <div>
                        <span>アプリ</span>
                        <strong>{appName}</strong>
                    </div>
                    <div>
                        <span>APPID</span>
                        <code>{appId || '未指定'}</code>
                    </div>
                    <div>
                        <span>ユーザー</span>
                        <strong>{playerId || 'ログイン中のユーザー'}</strong>
                    </div>
                    <div>
                        <span>callback</span>
                        <code>{redirectPreview || '未指定'}</code>
                    </div>
                </div>

                <div className="authorize-scope">
                    <h2>許可されること</h2>
                    {requestedScopes.length > 0 && (
                        <div className="authorize-scope-list" aria-label="要求されたスコープ">
                            {requestedScopes.map((scope) => (
                                <code key={scope}>{scope}</code>
                            ))}
                        </div>
                    )}
                    <ul>
                        <li>このアプリ専用の APPToken を発行する</li>
                        <li>発行された APPToken で ToNRoundCounter Cloud SDK 通信を行う</li>
                        <li>許可を取り消すと、その APPToken での通信を止める</li>
                    </ul>
                </div>

                {error && <div className="error-message">{error}</div>}

                <div className="authorize-actions">
                    <button className="btn-secondary" type="button" onClick={deny} disabled={loading || !redirectUri}>
                        拒否
                    </button>
                    <button className="btn-primary" type="button" onClick={approve} disabled={loading || invalidRequest}>
                        {loading ? '許可中...' : '許可して戻る'}
                    </button>
                </div>
            </section>
        </div>
    );
};
