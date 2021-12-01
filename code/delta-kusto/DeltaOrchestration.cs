﻿using DeltaKustoIntegration;
using DeltaKustoIntegration.Action;
using DeltaKustoIntegration.Database;
using DeltaKustoIntegration.Kusto;
using DeltaKustoIntegration.Parameterization;
using DeltaKustoLib;
using DeltaKustoLib.CommandModel;
using DeltaKustoLib.KustoModel;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace delta_kusto
{
    internal class DeltaOrchestration
    {
        private readonly ITracer _tracer;
        private readonly ApiClient _apiClient;
        private readonly IFileGateway _fileGateway;

        public DeltaOrchestration(
            ITracer tracer,
            ApiClient apiClient,
            IFileGateway? fileGateway = null)
        {
            _tracer = tracer;
            _apiClient = apiClient;
            _fileGateway = fileGateway ?? new FileGateway();
        }

        public async Task<bool> ComputeDeltaAsync(
            string parameterFilePath,
            IEnumerable<string> pathOverrides)
        {
            _tracer.WriteLine(false, "Activating Client...");

            var availableClientVersions = await _apiClient.ActivateAsync();

            if (availableClientVersions == null)
            {
                _tracer.WriteLine(false, "Activating Client (skipped)");
            }
            else
            {
                _tracer.WriteLine(false, "Client Activated");
                if (availableClientVersions.Any())
                {
                    _tracer.WriteLine(
                        false,
                        "Newer clients available:  "
                        + string.Join(", ", availableClientVersions));
                }
            }

            _tracer.WriteLine(false, $"Loading parameters at '{parameterFilePath}'");

            var parameters =
                await LoadParameterizationAsync(parameterFilePath, pathOverrides);
            var parameterTelemetryTask = _apiClient.LogParameterTelemetryAsync(parameters);
            var parameterFolderPath = Path.GetDirectoryName(parameterFilePath);

            try
            {
                var localFileGateway = _fileGateway.ChangeFolder(parameterFolderPath!);
                var kustoManagementGatewayFactory = new KustoManagementGatewayFactory(
                    parameters.TokenProvider,
                    _tracer);
                var orderedJobs = parameters.Jobs.OrderBy(p => p.Value.Priority);
                var success = true;

                _tracer.WriteLine(false, $"{orderedJobs.Count()} jobs");
                foreach (var jobPair in orderedJobs)
                {
                    var (jobName, job) = jobPair;
                    var jobSuccess = await ProcessJobAsync(
                        parameters,
                        kustoManagementGatewayFactory,
                        localFileGateway,
                        jobName,
                        job);

                    success = success && jobSuccess;
                }

                await _apiClient.EndSessionAsync(success);

                return success;
            }
            catch (Exception ex)
            {
                if (parameters.SendErrorOptIn)
                {
                    var sessionID = await _apiClient.RegisterExceptionAsync(ex);

                    _tracer.WriteLine(
                        false,
                        $"Exception registered with Session ID '{sessionID}'");
                }
                throw;
            }
            finally
            {
                await parameterTelemetryTask;
            }
        }

        private async Task<bool> ProcessJobAsync(
            MainParameterization parameters,
            IKustoManagementGatewayFactory kustoGatewayFactory,
            IFileGateway localFileGateway,
            string jobName,
            JobParameterization job)
        {
            _tracer.WriteLine(false, $"Job '{jobName}':");
            _tracer.WriteLine(false, "");
            try
            {
                _tracer.WriteLine(true, "Current DB Provider...  ");

                var currentDbProvider = CreateDatabaseProvider(job.Current, kustoGatewayFactory, localFileGateway);

                _tracer.WriteLine(true, "Target DB Provider...  ");

                var targetDbProvider = CreateDatabaseProvider(job.Target, kustoGatewayFactory, localFileGateway);

                var currentDbTask = RetrieveDatabaseAsync(currentDbProvider, "current");
                var targetDbTask = RetrieveDatabaseAsync(targetDbProvider, "target");

                await Task.WhenAll(currentDbTask, targetDbTask);

                var currentDb = await currentDbTask;
                var targetDb = await targetDbTask;

                _tracer.WriteLine(false, "Compute Delta...");

                var delta = currentDb.ComputeDelta(targetDb);
                var actions = new CommandCollection(job.Action!.UsePluralForms, delta);
                var jobSuccess = ReportOnDeltaCommands(parameters, actions);
                var actionProviders = CreateActionProvider(
                    job.Action!,
                    kustoGatewayFactory,
                    localFileGateway,
                    job.Current?.Adx);

                _tracer.WriteLine(false, "Processing delta commands...");
                foreach (var actionProvider in actionProviders)
                {
                    await actionProvider.ProcessDeltaCommandsAsync(
                        parameters.FailIfDataLoss,
                        actions);
                }
                _tracer.WriteLine(false, "Delta processed / Job completed");
                _tracer.WriteLine(false, "");

                return jobSuccess;
            }
            catch (DeltaException ex)
            {
                throw new DeltaException($"Issue in running job '{jobName}'", ex);
            }
        }

        private async Task<DatabaseModel> RetrieveDatabaseAsync(
            IDatabaseProvider currentDbProvider,
            string db)
        {
            _tracer.WriteLine(true, $"Retrieving {db}...");

            var model = await currentDbProvider.RetrieveDatabaseAsync();

            _tracer.WriteLine(true, $"{db} retrieved");

            return model;
        }

        private bool ReportOnDeltaCommands(
            MainParameterization parameters,
            CommandCollection deltaCommands)
        {
            var success = true;

            _tracer.WriteLine(false, $"{deltaCommands.AllCommands.Count()} commands in delta");
            if (deltaCommands.DataLossCommands.Any())
            {
                _tracer.WriteLine(false, "Delta contains drop commands:");
                foreach (var command in deltaCommands.DataLossCommands)
                {
                    _tracer.WriteLine(false, "  " + command.ToScript());
                }
                _tracer.WriteLine(false, "");
                if (parameters.FailIfDataLoss)
                {
                    _tracer.WriteErrorLine("Drop commands forces failure");
                    success = false;
                }
            }

            return success;
        }

        internal async Task<MainParameterization> LoadParameterizationAsync(
            string parameterFilePath,
            IEnumerable<string> pathOverrides)
        {
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                var parameterText = await _fileGateway.GetFileContentAsync(parameterFilePath);
                var parameters = deserializer.Deserialize<MainParameterization>(parameterText);

                if (parameters == null)
                {
                    throw new DeltaException($"File '{parameterFilePath}' doesn't contain valid parameters");
                }

                ParameterOverrideHelper.InplaceOverride(parameters, pathOverrides);

                parameters.Validate();

                return parameters;
            }
            catch (JsonException ex)
            {
                throw new DeltaException(
                    $"Issue reading the parameter file '{parameterFilePath}'",
                    ex);
            }
        }

        private IImmutableList<IActionProvider> CreateActionProvider(
            ActionParameterization action,
            IKustoManagementGatewayFactory kustoGatewayFactory,
            IFileGateway localFileGateway,
            AdxSourceParameterization? database)
        {
            var builder = ImmutableArray<IActionProvider>.Empty.ToBuilder();

            builder.Add(new ConsoleActionProvider(_tracer, !action.PushToConsole));

            if (action.FilePath != null)
            {
                builder.Add(new OneFileActionProvider(localFileGateway, action.FilePath));
            }
            if (action.FolderPath != null)
            {
                builder.Add(new MultiFilesActionProvider(localFileGateway, action.FolderPath));
            }
            if (action.PushToCurrent)
            {
                var kustoManagementGateway = kustoGatewayFactory.CreateGateway(
                    new Uri(database!.ClusterUri!),
                    database!.Database!);

                builder.Add(new KustoActionProvider(kustoManagementGateway));
            }

            return builder.ToImmutable();
        }

        private IDatabaseProvider CreateDatabaseProvider(
            SourceParameterization? source,
            IKustoManagementGatewayFactory kustoGatewayFactory,
            IFileGateway localFileGateway)
        {
            if (source == null)
            {
                _tracer.WriteLine(true, "Empty database");

                return new EmptyDatabaseProvider();
            }
            else
            {
                if (source.Adx != null)
                {
                    _tracer.WriteLine(
                        true,
                        $"ADX Database:  cluster '{source.Adx.ClusterUri}', "
                        + $"database '{source.Adx.Database}'");

                    var kustoManagementGateway = kustoGatewayFactory.CreateGateway(
                        new Uri(source.Adx.ClusterUri!),
                        source.Adx.Database!);

                    return new KustoDatabaseProvider(_tracer, kustoManagementGateway);
                }
                else if (source.Scripts != null)
                {
                    _tracer.WriteLine(true, "Database scripts");

                    return new ScriptDatabaseProvider(_tracer, localFileGateway, source.Scripts);
                }
                else
                {
                    throw new InvalidOperationException("We should never get here");
                }
            }
        }
    }
}