﻿using Kusto.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DeltaKustoLib.CommandModel.Policies.Retention
{
    /// <summary>
    /// Models <see cref="https://docs.microsoft.com/en-us/azure/data-explorer/kusto/management/retention-policy#alter-retention-policy"/>
    /// </summary>
    [Command(14100, "Alter Retention Policies")]
    public class AlterRetentionPolicyCommand : EntityPolicyCommandBase, ISingularToPluralCommand
    {
        public override string CommandFriendlyName => ".alter <entity> policy retention";

        public override string ScriptPath => EntityType == EntityType.Database
            ? $"databases/policies/retention/{EntityName}"
            : $"tables/policies/retention/create/{EntityName}";

        public AlterRetentionPolicyCommand(
            EntityType entityType,
            EntityName entityName,
            JsonDocument policy) : base(entityType, entityName, policy)
        {
        }

        public AlterRetentionPolicyCommand(
            EntityType entityType,
            EntityName entityName,
            TimeSpan softDeletePeriod,
            bool recoverability)
            : this(
                  entityType,
                  entityName,
                  ToJsonDocument(new
                  {
                      SoftDeletePeriod = softDeletePeriod.ToString(),
                      Recoverability = recoverability ? "Enabled" : "Disabled"
                  }))
        {
        }

        internal static CommandBase FromCode(SyntaxElement rootElement)
        {
            var entityType = ExtractEntityType(rootElement);
            var entityName = rootElement.GetDescendants<NameReference>().Last();
            var policyText = QuotedText.FromLiteral(
                rootElement.GetUniqueDescendant<LiteralExpression>(
                    "RetentionPolicy",
                    e => e.NameInParent == "RetentionPolicy"));
            var policy = Deserialize<JsonDocument>(policyText.Text);

            if (policy == null)
            {
                throw new DeltaException(
                    $"Can't extract policy objects from {policyText.ToScript()}");
            }

            return new AlterRetentionPolicyCommand(
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
            builder.Append(" policy retention");
            builder.AppendLine();
            builder.Append("```");
            builder.Append(SerializePolicy());
            builder.AppendLine();
            builder.Append("```");

            return builder.ToString();
        }

        internal static IEnumerable<CommandBase> ComputeDelta(
            AlterRetentionPolicyCommand? currentCommand,
            AlterRetentionPolicyCommand? targetCommand)
        {
            var hasCurrent = currentCommand != null;
            var hasTarget = targetCommand != null;

            if (hasCurrent && !hasTarget)
            {   //  No target, we remove the current policy
                yield return new DeleteRetentionPolicyCommand(
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

        IEnumerable<CommandBase>
            ISingularToPluralCommand.ToPlural(IEnumerable<CommandBase> singularCommands)
        {
            var dbPolicyCommands = singularCommands
                .Cast<EntityPolicyCommandBase>()
                .Where(c => c.EntityType == EntityType.Database);
            var singularPolicyCommands = singularCommands
                .Cast<AlterRetentionPolicyCommand>()
                .Where(c => c.EntityType == EntityType.Table);

            if (singularPolicyCommands.Any(c => c.EntityType != EntityType.Table))
            {
                throw new ArgumentException(
                    "Expect only table policies",
                    nameof(singularCommands));
            }

            //  We might want to cap batches to a maximum size?
            var pluralCommands = singularPolicyCommands
                .GroupBy(c => c.SerializePolicy())
                .Select(g => new AlterRetentionPluralTablePolicyCommand(
                    g.Select(c => c.EntityName),
                    g.First().Policy));

            return pluralCommands
                .Cast<CommandBase>()
                .Concat(dbPolicyCommands)
                .ToImmutableArray();
        }
    }
}