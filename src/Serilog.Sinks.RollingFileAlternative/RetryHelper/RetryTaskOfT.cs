﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace Serilog.Sinks.RollingFileAlternative.RetryHelper
{
    /// <summary>
    ///     Represents the task to be retried.
    /// </summary>
    /// <typeparam name="T">The type of result returned by the retried delegate.</typeparam>
    public class RetryTask<T>
    {
        protected readonly Func<T> Task;
        protected Func<T, bool> EndCondition;

        protected Type ExpectedExceptionType = typeof(Exception);
        protected Exception LastException;

        protected int MaxTryCount;
        protected TimeSpan MaxTryTime;
        protected Action<T, int> OnFailureAction = (result, tryCount) => { };
        protected Action<T, int> OnSuccessAction = (result, tryCount) => { };

        protected Action<T, int> OnTimeoutAction = (result, tryCount) => { };
        protected bool RetryOnException;

        protected Stopwatch Stopwatch;
        protected string TimeoutErrorMsg;
        protected TraceSource TraceSource;
        protected int TriedCount;
        protected TimeSpan TryInterval;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RetryTask&lt;T&gt;" /> class.
        /// </summary>
        /// <param name="task">The task.</param>
        /// <param name="traceSource">The trace source.</param>
        public RetryTask(Func<T> task, TraceSource traceSource)
            : this(task, traceSource, RetryTask.DefaultMaxTryTime, RetryTask.DefaultMaxTryCount,
                RetryTask.DefaultTryInterval)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RetryTask&lt;T&gt;" /> class.
        /// </summary>
        /// <param name="task">The task.</param>
        /// <param name="traceSource">The trace source.</param>
        /// <param name="maxTryTime">The max try time.</param>
        /// <param name="maxTryCount">The max try count.</param>
        /// <param name="tryInterval">The try interval.</param>
        public RetryTask(Func<T> task, TraceSource traceSource,
            TimeSpan maxTryTime, int maxTryCount, TimeSpan tryInterval)
        {
            Task = task;
            TraceSource = traceSource;
            MaxTryTime = maxTryTime;
            MaxTryCount = maxTryCount;
            TryInterval = tryInterval;
        }

        /// <summary>
        ///     Retries the task until the specified end condition is satisfied,
        ///     or the max try time/count is exceeded, or an exception is thrown druing task execution.
        ///     Then returns the value returned by the task.
        /// </summary>
        /// <param name="endCondition">The end condition.</param>
        /// <returns></returns>
        [DebuggerNonUserCode]
        public T Until(Func<T, bool> endCondition)
        {
            EndCondition = endCondition;
            return TryImpl();
        }

        /// <summary>
        ///     Retries the task until the specified end condition is satisfied,
        ///     or the max try time/count is exceeded, or an exception is thrown druing task execution.
        ///     Then returns the value returned by the task.
        /// </summary>
        /// <param name="endCondition">The end condition.</param>
        /// <returns></returns>
        [DebuggerNonUserCode]
        public T Until(Func<bool> endCondition)
        {
            EndCondition = t => endCondition();
            return TryImpl();
        }

        /// <summary>
        ///     Retries the task until no exception is thrown during the task execution.
        /// </summary>
        /// <returns></returns>
        [DebuggerNonUserCode]
        public T UntilNoException()
        {
            RetryOnException = true;
            EndCondition = t => true;
            return TryImpl();
        }

        /// <summary>
        ///     Retries the task until the specified exception is not thrown during the task execution.
        ///     Any other exception thrown is re-thrown.
        /// </summary>
        /// <returns></returns>
        [DebuggerNonUserCode]
        public T UntilNoException<TException>()
        {
            ExpectedExceptionType = typeof(TException);
            return UntilNoException();
        }

        /// <summary>
        ///     Configures the max try time limit in milliseconds.
        /// </summary>
        /// <param name="milliseconds">The max try time limit in milliseconds.</param>
        /// <returns></returns>
        public RetryTask<T> WithTimeLimit(int milliseconds)
        {
            return WithTimeLimit(TimeSpan.FromMilliseconds(milliseconds));
        }

        /// <summary>
        ///     Configures the max try time limit.
        /// </summary>
        /// <param name="maxTryTime">The max try time limit.</param>
        /// <returns></returns>
        public RetryTask<T> WithTimeLimit(TimeSpan maxTryTime)
        {
            var retryTask = Clone();
            retryTask.MaxTryTime = maxTryTime;
            return retryTask;
        }

        /// <summary>
        ///     Configures the try interval time in milliseconds.
        /// </summary>
        /// <param name="milliseconds">The try interval time in milliseconds.</param>
        /// <returns></returns>
        public RetryTask<T> WithTryInterval(int milliseconds)
        {
            return WithTryInterval(TimeSpan.FromMilliseconds(milliseconds));
        }

        /// <summary>
        ///     Configures the try interval time.
        /// </summary>
        /// <param name="tryInterval">The try interval time.</param>
        /// <returns></returns>
        public RetryTask<T> WithTryInterval(TimeSpan tryInterval)
        {
            var retryTask = Clone();
            retryTask.TryInterval = tryInterval;
            return retryTask;
        }

        /// <summary>
        ///     Configures the max try count limit.
        /// </summary>
        /// <param name="maxTryCount">The max try count.</param>
        /// <returns></returns>
        public RetryTask<T> WithMaxTryCount(int maxTryCount)
        {
            var retryTask = Clone();
            retryTask.MaxTryCount = maxTryCount;
            return retryTask;
        }

        /// <summary>
        ///     Configures the action to take when the try action timed out before success.
        ///     The result of the last failed attempt is passed as parameter to the action.
        ///     For <see cref="UntilNoException" />, the parameter passed to the action
        ///     is always <c>default(T)</c>
        /// </summary>
        /// <param name="timeoutAction">The action to take on timeout.</param>
        /// <returns></returns>
        public RetryTask<T> OnTimeout(Action<T> timeoutAction)
        {
            var retryTask = Clone();
            retryTask.OnTimeoutAction += (result, tryCount) => timeoutAction(result);
            return retryTask;
        }

        /// <summary>
        ///     Configures the action to take when the try action timed out before success.
        ///     The result of the last failed attempt and the total count of attempts
        ///     are passed as parameters to the action.
        ///     For <see cref="UntilNoException" />, the parameter passed to the action
        ///     is always <c>default(T)</c>
        /// </summary>
        /// <param name="timeoutAction">The action to take on timeout.</param>
        /// <returns></returns>
        public RetryTask<T> OnTimeout(Action<T, int> timeoutAction)
        {
            var retryTask = Clone();
            retryTask.OnTimeoutAction += timeoutAction;
            return retryTask;
        }

        /// <summary>
        ///     Configures the action to take after each time the try action fails and before the next try.
        ///     The result of the failed try action will be passed as parameter to the action.
        /// </summary>
        /// <param name="failureAction">The action to take on failure.</param>
        /// <returns></returns>
        public RetryTask<T> OnFailure(Action<T> failureAction)
        {
            var retryTask = Clone();
            retryTask.OnFailureAction += (result, tryCount) => failureAction(result);
            return retryTask;
        }

        /// <summary>
        ///     Configures the action to take after each time the try action fails and before the next try.
        ///     The result of the failed try action and the total count of attempts that
        ///     have been performed are passed as parameters to the action.
        /// </summary>
        /// <param name="failureAction">The action to take on failure.</param>
        /// <returns></returns>
        public RetryTask<T> OnFailure(Action<T, int> failureAction)
        {
            var retryTask = Clone();
            retryTask.OnFailureAction += failureAction;
            return retryTask;
        }

        /// <summary>
        ///     Configures the action to take when the try action succeeds.
        ///     The result of the successful attempt is passed as parameter to the action.
        /// </summary>
        /// <param name="successAction">The action to take on success.</param>
        /// <returns></returns>
        public RetryTask<T> OnSuccess(Action<T> successAction)
        {
            var retryTask = Clone();
            retryTask.OnSuccessAction += (result, tryCount) => successAction(result);
            return retryTask;
        }

        /// <summary>
        ///     Configures the action to take when the try action succeeds.
        ///     The result of the successful attempt and the total count of attempts
        ///     are passed as parameters to the action. This count includes the
        ///     final successful one.
        /// </summary>
        /// <param name="successAction">The action to take on success.</param>
        /// <returns></returns>
        public RetryTask<T> OnSuccess(Action<T, int> successAction)
        {
            var retryTask = Clone();
            retryTask.OnSuccessAction += successAction;
            return retryTask;
        }

        /// <summary>
        ///     Clones this instance.
        /// </summary>
        /// <returns></returns>
        protected virtual RetryTask<T> Clone()
        {
            return new RetryTask<T>(Task, TraceSource, MaxTryTime, MaxTryCount, TryInterval)
            {
                OnTimeoutAction = OnTimeoutAction,
                OnSuccessAction = OnSuccessAction
            };
        }

        #region Private methods

        private T TryImpl()
        {
            TraceSource.TraceVerbose("Starting trying with max try time {0} and max try count {1}.",
                MaxTryTime, MaxTryCount);
            TriedCount = 0;
            Stopwatch = Stopwatch.StartNew();

            // Start the try loop.
            T result;
            do
            {
                TraceSource.TraceVerbose("Trying time {0}, elapsed time {1}.", TriedCount, Stopwatch.Elapsed);
                result = default(T);

                try
                {
                    // Perform the try action.
                    result = Task();
                }
                catch (Exception ex)
                {
                    if (ShouldThrow(ex)) throw;
                    // Otherwise, store the exception and continue.
                    LastException = ex;
                    continue;
                }

                if (EndCondition(result))
                {
                    TraceSource.TraceVerbose("Trying succeeded after time {0} and total try count {1}.",
                        Stopwatch.Elapsed, TriedCount + 1);
                    OnSuccessAction(result, TriedCount + 1);
                    return result;
                }
            } while (ShouldContinue(result));

            // Should not continue. 
            OnTimeoutAction(result, TriedCount);
            throw new TimeoutException(TimeoutErrorMsg, LastException);
        }

        private bool ShouldThrow(Exception exception)
        {
            // If exception is not recoverable,
            if (exception is OutOfMemoryException || exception is AccessViolationException ||
                // or exception is not expected or not of expected type.
                !RetryOnException || !ExpectedExceptionType.IsInstanceOfType(exception))
            {
                TraceSource.TraceError("{0} detected when trying; throwing...", exception.GetType().Name);
                return true;
            }

            TraceSource.TraceVerbose("{0} detected when trying; continue trying...; details: {1}",
                exception.GetType().Name, exception);
            return false;
        }

        private bool ShouldContinue(T result)
        {
            if (Stopwatch.Elapsed >= MaxTryTime)
            {
                TimeoutErrorMsg = string.Format(CultureInfo.InvariantCulture,
                    "The maximum try time {0} for the operation has been exceeded.", MaxTryTime);
                return false;
            }

            if (++TriedCount >= MaxTryCount)
            {
                TimeoutErrorMsg = string.Format(CultureInfo.InvariantCulture,
                    "The maximum try count {0} for the operation has been exceeded.", MaxTryCount);
                return false;
            }

            // If should continue, perform the OnFailure action and wait some time before next try.
            OnFailureAction(result, TriedCount);
            Thread.Sleep(TryInterval);
            return true;
        }

        #endregion
    }
}