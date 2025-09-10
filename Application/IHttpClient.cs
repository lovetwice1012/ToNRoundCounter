using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Abstraction for HTTP communication to enable testability and external API integration.
    /// </summary>
    public interface IHttpClient
    {
        Task<HttpResponseMessage> PostAsync(string url, HttpContent content, CancellationToken cancellationToken);
    }
}
