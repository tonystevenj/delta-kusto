﻿using Kusto.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DeltaKustoLib.CommandModel.Policies.IngestionBatching
{
    /// <summary>
    /// Models <see cref="https://docs.microsoft.com/en-us/azure/data-explorer/kusto/management/batching-policy#altering-the-ingestionbatching-policy"/>
    /// </summary>
    [Command(12100, "Alter Ingestion Batching Policies")]
    public class AlterIngestionBatchingPolicyCommand
        : EntityPolicyCommandBase, ISingularToPluralCommand
    {
        public override string CommandFriendlyName => ".alter <entity> policy ingestionbatching";

        public override string ScriptPath => EntityType == EntityType.Database
            ? $"databases/policies/ingestionbatching/create/{EntityName}"
            : $"tables/policies/ingestionbatching/create/{EntityName}";

        public AlterIngestionBatchingPolicyCommand(
            EntityType entityType,
            EntityName entityName,
            JsonDocument policy) : base(entityType, entityName, policy)
        {
        }

        public AlterIngestionBatchingPolicyCommand(
            EntityType entityType,
            EntityName entityName,
            TimeSpan maximumBatchingTimeSpan,
            int maximumNumberOfItems,
            int maximumRawDataSizeMb)
            : this(
                  entityType,
                  entityName,
                  ToJsonDocument(new
                  {
                      MaximumBatchingTimeSpan = maximumBatchingTimeSpan.ToString(),
                      MaximumNumberOfItems = maximumNumberOfItems,
                      MaximumRawDataSizeMb = maximumRawDataSizeMb
                  }))
        {
        }

        internal static CommandBase FromCode(SyntaxElement rootElement)
        {
            var entityType = ExtractEntityType(rootElement);
            var entityName = rootElement.GetDescendants<NameReference>().Last();
            var policyText = QuotedText.FromLiteral(
                rootElement.GetUniqueDescendant<LiteralExpression>(
                    "IngestionBatching",
                    e => e.NameInParent == "IngestionBatchingPolicy"));
            var policy = Deserialize<JsonDocument>(policyText.Text);

            if (policy == null)
            {
                throw new DeltaException(
                    $"Can't extract policy objects from {policyText.ToScript()}");
            }

            return new AlterIngestionBatchingPolicyCommand(
                entityType,
                EntityName.FromCode(entityName.Name),
                policy);
        }

        public override string ToScript(ScriptingContext? context)
        {
            var builder = new StringBuilder();

            builder.Append(".alter ");
            builder.Append(EntityType == EntityType.Table ? "table" : "database");
            builder.Append(" ");
            if (EntityType == EntityType.Database && context?.CurrentDatabaseName != null)
            {
                builder.Append(context.CurrentDatabaseName.ToScript());
            }
            else
            {
                builder.Append(EntityName.ToScript());
            }
            builder.AppendLine(" policy ingestionbatching");
            builder.AppendLine("```");
            builder.AppendLine(SerializePolicy());
            builder.AppendLine("```");

            return builder.ToString();
        }

        IEnumerable<CommandBase> ISingularToPluralCommand.ToPlural(
            IEnumerable<CommandBase> singularCommands)
        {
            var dbPolicyCommands = singularCommands
                .Cast<EntityPolicyCommandBase>()
                .Where(c => c.EntityType == EntityType.Database);
            var singularPolicyCommands = singularCommands
                .Cast<AlterIngestionBatchingPolicyCommand>()
                .Where(c => c.EntityType == EntityType.Table);

            //  We might want to cap batches to a maximum size?
            var pluralCommands = singularPolicyCommands
                .GroupBy(c => c.SerializePolicy())
                .Select(g => new AlterIngestionBatchingPluralPolicyCommand(
                    g.Select(c => c.EntityName),
                    g.First().Policy));

            return pluralCommands
                .Cast<CommandBase>()
                .Concat(dbPolicyCommands)
                .ToImmutableArray();
        }

        internal static IEnumerable<CommandBase> ComputeDelta(
            AlterIngestionBatchingPolicyCommand? currentCommand,
            AlterIngestionBatchingPolicyCommand? targetCommand)
        {
            var hasCurrent = currentCommand != null;
            var hasTarget = targetCommand != null;

            if (hasCurrent && !hasTarget)
            {   //  No target, we remove the current policy
                yield return new DeleteIngestionBatchingPolicyCommand(
                    currentCommand!.EntityType,
                    currentCommand!.EntityName);
            }
            else if (hasTarget)
            {
                if (!hasCurrent || !currentCommand!.Equals(targetCommand!))
                {   //  There is a target and either no current or the current is different
                    yield return targetCommand!;
                }
            }
            else
            {   //  Both target and current are null:  no delta
            }
        }
    }
}