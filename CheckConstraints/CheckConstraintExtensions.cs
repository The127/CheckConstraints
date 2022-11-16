using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CheckConstraints;

public static class CheckConstraintExtensions
{
    public static DbContextOptionsBuilder UseCheckConstraints(
        this DbContextOptionsBuilder optionsBuilder)
    {
        var extension = optionsBuilder.Options.FindExtension<CheckConstraintExtension>() ??
                        new CheckConstraintExtension();

        var dbContextOptionsBuilderInfrastructure = (IDbContextOptionsBuilderInfrastructure)optionsBuilder;
        dbContextOptionsBuilderInfrastructure.AddOrUpdateExtension(extension);

        return optionsBuilder;
    }
}

public class CheckConstraintExtension : IDbContextOptionsExtension
{
    public DbContextOptionsExtensionInfo Info { get; }

    public CheckConstraintExtension()
    {
        Info = new ExtensionInfo(this);
    }

    public void ApplyServices(IServiceCollection services)
    {
        var entityFrameworkServicesBuilder = new EntityFrameworkServicesBuilder(services);
        entityFrameworkServicesBuilder.TryAdd<IConventionSetPlugin, CheckConstraintService>();
    }

    public void Validate(IDbContextOptions options)
    {
    }
}

public class CheckConstraintService : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.ModelFinalizingConventions.Add(new CheckConstraintConvention());
        return conventionSet;
    }
}

public class CheckConstraintConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var conventionEntityType in modelBuilder.Metadata.GetEntityTypes())
        {
            var entityClrType = conventionEntityType.ClrType;

            if (!ConstraintsBuilder.TryGetConstraints(entityClrType, out var constraints)) 
                continue;
            
            foreach (var constraint in constraints)
            {
                constraint.Configure(modelBuilder);
            }
        }
    }
}

internal sealed class ExtensionInfo : DbContextOptionsExtensionInfo
{
    public ExtensionInfo(IDbContextOptionsExtension extension)
        : base(extension)
    {
    }

    public override bool IsDatabaseProvider => false;

    public override string LogFragment => "using check constraints";

    public override int GetServiceProviderHashCode()
        => 0;

    public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        => other is ExtensionInfo;

    public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
    {
        debugInfo["CheckConstraint:All"] = "1";
    }
}