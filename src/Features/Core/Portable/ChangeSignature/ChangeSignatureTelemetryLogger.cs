﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using Microsoft.CodeAnalysis.Internal.Log;

namespace Microsoft.CodeAnalysis.ChangeSignature
{
    internal class ChangeSignatureLogger
    {
        private const string Maximum = nameof(Maximum);
        private const string Minimum = nameof(Minimum);
        private const string Mean = nameof(Mean);

        private static readonly LogAggregator<ActionInfo> s_logAggregator = new();
        private static readonly StatisticLogAggregator<ActionInfo> s_statisticLogAggregator = new();
        private static readonly HistogramLogAggregator<ActionInfo> s_histogramLogAggregator = new(bucketSize: 1000, maxBucketValue: 30000);

        internal enum ActionInfo
        {
            // Calculate % of successful dialog launches
            ChangeSignatureDialogLaunched,
            ChangeSignatureDialogCommitted,
            ChangeSignatureCommitCompleted,

            // Calculate % of successful dialog launches
            AddParameterDialogLaunched,
            AddParameterDialogCommitted,

            // Which transformations were done
            CommittedSessionAddedRemovedReordered,
            CommittedSessionAddedRemovedOnly,
            CommittedSessionAddedReorderedOnly,
            CommittedSessionRemovedReorderedOnly,
            CommittedSessionAddedOnly,
            CommittedSessionRemovedOnly,
            CommittedSessionReorderedOnly,

            // Signature change specification details
            CommittedSession_OriginalParameterCount,
            CommittedSessionWithRemoved_NumberRemoved,
            CommittedSessionWithAdded_NumberAdded,

            // Signature change commit information
            CommittedSessionNumberOfDeclarationsUpdated,
            CommittedSessionNumberOfCallSitesUpdated,
            CommittedSessionCommitElapsedMS,

            // Added parameter binds or doesn't bind
            AddedParameterTypeBinds,

            // Added parameter required or optional w/default
            AddedParameterRequired,

            // Added parameter callsite value options
            AddedParameterValueExplicit,
            AddedParameterValueExplicitNamed,
            AddedParameterValueTODO,
            AddedParameterValueOmitted
        }

        internal static void LogChangeSignatureDialogLaunched() =>
            s_logAggregator.IncreaseCount(ActionInfo.ChangeSignatureDialogLaunched);

        internal static void LogChangeSignatureDialogCommitted() =>
            s_logAggregator.IncreaseCount(ActionInfo.ChangeSignatureDialogCommitted);

        internal static void LogAddParameterDialogLaunched() =>
            s_logAggregator.IncreaseCount(ActionInfo.AddParameterDialogLaunched);

        internal static void LogAddParameterDialogCommitted() =>
            s_logAggregator.IncreaseCount(ActionInfo.AddParameterDialogCommitted);

        internal static void LogTransformationInformation(int numOriginalParameters, int numParametersAdded, int numParametersRemoved, bool anyParametersReordered)
        {
            LogTransformationCombination(numParametersAdded > 0, numParametersRemoved > 0, anyParametersReordered);

            s_logAggregator.IncreaseCountBy(ActionInfo.CommittedSession_OriginalParameterCount, numOriginalParameters);

            if (numParametersAdded > 0)
            {
                s_logAggregator.IncreaseCountBy(ActionInfo.CommittedSessionWithAdded_NumberAdded, numParametersAdded);
            }

            if (numParametersRemoved > 0)
            {
                s_logAggregator.IncreaseCountBy(ActionInfo.CommittedSessionWithRemoved_NumberRemoved, numParametersRemoved);
            }
        }

        private static void LogTransformationCombination(bool parametersAdded, bool parametersRemoved, bool parametersReordered)
        {
            // All three transformations
            if (parametersAdded && parametersRemoved && parametersReordered)
            {
                s_logAggregator.IncreaseCount(ActionInfo.CommittedSessionAddedRemovedReordered);
                return;
            }

            // Two transformations
            if (parametersAdded && parametersRemoved)
            {
                s_logAggregator.IncreaseCount(ActionInfo.CommittedSessionAddedRemovedOnly);
                return;
            }

            if (parametersAdded && parametersReordered)
            {
                s_logAggregator.IncreaseCount(ActionInfo.CommittedSessionAddedReorderedOnly);
                return;
            }

            if (parametersRemoved && parametersReordered)
            {
                s_logAggregator.IncreaseCount(ActionInfo.CommittedSessionRemovedReorderedOnly);
                return;
            }

            // One transformation
            if (parametersAdded)
            {
                s_logAggregator.IncreaseCount(ActionInfo.CommittedSessionAddedOnly);
                return;
            }

            if (parametersRemoved)
            {
                s_logAggregator.IncreaseCount(ActionInfo.CommittedSessionRemovedOnly);
                return;
            }

            if (parametersReordered)
            {
                s_logAggregator.IncreaseCount(ActionInfo.CommittedSessionReorderedOnly);
                return;
            }
        }

        internal static void LogCommitInformation(int numDeclarationsUpdated, int numCallSitesUpdated, TimeSpan elapsedTime)
        {
            s_logAggregator.IncreaseCount(ActionInfo.ChangeSignatureCommitCompleted);

            s_logAggregator.IncreaseCountBy(ActionInfo.CommittedSessionNumberOfDeclarationsUpdated, numDeclarationsUpdated);
            s_logAggregator.IncreaseCountBy(ActionInfo.CommittedSessionNumberOfCallSitesUpdated, numCallSitesUpdated);

            s_statisticLogAggregator.AddDataPoint(ActionInfo.CommittedSessionCommitElapsedMS, (int)elapsedTime.TotalMilliseconds);
            s_histogramLogAggregator.IncreaseCount(ActionInfo.CommittedSessionCommitElapsedMS, elapsedTime);
        }

        internal static void LogAddedParameterTypeBinds()
        {
            s_logAggregator.IncreaseCount(ActionInfo.AddedParameterTypeBinds);
        }

        internal static void LogAddedParameterRequired()
        {
            s_logAggregator.IncreaseCount(ActionInfo.AddedParameterRequired);
        }

        internal static void LogAddedParameter_ValueExplicit()
        {
            s_logAggregator.IncreaseCount(ActionInfo.AddedParameterValueExplicit);
        }

        internal static void LogAddedParameter_ValueExplicitNamed()
        {
            s_logAggregator.IncreaseCount(ActionInfo.AddedParameterValueExplicitNamed);
        }

        internal static void LogAddedParameter_ValueTODO()
        {
            s_logAggregator.IncreaseCount(ActionInfo.AddedParameterValueTODO);
        }

        internal static void LogAddedParameter_ValueOmitted()
        {
            s_logAggregator.IncreaseCount(ActionInfo.AddedParameterValueOmitted);
        }

        internal static void ReportTelemetry()
        {
            Logger.Log(FunctionId.ChangeSignature_Data, KeyValueLogMessage.Create(m =>
            {
                foreach (var kv in s_logAggregator)
                {
                    var info = kv.Key.ToString("f");
                    m[info] = kv.Value.GetCount();
                }

                foreach (var kv in s_statisticLogAggregator)
                {
                    var info = kv.Key.ToString("f");
                    var statistics = kv.Value.GetStatisticResult();

                    m[CreateProperty(info, Maximum)] = statistics.Maximum;
                    m[CreateProperty(info, Minimum)] = statistics.Minimum;
                    m[CreateProperty(info, Mean)] = statistics.Mean;
                }

                foreach (var kv in s_histogramLogAggregator)
                {
                    var info = kv.Key.ToString("f");
                    m[$"{info}.BucketSize"] = kv.Value.BucketSize;
                    m[$"{info}.MaxBucketValue"] = kv.Value.MaxBucketValue;
                    m[$"{info}.Buckets"] = kv.Value.GetBucketsAsString();
                }
            }));
        }

        private static string CreateProperty(string parent, string child)
            => parent + "." + child;
    }
}
