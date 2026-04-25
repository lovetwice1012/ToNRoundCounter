using System;
using System.Collections.Generic;
using System.Linq;
using ToNRoundCounter.Application;
using ToNRoundCounter.Application.Services;

namespace ToNRoundCounter.Infrastructure.Services
{
    /// <summary>
    /// Default implementation of <see cref="IModuleSoundApi"/>. Thin facade over
    /// <see cref="ISoundManager"/> that exposes only the operations safe for module use.
    /// </summary>
    public sealed class ModuleSoundApi : IModuleSoundApi
    {
        private readonly ISoundManager _soundManager;
        private readonly IAppSettings _settings;

        public ModuleSoundApi(ISoundManager soundManager, IAppSettings settings)
        {
            _soundManager = soundManager ?? throw new ArgumentNullException(nameof(soundManager));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public bool IsMasterMuted => _settings.MasterMuted;

        public double GetCurrentMasterVolume()
        {
            if (_settings.MasterMuted) return 0.0;
            double v = _settings.MasterVolume;
            if (double.IsNaN(v) || v < 0) return 0.0;
            if (v > 1.0) return 1.0;
            return v;
        }

        public IDisposable Play(string pathOrUrl, double volume = 1.0, bool loop = false)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
            {
                return EmptyDisposable.Instance;
            }
            return _soundManager.PlayCustomSound(new[] { pathOrUrl }, volume, loop);
        }

        public IDisposable PlayPlaylist(IEnumerable<string> pathsOrUrls, double volume = 1.0, bool loop = false)
        {
            if (pathsOrUrls == null)
            {
                return EmptyDisposable.Instance;
            }
            var list = pathsOrUrls.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (list.Count == 0)
            {
                return EmptyDisposable.Instance;
            }
            return _soundManager.PlayCustomSound(list, volume, loop);
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new EmptyDisposable();
            public void Dispose() { }
        }
    }
}
