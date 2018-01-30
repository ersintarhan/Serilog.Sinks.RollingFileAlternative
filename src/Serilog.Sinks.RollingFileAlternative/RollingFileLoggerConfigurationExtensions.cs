using System;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Sinks.RollingFileAlternative.Sinks;

namespace Serilog.Sinks.RollingFileAlternative
{
    public static class RollingFileLoggerConfigurationExtensions
    {
        private const string DefaultretainedFileDurationLimit = "7.00:00:00";
        private const long DefaultFileSizeLimitBytes = 1L * 1024 * 1024 * 1024;

        private const string DefaultOutputTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {Message}{NewLine}{Exception}";

        /// <summary>
        ///     Write log events to a series of files. Each file will be named according to
        ///     the date of the first log entry written to it. Only simple date-based rolling is
        ///     currently supported.
        /// </summary>
        /// <param name="sinkConfiguration">Logger sink configuration.</param>
        /// <param name="pathFormat">
        ///     String describing the location of the log files,
        ///     with {Date} in the place of the file date. E.g. "Logs\myapp-{Date}.log" will result in log
        ///     files such as "Logs\myapp-2013-10-20.log", "Logs\myapp-2013-10-21.log" and so on.
        /// </param>
        /// <param name="restrictedToMinimumLevel">
        ///     The minimum level for
        ///     events passed through the sink. Ignored when is specified.
        /// </param>
        /// <param name="outputTemplate">
        ///     A message template describing the format used to write to the sink.
        ///     the default is "{Timestamp} [{Level}] {Message}{NewLine}{Exception}".
        /// </param>
        /// <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
        /// <param name="fileSizeLimitBytes">
        ///     The maximum size, in bytes, to which any single log file will be allowed to grow.
        ///     For unrestricted growth, pass null. The default is 1 GB.
        /// </param>
        /// <param name="retainedFileDurationLimit">
        ///     The maximum timespan of log files that will be retained,
        ///     including the current log file. The value should folowing TimeSpan format (d.hh.mm.ss).The default is 7 days
        /// </param>
        /// <param name="supportAsync">Async Write Support</param>
        /// <param name="bufferSize">Buffer Size</param>
        /// <param name="maxRetries">Max Retries</param>
        /// <returns>Configuration object allowing method chaining.</returns>
        /// <remarks>The file will be written using the UTF-8 character set.</remarks>
        public static LoggerConfiguration RollingFile(this LoggerSinkConfiguration sinkConfiguration, string pathFormat,
            TimeSpan? retainedFileDurationLimit = null,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum, string outputTemplate = DefaultOutputTemplate,
            IFormatProvider formatProvider = null, long? fileSizeLimitBytes = null, bool supportAsync = false,
            int? bufferSize = null, int? maxRetries = null)
        {
            var templateFormatter = new MessageTemplateTextFormatter(outputTemplate, formatProvider);

            return RollingFile(sinkConfiguration, templateFormatter, pathFormat, retainedFileDurationLimit,
                restrictedToMinimumLevel, outputTemplate, formatProvider, fileSizeLimitBytes, supportAsync, bufferSize,
                maxRetries);
        }

        public static LoggerConfiguration RollingFile(this LoggerSinkConfiguration sinkConfiguration,
            ITextFormatter formatter, string pathFormat, TimeSpan? retainedFileDurationLimit = null,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum, string outputTemplate = DefaultOutputTemplate,
            IFormatProvider formatProvider = null, long? fileSizeLimitBytes = null, bool supportAsync = false,
            int? bufferSize = null, int? maxRetries = null)
        {
            if (sinkConfiguration == null) throw new ArgumentNullException(nameof(sinkConfiguration));

            AsyncOptions.SupportAsync = supportAsync;
            AsyncOptions.MaxRetries = maxRetries ?? AsyncOptions.MaxRetries;
            AsyncOptions.BufferSize = bufferSize ?? AsyncOptions.BufferSize;

            var sink = new SizeRollingFileSink(pathFormat, formatter, fileSizeLimitBytes ?? DefaultFileSizeLimitBytes,
                retainedFileDurationLimit ?? TimeSpan.Parse(DefaultretainedFileDurationLimit));

            return sinkConfiguration.Sink(sink, restrictedToMinimumLevel);
        }
    }
}