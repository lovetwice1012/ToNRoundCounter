namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Application-wide constants to avoid magic numbers
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Network-related constants
        /// </summary>
        public static class Network
        {
            /// <summary>
            /// Minimum port number for standard services (registered ports start at 1024)
            /// </summary>
            public const int MinimumPort = 1024;

            /// <summary>
            /// Minimum port number for proxy services (avoid well-known ports below 1025)
            /// </summary>
            public const int MinimumProxyPort = 1025;

            /// <summary>
            /// Maximum valid port number (16-bit unsigned integer max)
            /// </summary>
            public const int MaximumPort = 65535;

            /// <summary>
            /// WebSocket reconnection delay in milliseconds
            /// </summary>
            public const int WebSocketReconnectDelayMs = 300;

            /// <summary>
            /// Default timeout for waiting processes in milliseconds
            /// </summary>
            public const int ProcessWaitTimeoutMs = 5000;

            /// <summary>
            /// Default refresh/polling interval in milliseconds
            /// </summary>
            public const int DefaultRefreshIntervalMs = 2000;

            /// <summary>
            /// VPN/Proxy buffer size in bytes (16 KB)
            /// </summary>
            public const int VpnBufferSize = 16 * 1024;
        }

        /// <summary>
        /// UI-related constants
        /// </summary>
        public static class UI
        {
            /// <summary>
            /// Standard control width for input fields and columns
            /// </summary>
            public const int StandardControlWidth = 300;

            /// <summary>
            /// Minimum window width for main form
            /// </summary>
            public const int MinimumWindowWidth = 300;

            /// <summary>
            /// Minimum window height for main form
            /// </summary>
            public const int MinimumWindowHeight = 400;
        }

        /// <summary>
        /// Data conversion constants
        /// </summary>
        public static class Data
        {
            /// <summary>
            /// Bytes per kilobyte for binary calculations
            /// </summary>
            public const double BytesPerKilobyte = 1024.0;

            /// <summary>
            /// Bytes per megabyte for binary calculations
            /// </summary>
            public const double BytesPerMegabyte = 1024.0 * 1024.0;
        }

        /// <summary>
        /// Database-related constants
        /// </summary>
        public static class Database
        {
            /// <summary>
            /// SQLite busy timeout in milliseconds
            /// </summary>
            public const int SqliteBusyTimeoutMs = 5000;
        }

        /// <summary>
        /// Audio capture constants
        /// </summary>
        public static class Audio
        {
            /// <summary>
            /// WASAPI event wait timeout in milliseconds
            /// </summary>
            public const uint WasapiEventTimeoutMs = 2000;
        }
    }
}
