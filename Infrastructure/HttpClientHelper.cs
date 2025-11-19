using System;
using System.Net.Http;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Provides a shared HttpClient instance to prevent socket exhaustion.
    /// HttpClient is designed to be reused and should not be created in using statements.
    /// </summary>
    public static class HttpClientHelper
    {
        /// <summary>
        /// Shared HttpClient instance for the entire application.
        /// Timeout is set to 30 seconds by default.
        /// </summary>
        private static readonly Lazy<HttpClient> _lazyClient = new Lazy<HttpClient>(() =>
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            return client;
        });

        /// <summary>
        /// Gets the shared HttpClient instance.
        /// </summary>
        public static HttpClient Client => _lazyClient.Value;
    }
}
