using System;
using System.IO;
using System.Text;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;

namespace Serilog.Sinks.RollingFileAlternative.Sinks
{
    internal class RollingFileAlternativeSink : ILogEventSink, IDisposable
    {
        private static readonly string ThisObjectName = typeof(RollingFileAlternativeSink).Name;
        private readonly long _fileSizeLimitBytes;

        private readonly ITextFormatter _formatter;
        private readonly StreamWriter _output;
        private readonly TemplatedPathRoller _roller;
        private readonly object _syncRoot = new object();
        private bool _disposed;

        public RollingFileAlternativeSink(ITextFormatter formatter, TemplatedPathRoller roller, long fileSizeLimitBytes,
            Encoding encoding = null) : this(formatter, roller, fileSizeLimitBytes, roller.GetLatestOrNew(), encoding)
        {
            _formatter = formatter;
            _roller = roller;
            _fileSizeLimitBytes = fileSizeLimitBytes;
            EnableLevelLogging = roller.PathIncludesLevel;
            _output = OpenFileForWriting(roller.LogFileDirectory, roller.GetLatestOrNew(), encoding ?? Encoding.UTF8);
        }

        public RollingFileAlternativeSink(ITextFormatter formatter, TemplatedPathRoller roller, long fileSizeLimitBytes,
            RollingLogFile rollingLogFile, Encoding encoding = null)
        {
            _formatter = formatter;
            _roller = roller;
            _fileSizeLimitBytes = fileSizeLimitBytes;
            EnableLevelLogging = roller.PathIncludesLevel;
            _output = OpenFileForWriting(roller.LogFileDirectory, rollingLogFile, encoding ?? Encoding.UTF8);
        }

        internal bool EnableLevelLogging { get; }

        internal LogEventLevel? ActiveLogLevel { get; set; }

        internal bool SizeLimitReached { get; private set; }

        internal RollingLogFile LogFile { get; private set; }

        public void Dispose()
        {
            if (!_disposed)
            {
                _output.Flush();
                _output.Dispose();
                _disposed = true;
            }
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null) throw new ArgumentNullException(nameof(logEvent));

            lock (_syncRoot)
            {
                if (_disposed) throw new ObjectDisposedException(ThisObjectName, "Cannot write to disposed file");

                if (_output == null) return;

                _formatter.Format(logEvent, _output);
                _output.Flush();

                ActiveLogLevel = logEvent.Level;

                if (_output.BaseStream.Length > _fileSizeLimitBytes) SizeLimitReached = true;
            }
        }

        private StreamWriter OpenFileForWriting(string folderPath, RollingLogFile rollingLogFile, Encoding encoding)
        {
            EnsureDirectoryCreated(folderPath);
            try
            {
                LogFile = rollingLogFile;
                var fullPath = Path.Combine(folderPath, rollingLogFile.Filename);
                var stream = File.Open(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read);

                return new StreamWriter(stream, encoding ?? Encoding.UTF8);
            }
            catch (IOException ex)
            {
                SelfLog.WriteLine("Error {0} while opening obsolete file {1}", ex, rollingLogFile.Filename);

                return OpenFileForWriting(folderPath, rollingLogFile.Next(_roller), encoding);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Error {0} while opening obsolete file {1}", ex, rollingLogFile.Filename);
                throw;
            }
        }

        private static void EnsureDirectoryCreated(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
    }
}