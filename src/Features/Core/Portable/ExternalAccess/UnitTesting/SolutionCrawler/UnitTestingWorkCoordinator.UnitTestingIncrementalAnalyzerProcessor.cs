﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.ErrorReporting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Internal.Log;
using Microsoft.CodeAnalysis.LanguageService;
using Microsoft.CodeAnalysis.Notification;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.ExternalAccess.UnitTesting.SolutionCrawler
{
    internal partial class UnitTestingSolutionCrawlerRegistrationService
    {
        internal partial class UnitTestingWorkCoordinator
        {
            private partial class UnitTestingIncrementalAnalyzerProcessor
            {
                private static readonly Func<int, object, bool, string> s_enqueueLogger = EnqueueLogger;

                private readonly UnitTestingRegistration _registration;
                private readonly IAsynchronousOperationListener _listener;
                private readonly IUnitTestingDocumentTrackingService _documentTracker;
                private readonly IProjectCacheService? _cacheService;

                private readonly UnitTestingHighPriorityProcessor _highPriorityProcessor;
                private readonly UnitTestingNormalPriorityProcessor _normalPriorityProcessor;
                private readonly UnitTestingLowPriorityProcessor _lowPriorityProcessor;

#if false // Not used in unit testing crawling
                // NOTE: IDiagnosticAnalyzerService can be null in test environment.
                private readonly Lazy<IDiagnosticAnalyzerService?> _lazyDiagnosticAnalyzerService;
#endif

                /// <summary>
                /// The keys in this are either a string or a (string, Guid) tuple. See <see cref="UnitTestingSolutionCrawlerLogger.LogIncrementalAnalyzerProcessorStatistics"/>
                /// for what is writing this out.
                /// </summary>
                private CountLogAggregator<object> _logAggregator = new();

                public UnitTestingIncrementalAnalyzerProcessor(
                    IAsynchronousOperationListener listener,
                    IEnumerable<Lazy<IUnitTestingIncrementalAnalyzerProvider, UnitTestingIncrementalAnalyzerProviderMetadata>> analyzerProviders,
                    bool initializeLazily,
                    UnitTestingRegistration registration,
                    TimeSpan highBackOffTimeSpan,
                    TimeSpan normalBackOffTimeSpan,
                    TimeSpan lowBackOffTimeSpan,
                    CancellationToken shutdownToken)
                {
                    _listener = listener;
                    _registration = registration;
                    _cacheService = registration.Services.GetService<IProjectCacheService>();

#if false // Not used in unit testing crawling
                    _lazyDiagnosticAnalyzerService = new Lazy<IDiagnosticAnalyzerService?>(() => GetDiagnosticAnalyzerService(analyzerProviders));
#endif

                    var analyzersGetter = new UnitTestingAnalyzersGetter(analyzerProviders);

                    // create analyzers lazily.
                    var lazyActiveFileAnalyzers = new Lazy<ImmutableArray<IUnitTestingIncrementalAnalyzer>>(() => GetIncrementalAnalyzers(_registration, analyzersGetter, onlyHighPriorityAnalyzer: true));
                    var lazyAllAnalyzers = new Lazy<ImmutableArray<IUnitTestingIncrementalAnalyzer>>(() => GetIncrementalAnalyzers(_registration, analyzersGetter, onlyHighPriorityAnalyzer: false));

                    if (!initializeLazily)
                    {
                        // realize all analyzer right away
                        _ = lazyActiveFileAnalyzers.Value;
                        _ = lazyAllAnalyzers.Value;
                    }

                    // event and worker queues
                    _documentTracker = _registration.Services.GetRequiredService<IUnitTestingDocumentTrackingService>();

                    var globalNotificationService = _registration.Services.GetRequiredService<IGlobalOperationNotificationService>();

                    _highPriorityProcessor = new UnitTestingHighPriorityProcessor(listener, this, lazyActiveFileAnalyzers, highBackOffTimeSpan, shutdownToken);
                    _normalPriorityProcessor = new UnitTestingNormalPriorityProcessor(listener, this, lazyAllAnalyzers, globalNotificationService, normalBackOffTimeSpan, shutdownToken);
                    _lowPriorityProcessor = new UnitTestingLowPriorityProcessor(listener, this, lazyAllAnalyzers, globalNotificationService, lowBackOffTimeSpan, shutdownToken);
                }

#if false // Not used in unit testing crawling
                private static IDiagnosticAnalyzerService? GetDiagnosticAnalyzerService(IEnumerable<Lazy<IUnitTestingIncrementalAnalyzerProvider, UnitTestingIncrementalAnalyzerProviderMetadata>> analyzerProviders)
                {
                    // alternatively, we could just MEF import IDiagnosticAnalyzerService directly
                    // this can be null in test env.
                    return (IDiagnosticAnalyzerService?)analyzerProviders.Where(p => p.Value is IDiagnosticAnalyzerService).SingleOrDefault()?.Value;
                }
#endif

                private static ImmutableArray<IUnitTestingIncrementalAnalyzer> GetIncrementalAnalyzers(UnitTestingRegistration registration, UnitTestingAnalyzersGetter analyzersGetter, bool onlyHighPriorityAnalyzer)
                {
                    var orderedAnalyzers = analyzersGetter.GetOrderedAnalyzers(registration.WorkspaceKind, registration.Services, onlyHighPriorityAnalyzer);

                    UnitTestingSolutionCrawlerLogger.LogAnalyzers(registration.CorrelationId, registration.WorkspaceKind, orderedAnalyzers, onlyHighPriorityAnalyzer);
                    return orderedAnalyzers;
                }

                public void Enqueue(UnitTestingWorkItem item)
                {
                    Contract.ThrowIfNull(item.DocumentId);

                    _highPriorityProcessor.Enqueue(item);
                    _normalPriorityProcessor.Enqueue(item);
                    _lowPriorityProcessor.Enqueue(item);

                    ReportPendingWorkItemCount();
                }

                public void AddAnalyzer(IUnitTestingIncrementalAnalyzer analyzer, bool highPriorityForActiveFile)
                {
                    if (highPriorityForActiveFile)
                    {
                        _highPriorityProcessor.AddAnalyzer(analyzer);
                    }

                    _normalPriorityProcessor.AddAnalyzer(analyzer);
                    _lowPriorityProcessor.AddAnalyzer(analyzer);
                }

                public void Shutdown()
                {
                    _highPriorityProcessor.Shutdown();
                    _normalPriorityProcessor.Shutdown();
                    _lowPriorityProcessor.Shutdown();
                }

                public ImmutableArray<IUnitTestingIncrementalAnalyzer> Analyzers => _normalPriorityProcessor.Analyzers;

#if false // Not used in unit testing crawling
                private ProjectDependencyGraph DependencyGraph => _registration.GetSolutionToAnalyze().GetProjectDependencyGraph();
                private IDiagnosticAnalyzerService? DiagnosticAnalyzerService => _lazyDiagnosticAnalyzerService?.Value;
#endif

                public Task AsyncProcessorTask
                {
                    get
                    {
                        return Task.WhenAll(
                            _highPriorityProcessor.AsyncProcessorTask,
                            _normalPriorityProcessor.AsyncProcessorTask,
                            _lowPriorityProcessor.AsyncProcessorTask);
                    }
                }

                private IDisposable EnableCaching(ProjectId projectId)
                    => _cacheService?.EnableCaching(projectId) ?? UnitTestingNullDisposable.Instance;

#if false // Not used in unit testing crawling
                private IEnumerable<DocumentId> GetOpenDocumentIds()
                    => _registration.Workspace.GetOpenDocumentIds();
#endif

                private void ResetLogAggregator()
                    => _logAggregator = new CountLogAggregator<object>();

                private void ReportPendingWorkItemCount()
                {
                    var pendingItemCount = _highPriorityProcessor.WorkItemCount + _normalPriorityProcessor.WorkItemCount + _lowPriorityProcessor.WorkItemCount;
                    _registration.ProgressReporter.UpdatePendingItemCount(pendingItemCount);
                }

                private async Task ProcessDocumentAnalyzersAsync(
                    TextDocument textDocument, ImmutableArray<IUnitTestingIncrementalAnalyzer> analyzers, UnitTestingWorkItem workItem, CancellationToken cancellationToken)
                {
                    // process special active document switched request, if any.
                    if (ProcessActiveDocumentSwitched(analyzers, workItem, textDocument, cancellationToken))
                    {
                        return;
                    }

                    // process all analyzers for each categories in this order - syntax, body, document
                    var reasons = workItem.InvocationReasons;
                    if (workItem.MustRefresh || reasons.Contains(UnitTestingPredefinedInvocationReasons.SyntaxChanged))
                    {
                        await RunAnalyzersAsync(analyzers, textDocument, workItem, (analyzer, document, cancellationToken) =>
                            AnalyzeSyntaxAsync(analyzer, document, reasons, cancellationToken), cancellationToken).ConfigureAwait(false);
                    }

                    if (textDocument is not Document document)
                    {
                        // Semantic analysis is not supported for non-source documents.
                        return;
                    }

                    if (workItem.MustRefresh || reasons.Contains(UnitTestingPredefinedInvocationReasons.SemanticChanged))
                    {
                        await RunAnalyzersAsync(analyzers, document, workItem, (analyzer, document, cancellationToken) =>
                            analyzer.AnalyzeDocumentAsync(document, null, reasons, cancellationToken), cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        // if we don't need to re-analyze whole body, see whether we need to at least re-analyze one method.
                        await RunBodyAnalyzersAsync(analyzers, workItem, document, cancellationToken).ConfigureAwait(false);
                    }

                    return;

                    static async Task AnalyzeSyntaxAsync(IUnitTestingIncrementalAnalyzer analyzer, TextDocument textDocument, UnitTestingInvocationReasons reasons, CancellationToken cancellationToken)
                    {
                        if (textDocument is Document document)
                        {
                            await analyzer.AnalyzeSyntaxAsync(document, reasons, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
#if false // Not used in unit testing crawling
                            await analyzer.AnalyzeNonSourceDocumentAsync(textDocument, reasons, cancellationToken).ConfigureAwait(false);
#endif
                        }
                    }

                    bool ProcessActiveDocumentSwitched(ImmutableArray<IUnitTestingIncrementalAnalyzer> analyzers, UnitTestingWorkItem workItem, TextDocument document, CancellationToken cancellationToken)
                    {
                        try
                        {
                            if (!workItem.InvocationReasons.Contains(UnitTestingPredefinedInvocationReasons.ActiveDocumentSwitched))
                            {
                                return false;
                            }

#if false // Not used in unit testing crawling
                            await RunAnalyzersAsync(analyzers, document, workItem, (analyzer, document, cancellationToken) =>
                                analyzer.ActiveDocumentSwitchedAsync(document, cancellationToken), cancellationToken).ConfigureAwait(false);
#endif
                            return true;
                        }
                        catch (Exception e) when (FatalError.ReportAndPropagateUnlessCanceled(e, cancellationToken))
                        {
                            throw ExceptionUtilities.Unreachable;
                        }
                    }
                }

                private async Task RunAnalyzersAsync<T>(
                    ImmutableArray<IUnitTestingIncrementalAnalyzer> analyzers,
                    T value,
                    UnitTestingWorkItem workItem,
                    Func<IUnitTestingIncrementalAnalyzer, T, CancellationToken, Task> runnerAsync,
                    CancellationToken cancellationToken)
                {
                    using var evaluating = _registration.ProgressReporter.GetEvaluatingScope();

                    ReportPendingWorkItemCount();

                    // Check if the work item is specific to some incremental analyzer(s).
                    var analyzersToExecute = workItem.GetApplicableAnalyzers(analyzers) ?? analyzers;
                    foreach (var analyzer in analyzersToExecute)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        var local = analyzer;
                        if (local == null)
                        {
                            return;
                        }

                        await GetOrDefaultAsync(value, async (v, c) =>
                        {
                            await runnerAsync(local, v, c).ConfigureAwait(false);
                            return (object?)null;
                        }, cancellationToken).ConfigureAwait(false);
                    }
                }

                private async Task RunBodyAnalyzersAsync(ImmutableArray<IUnitTestingIncrementalAnalyzer> analyzers, UnitTestingWorkItem workItem, Document document, CancellationToken cancellationToken)
                {
                    try
                    {
                        var root = await GetOrDefaultAsync(document, (d, c) => d.GetSyntaxRootAsync(c), cancellationToken).ConfigureAwait(false);
                        var syntaxFactsService = document.GetLanguageService<ISyntaxFactsService>();
                        var reasons = workItem.InvocationReasons;
                        if (root == null || syntaxFactsService == null)
                        {
                            // as a fallback mechanism, if we can't run one method body due to some missing service, run whole document analyzer.
                            await RunAnalyzersAsync(analyzers, document, workItem, (analyzer, document, cancellationToken) =>
                                analyzer.AnalyzeDocumentAsync(document, null, reasons, cancellationToken), cancellationToken).ConfigureAwait(false);
                            return;
                        }

                        // check whether we know what body has changed. currently, this is an optimization toward typing case. if there are more than one body changes
                        // it will be considered as semantic change and whole document analyzer will take care of that case.
                        var activeMember = GetMemberNode(syntaxFactsService, root, workItem.ActiveMember);
                        if (activeMember == null)
                        {
                            // no active member means, change is out side of a method body, but it didn't affect semantics (such as change in comment)
                            // in that case, we update whole document (just this document) so that we can have updated locations.
                            await RunAnalyzersAsync(analyzers, document, workItem, (analyzer, document, cancellationToken) =>
                                analyzer.AnalyzeDocumentAsync(document, null, reasons, cancellationToken), cancellationToken).ConfigureAwait(false);
                            return;
                        }

                        // re-run just the body
                        await RunAnalyzersAsync(analyzers, document, workItem, (analyzer, document, cancellationToken) =>
                            analyzer.AnalyzeDocumentAsync(document, activeMember, reasons, cancellationToken), cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (FatalError.ReportAndPropagateUnlessCanceled(e, cancellationToken))
                    {
                        throw ExceptionUtilities.Unreachable;
                    }
                }

                private static async Task<TResult?> GetOrDefaultAsync<TData, TResult>(TData value, Func<TData, CancellationToken, Task<TResult?>> funcAsync, CancellationToken cancellationToken)
                    where TResult : class
                {
                    try
                    {
                        return await funcAsync(value, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                    catch (AggregateException e) when (ReportWithoutCrashUnlessAllCanceledAndPropagate(e))
                    {
                        return null;
                    }
                    catch (Exception e) when (FatalError.ReportAndPropagate(e))
                    {
                        // TODO: manage bad workers like what code actions does now
                        throw ExceptionUtilities.Unreachable;
                    }

                    static bool ReportWithoutCrashUnlessAllCanceledAndPropagate(AggregateException aggregate)
                    {
                        var flattened = aggregate.Flatten();
                        if (flattened.InnerExceptions.All(e => e is OperationCanceledException))
                        {
                            return true;
                        }

                        return FatalError.ReportAndPropagate(flattened);
                    }
                }

                private static SyntaxNode? GetMemberNode(ISyntaxFactsService service, SyntaxNode? root, SyntaxPath? memberPath)
                {
                    if (root == null || memberPath == null)
                    {
                        return null;
                    }

                    if (!memberPath.TryResolve(root, out SyntaxNode? memberNode))
                    {
                        return null;
                    }

                    return service.IsMethodLevelMember(memberNode) ? memberNode : null;
                }

                private static string EnqueueLogger(int tick, object documentOrProjectId, bool replaced)
                {
                    if (documentOrProjectId is DocumentId documentId)
                    {
                        return $"Tick:{tick}, {documentId}, {documentId.ProjectId}, Replaced:{replaced}";
                    }

                    return $"Tick:{tick}, {documentOrProjectId}, Replaced:{replaced}";
                }

                internal TestAccessor GetTestAccessor()
                {
                    return new TestAccessor(this);
                }

                internal readonly struct TestAccessor
                {
                    private readonly UnitTestingIncrementalAnalyzerProcessor _incrementalAnalyzerProcessor;

                    internal TestAccessor(UnitTestingIncrementalAnalyzerProcessor incrementalAnalyzerProcessor)
                    {
                        _incrementalAnalyzerProcessor = incrementalAnalyzerProcessor;
                    }

                    internal void WaitUntilCompletion(ImmutableArray<IUnitTestingIncrementalAnalyzer> analyzers, List<UnitTestingWorkItem> items)
                    {
                        _incrementalAnalyzerProcessor._normalPriorityProcessor.GetTestAccessor().WaitUntilCompletion(analyzers, items);

                        var projectItems = items.Select(i => i.ToProjectWorkItem(EmptyAsyncToken.Instance));
                        _incrementalAnalyzerProcessor._lowPriorityProcessor.GetTestAccessor().WaitUntilCompletion(analyzers, items);
                    }

                    internal void WaitUntilCompletion()
                    {
                        _incrementalAnalyzerProcessor._normalPriorityProcessor.GetTestAccessor().WaitUntilCompletion();
                        _incrementalAnalyzerProcessor._lowPriorityProcessor.GetTestAccessor().WaitUntilCompletion();
                    }
                }

                private class UnitTestingNullDisposable : IDisposable
                {
                    public static readonly IDisposable Instance = new UnitTestingNullDisposable();

                    public void Dispose() { }
                }

                private class UnitTestingAnalyzersGetter
                {
                    private readonly List<Lazy<IUnitTestingIncrementalAnalyzerProvider, UnitTestingIncrementalAnalyzerProviderMetadata>> _analyzerProviders;
                    private readonly Dictionary<(string workspaceKind, SolutionServices services), ImmutableArray<(IUnitTestingIncrementalAnalyzer analyzer, bool highPriorityForActiveFile)>> _analyzerMap = new();

                    public UnitTestingAnalyzersGetter(IEnumerable<Lazy<IUnitTestingIncrementalAnalyzerProvider, UnitTestingIncrementalAnalyzerProviderMetadata>> analyzerProviders)
                    {
                        _analyzerProviders = analyzerProviders.ToList();
                    }

                    public ImmutableArray<IUnitTestingIncrementalAnalyzer> GetOrderedAnalyzers(string workspaceKind, SolutionServices services, bool onlyHighPriorityAnalyzer)
                    {
                        lock (_analyzerMap)
                        {
                            if (!_analyzerMap.TryGetValue((workspaceKind, services), out var analyzers))
                            {
                                // Sort list so DiagnosticIncrementalAnalyzers (if any) come first.
                                analyzers = _analyzerProviders.Select(p => (analyzer: p.Value.CreateIncrementalAnalyzer(), highPriorityForActiveFile: p.Metadata.HighPriorityForActiveFile))
                                                .Where(t => t.analyzer != null)
#if false // Not used in unit testing crawling
                                                .OrderBy(t => t.analyzer!.Priority)
#endif
                                                .ToImmutableArray()!;

                                _analyzerMap[(workspaceKind, services)] = analyzers;
                            }

                            if (onlyHighPriorityAnalyzer)
                            {
                                // include only high priority analyzer for active file
                                return analyzers.SelectAsArray(t => t.highPriorityForActiveFile, t => t.analyzer);
                            }

                            // return all analyzers
                            return analyzers.Select(t => t.analyzer).ToImmutableArray();
                        }
                    }
                }
            }
        }
    }
}
