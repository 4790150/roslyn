﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommonLanguageServerProtocol.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor.Xaml;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.LanguageServer;
using Microsoft.CodeAnalysis.LanguageServer.Handler;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Microsoft.VisualStudio.LanguageServices.Xaml.Telemetry;

namespace Microsoft.VisualStudio.LanguageServices.Xaml.LanguageServer
{
    internal interface IRoslynRequestExecutionQueue : IRequestExecutionQueue<RequestContext>, ILspService
    {
    }

    [ExportLspServiceFactory(typeof(IRoslynRequestExecutionQueue), StringConstants.XamlLspLanguagesContract), Shared]
    internal sealed class XamlRequestExecutionQueueFactory : ILspServiceFactory
    {
        private readonly XamlProjectService _projectService;
        private readonly IXamlLanguageServerFeedbackService? _feedbackService;

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public XamlRequestExecutionQueueFactory(
            XamlProjectService projectService,
            [Import(AllowDefault = true)] IXamlLanguageServerFeedbackService? feedbackService)
        {
            _projectService = projectService;
            _feedbackService = feedbackService;
        }

        public ILspService CreateILspService(LspServices lspServices, WellKnownLspServerKinds serverKind)
        {
            var handlerProvider = lspServices.GetRequiredService<IHandlerProvider>();
            var logger = lspServices.GetRequiredService<ILspLogger>();
            return new XamlRequestExecutionQueue(_projectService, lspServices, _feedbackService, logger, handlerProvider);
        }

        private class XamlRequestExecutionQueue : RequestExecutionQueue<RequestContext>, ILspService
        {
            private readonly XamlProjectService _projectService;
            private readonly IXamlLanguageServerFeedbackService? _feedbackService;
            private readonly ILspServices _lspServices;
            private readonly IRequestExecutionQueue<RequestContext> _baseQueue;
            private readonly IHandlerProvider _handlerProvider;

            public XamlRequestExecutionQueue(
                XamlProjectService projectService,
                ILspServices lspServices,
                IXamlLanguageServerFeedbackService? feedbackService,
                ILspLogger logger,
                IHandlerProvider handlerProvider) : base(logger, handlerProvider)
            {
                _projectService = projectService;
                _feedbackService = feedbackService;
                _lspServices = lspServices;
                _handlerProvider = handlerProvider;
            }

            public event EventHandler<RequestShutdownEventArgs>? RequestServerShutdown;

            public ValueTask DisposeAsync()
            {
                return base.DisposeAsync();
            }

            public async Task<TResponseType> ExecuteAsync<TRequestType, TResponseType>(
                TRequestType request,
                string methodName,
                ILspServices lspServices,
                CancellationToken cancellationToken)
            {
                var textDocument = GetTextDocumentIdentifier<TRequestType, TResponseType>(request, methodName);

                DocumentId? documentId = null;
                if (textDocument as TextDocumentIdentifier is { Uri: { IsAbsoluteUri: true } documentUri })
                {
                    documentId = _projectService.TrackOpenDocument(documentUri.LocalPath);
                }

                using (var requestScope = _feedbackService?.CreateRequestScope(documentId, methodName))
                {
                    try
                    {
                        return await base.ExecuteAsync<TRequestType, TResponseType>(
                            request, methodName, lspServices, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        // Inform Xaml language service that the RequestScope failed.
                        // This doesn't send the exception to Telemetry or Watson
                        requestScope?.RecordFailure(e);
                        throw;
                    }
                }
            }

            public void Start(ILspServices lspServices)
            {
                _baseQueue.Start(lspServices);
            }
        }
    }
}
