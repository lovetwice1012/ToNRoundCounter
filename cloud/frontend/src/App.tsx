/**
 * Main App Component
 */

import React, { Suspense, lazy } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { useAppStore } from './store/appStore';
import { ToastHub } from './components/ToastHub';
import './App.css';

const Login = lazy(() => import('./pages/Login').then((m) => ({ default: m.Login })));
const Dashboard = lazy(() => import('./pages/Dashboard').then((m) => ({ default: m.Dashboard })));

const ProtectedRoute: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const { sessionToken } = useAppStore();
    
    if (!sessionToken) {
        return <Navigate to="/login" replace />;
    }
    
    return <>{children}</>;
};

const App: React.FC = () => {
    return (
        <BrowserRouter>
            <Suspense fallback={<div className="route-loading">Loading dashboard...</div>}>
                <Routes>
                    <Route path="/login" element={<Login />} />
                    <Route
                        path="/dashboard"
                        element={
                            <ProtectedRoute>
                                <Dashboard />
                            </ProtectedRoute>
                        }
                    />
                    <Route path="/" element={<Navigate to="/dashboard" replace />} />
                </Routes>
            </Suspense>
            <ToastHub />
        </BrowserRouter>
    );
};

export default App;
