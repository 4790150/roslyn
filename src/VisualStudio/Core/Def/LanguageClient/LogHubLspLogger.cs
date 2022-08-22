﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.LanguageServer;
using Microsoft.VisualStudio.LogHub;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.LanguageClient
{
    internal class LogHubLspLogger : IRoslynLspLogger
    {
        private readonly TraceConfiguration _configuration;
        private readonly TraceSource _traceSource;
        private bool _disposed;

        public LogHubLspLogger(TraceConfiguration configuration, TraceSource traceSource)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _traceSource = traceSource ?? throw new ArgumentNullException(nameof(traceSource));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                Contract.Fail($"{GetType().FullName} was double disposed");
                return;
            }

            _disposed = true;
            _traceSource.Flush();
            _traceSource.Close();
            _configuration.Dispose();
        }

        public Task LogInformationAsync(string message, CancellationToken cancellationToken, params object[] @params)
        {
            // Explicitly call TraceEvent here instead of TraceInformation.
            // TraceInformation indirectly calls string.Format which throws if the message
            // has unescaped curlies in it (can be a part of a URI for example).
            // Since we have no need to call string.Format here, we don't.
            _traceSource.TraceEvent(TraceEventType.Information, id: 0, message);

            return Task.CompletedTask;
        }

        public Task LogWarningAsync(string message, CancellationToken cancellationToken, params object[] @params)
        {
            _traceSource.TraceEvent(TraceEventType.Warning, id: 0, message);

            return Task.CompletedTask;
        }

        public Task LogErrorAsync(string message, CancellationToken cancellationToken, params object[] @params)
        {
            _traceSource.TraceEvent(TraceEventType.Error, id: 0, message);

            return Task.CompletedTask;
        }

        public Task LogExceptionAsync(Exception exception, string? message = null, CancellationToken? cancellationToken = null, params object[] @params)
        {
            _traceSource.TraceEvent(TraceEventType.Error, id: 0, "Exception: {0}", exception);

            return Task.CompletedTask;
        }

        public Task LogStartContextAsync(string message, CancellationToken cancellationToken, params object[] @params)
        {
            _traceSource.TraceEvent(TraceEventType.Start, id: 0, message);

            return Task.CompletedTask;
        }

        public Task LogEndContextAsync(string message, CancellationToken cancellationToken, params object[] @params)
        {
            _traceSource.TraceEvent(TraceEventType.Stop, id: 0, message);

            return Task.CompletedTask;
        }
    }
}
