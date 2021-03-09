﻿using DeltaKustoIntegration;
using DeltaKustoIntegration.Action;
using DeltaKustoIntegration.Database;
using DeltaKustoIntegration.Parameterization;
using DeltaKustoLib;
using DeltaKustoLib.CommandModel;
using DeltaKustoLib.KustoModel;
using DeltaKustoLib.SchemaObjects;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace delta_kusto
{
    internal class DeltaOrchestration
    {
        private readonly IFileGateway _fileGateway;
        private readonly IKustoManagementGatewayFactory _kustoManagementGatewayFactory;
        private readonly ITokenProviderFactory _tokenProviderFactory;

        public DeltaOrchestration(
            IFileGateway? fileGateway = null,
            IKustoManagementGatewayFactory? kustoManagementGatewayFactory = null,
            ITokenProviderFactory? tokenProviderFactory = null)
        {
            _fileGateway = fileGateway ?? new FileGateway();
            _kustoManagementGatewayFactory = kustoManagementGatewayFactory
                ?? new KustoManagementGatewayFactory();
            _tokenProviderFactory = tokenProviderFactory ?? new TokenProviderFactory();
        }

        public async Task ComputeDeltaAsync(string parameterFilePath)
        {
            var parameters = await LoadParameterizationAsync(parameterFilePath);
            var tokenProvider = _tokenProviderFactory.CreateProvider(parameters.TokenProvider);
            var orderedJobs = parameters.Jobs.OrderBy(p => p.Value.Priority);

            foreach (var jobPair in orderedJobs)
            {
                var (jobName, job) = jobPair;

                try
                {
                    var currentDbProvider = CreateDatabaseProvider(job.Current, tokenProvider);
                    var targetDbProvider = CreateDatabaseProvider(job.Target, tokenProvider);
                    var actionProvider = CreateActionProvider(job.Action, tokenProvider, job.Target?.Cluster);
                    var currentDb = await currentDbProvider.RetrieveDatabaseAsync();
                    var targetDb = await targetDbProvider.RetrieveDatabaseAsync();
                    var deltaCommands = currentDb.ComputeDelta(targetDb);

                    await actionProvider.ProcessDeltaCommandsAsync(deltaCommands);
                }
                catch (DeltaException ex)
                {
                    throw new DeltaException($"Issue in running job '{jobName}'", ex);
                }
            }
        }

        internal async Task<MainParameterization> LoadParameterizationAsync(string parameterFilePath)
        {
            var parameterText = await _fileGateway.GetFileContentAsync(parameterFilePath);
            var parameters = JsonSerializer.Deserialize<MainParameterization>(
                parameterText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            parameters.Validate();

            return parameters;
        }

        private IActionProvider CreateActionProvider(
            ActionParameterization? action,
            ITokenProvider? tokenProvider,
            ClusterSourceParameterization? cluster)
        {
            if (action == null)
            {
                throw new NotImplementedException();
            }
            else if (action.FilePath != null)
            {
                return new OneFileActionProvider(_fileGateway, action.FilePath);
            }
            else if (action.FolderPath != null)
            {
                return new MultiFilesActionProvider(_fileGateway, action.FolderPath);
            }
            else if (action.UseTargetCluster == true)
            {
                throw new NotImplementedException();
            }
            else
            {
                throw new InvalidOperationException("We should never get here");
            }
        }

        private IDatabaseProvider CreateDatabaseProvider(
            SourceParameterization? source,
            ITokenProvider? tokenProvider)
        {
            if (source == null)
            {
                return new EmptyDatabaseProvider();
            }
            else
            {
                if (source.Cluster != null)
                {
                    if (tokenProvider == null)
                    {
                        throw new InvalidOperationException($"{tokenProvider} can't be null at this point");
                    }

                    var kustoManagementGateway = _kustoManagementGatewayFactory.CreateGateway(
                        source.Cluster.ClusterUri!,
                        source.Cluster.Database!,
                        tokenProvider);

                    return new KustoDatabaseProvider(kustoManagementGateway);
                }
                else if (source.Scripts != null)
                {
                    return new ScriptDatabaseProvider(_fileGateway, source.Scripts);
                }
                else
                {
                    throw new InvalidOperationException("We should never get here");
                }
            }
        }
    }
}