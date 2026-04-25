/**
 * Main App Component
 */

import React, { Suspense, lazy } from 'react';
import { BrowserRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom';
import { useAppStore } from './store/appStore';
import { ToastHub } from './components/ToastHub';
import './App.css';

const Login = lazy(() => import('./pages/Login').then((m) => ({ default: m.Login })));
const Dashboard = lazy(() => import('./pages/Dashboard').then((m) => ({ default: m.Dashboard })));
const AuthorizeApp = lazy(() => import('./pages/AuthorizeApp').then((m) => ({ default: m.AuthorizeApp })));
const AdminAppPrivileges = lazy(() => import('./pages/AdminAppPrivileges').then((m) => ({ default: m.AdminAppPrivileges })));

const ProtectedRoute: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const { sessionToken } = useAppStore();
    const location = useLocation();
    
    if (!sessionToken) {
        sessionStorage.setItem('tonround-cloud-return-to', `${location.pathname}${location.search}`);
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
                    <Route
                        path="/authorize-app"
                        element={
                            <ProtectedRoute>
                                <AuthorizeApp />
                            </ProtectedRoute>
                        }
                    />
                    <Route
                        path="/admin/app-privileges"
                        element={
                            <ProtectedRoute>
                                <AdminAppPrivileges />
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
