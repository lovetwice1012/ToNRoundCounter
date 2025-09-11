using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Default implementation of <see cref="IHttpClient"/> using <see cref="HttpClient"/>.
    /// </summary>
    public class HttpClientWrapper : IHttpClient, IDisposable
    {
        private readonly HttpClient _client = new HttpClient();

        public Task<HttpResponseMessage> PostAsync(string url, HttpContent content, CancellationToken cancellationToken)
            => _client.PostAsync(url, content, cancellationToken);

        public void Dispose() => _client.Dispose();
    }
}
