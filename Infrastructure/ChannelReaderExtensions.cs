using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Extension methods for <see cref="ChannelReader{T}"/> to support async enumeration on .NET Framework.
    /// </summary>
    public static class ChannelReaderExtensions
    {
        public static async IAsyncEnumerable<T> ReadAllAsync<T>(this ChannelReader<T> reader, CancellationToken cancellationToken = default)
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }
    }
}
