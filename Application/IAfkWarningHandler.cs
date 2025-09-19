using System.Threading;
using System.Threading.Tasks;

namespace ToNRoundCounter.Application
{
    /// <summary>
    /// Provides information related to an AFK warning trigger.
    /// </summary>
    public sealed class AfkWarningContext
    {
        public AfkWarningContext(double idleSeconds)
        {
            IdleSeconds = idleSeconds;
        }

        /// <summary>
        /// Gets the number of seconds the player has been idle when the warning fired.
        /// </summary>
        public double IdleSeconds { get; }
    }

    /// <summary>
    /// Represents a handler that can intercept the AFK warning behaviour.
    /// </summary>
    public interface IAfkWarningHandler
    {
        /// <summary>
        /// Handles the AFK warning.
        /// </summary>
        /// <param name="context">Contextual data about the warning.</param>
        /// <param name="cancellationToken">Token used to observe cancellation requests.</param>
        /// <returns><c>true</c> if the handler has processed the warning and the default behaviour should be skipped; otherwise, <c>false</c>.</returns>
        Task<bool> HandleAsync(AfkWarningContext context, CancellationToken cancellationToken);
    }
}
