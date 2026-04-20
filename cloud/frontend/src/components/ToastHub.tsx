import React, { useEffect } from 'react';
import { useAppStore } from '../store/appStore';

export const ToastHub: React.FC = () => {
    const { toasts, removeToast } = useAppStore();

    useEffect(() => {
        if (toasts.length === 0) {
            return;
        }

        const timers = toasts.map((toast) => setTimeout(() => removeToast(toast.id), 3500));
        return () => {
            timers.forEach((timer) => clearTimeout(timer));
        };
    }, [toasts, removeToast]);

    return (
        <div className="toast-hub" aria-live="polite" aria-atomic="true">
            {toasts.map((toast) => (
                <div key={toast.id} className={`toast-item toast-${toast.type}`}>
                    <span>{toast.message}</span>
                    <button type="button" onClick={() => removeToast(toast.id)} className="toast-close" aria-label="close">
                        x
                    </button>
                </div>
            ))}
        </div>
    );
};
