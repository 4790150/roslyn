﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.ExternalAccess.Xaml;
using Microsoft.CodeAnalysis.LanguageServer.Handler;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Microsoft.CodeAnalysis.LanguageServer.Xaml.Handler;

internal abstract class XamlRequestHandlerBase<TRequest, TResponse> : ILspServiceDocumentRequestHandler<TRequest, TResponse>
{
    private readonly IXamlRequestHandler<TRequest, TResponse>? _xamlRequestHandler;

    public XamlRequestHandlerBase(IXamlRequestHandler<TRequest, TResponse>? xamlRequestHandler)
    {
        _xamlRequestHandler = xamlRequestHandler;
    }

    public bool MutatesSolutionState => _xamlRequestHandler?.MutatesSolutionState ?? false;

    public virtual bool RequiresLSPSolution => true;

    public abstract TextDocumentIdentifier GetTextDocumentIdentifier(TRequest request);

    public Task<TResponse> HandleRequestAsync(TRequest request, RequestContext context, CancellationToken cancellationToken)
        => _xamlRequestHandler?.HandleRequestAsync(request, XamlRequestContext.FromRequestContext(context), cancellationToken) ?? throw new NotImplementedException();
}
