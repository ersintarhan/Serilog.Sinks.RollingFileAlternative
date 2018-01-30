using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;

namespace Serilog.Sinks.RollingFileAlternative.Sinks
{
    public class SizeRollingFileSink : ILogEventSink, IDisposable
    {
        private static readonly string ThisObjectName = typeof(RollingFileAlternativeSink).Name;
        private readonly CancellationTokenSource _cancelToken = new CancellationTokenSource();
        private readonly Encoding _encoding;
        private readonly long _fileSizeLimitBytes;
        private readonly ITextFormatter _formatter;
        private readonly BlockingCollection<LogEvent> _queue;
        private readonly TimeSpan? _retainedFileDurationLimit;
        private readonly TemplatedPathRoller _roller;
        private readonly object _syncRoot = new object();
        private RollingFileAlternativeSink _currentSink;
        private bool _disposed;

        public SizeRollingFileSink(string pathFormat, ITextFormatter formatter, long fileSizeLimitBytes,
            TimeSpan? retainedFileDurationLimit, Encoding encoding = null)
        {
            _roller = new TemplatedPathRoller(pathFormat);
            _formatter = formatter;
            _fileSizeLimitBytes = fileSizeLimitBytes;
            _encoding = encoding;
            _retainedFileDurationLimit = retainedFileDurationLimit;
            _currentSink = GetLatestSink();

            if (AsyncOptions.SupportAsync)
            {
                _queue = new BlockingCollection<LogEvent>(AsyncOptions.BufferSize);
                Task.Run((Action) ProcessQueue, _cancelToken.Token);
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed || _currentSink == null) return;
                _currentSink.Dispose();
                _currentSink = null;
                _disposed = true;
                _cancelToken.Cancel();
            }
        }


        /// <summary>
        ///     Emits a log event to this sink
        /// </summary>
        /// <param name="logEvent">The <see cref="T:Serilog.Events.LogEvent" /> to emit</param>
        /// <exception cref="T:System.ArgumentNullException"></exception>
        /// <exception cref="T:System.ObjectDisposedException"></exception>
        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null) throw new ArgumentNullException(nameof(logEvent));

            if (AsyncOptions.SupportAsync)
                _queue.Add(logEvent);
            else
                WriteToFile(logEvent);
        }

        private void WriteToFile(LogEvent logEvent)
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    throw new ObjectDisposedException(ThisObjectName, "The rolling file sink has been disposed");

                var resetSequence = _currentSink.LogFile.Date.Date != DateTime.UtcNow.Date;

                if (_currentSink.EnableLevelLogging && _currentSink.ActiveLogLevel != logEvent.Level)
                    _currentSink = NextSizeLimitedFileSink(resetSequence, logEvent.Level);

                if (_currentSink.SizeLimitReached || resetSequence)
                {
                    _currentSink = NextSizeLimitedFileSink(resetSequence, logEvent.Level);
                    ApplyRetentionPolicy();
                }

                _currentSink?.Emit(logEvent);
            }
        }

        private RollingFileAlternativeSink GetLatestSink()
        {
            EnsureDirectoryCreated(_roller.LogFileDirectory);

            var logFile = _roller.GetLatestOrNew();

            return new RollingFileAlternativeSink(
                _formatter,
                _roller,
                _fileSizeLimitBytes,
                logFile,
                _encoding);
        }

        private RollingFileAlternativeSink NextSizeLimitedFileSink(bool resetSequance = false,
            LogEventLevel? level = null)
        {
            if (resetSequance)
                _currentSink.LogFile.ResetSequance();

            var next = _currentSink.LogFile.Next(_roller, level);
            _currentSink.Dispose();

            return new RollingFileAlternativeSink(_formatter, _roller, _fileSizeLimitBytes, next, _encoding)
            {
                ActiveLogLevel = level
            };
        }

        private static void EnsureDirectoryCreated(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }

        private void ApplyRetentionPolicy()
        {
            if (!_retainedFileDurationLimit.HasValue) return;


            var toRemove = _roller.GetAllFiles()
                .Where(f => DateTime.UtcNow.Subtract(f.Date).TotalSeconds >
                            _retainedFileDurationLimit.Value.TotalSeconds)
                .Select(f => f.Filename)
                .ToList();

            foreach (var obsolete in toRemove)
            {
                var fullPath = Path.Combine(_roller.LogFileDirectory, obsolete);
                try
                {
                    File.Delete(fullPath);
                }
                catch (Exception ex)
                {
                    SelfLog.WriteLine("Error {0} while removing obsolete file {1}", ex, fullPath);
                }
            }
        }

        private void ProcessQueue()
        {
            try
            {
                RetryHelper.RetryHelper.Instance.Try(ProcessQueueWithRetry)
                    .WithMaxTryCount(AsyncOptions.MaxRetries)
                    .OnFailure(result => Log.Error(string.Format("Try failed. Got {0}", result)))
                    .UntilNoException();
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Error occured in processing queue, {0} thread: {1}", typeof(SizeRollingFileSink),
                    ex);
            }
        }

        private void ProcessQueueWithRetry()
        {
            try
            {
                while (true)
                {
                    var logEvent = _queue.Take(_cancelToken.Token);
                    WriteToFile(logEvent);
                }
            }
            catch
            {
                SelfLog.WriteLine("Error occured in ProcessQueueWithRetry, {0} ", typeof(SizeRollingFileSink));
                throw;
            }
        }
    }
}