using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Darkarotte.CheckConstraints;

public abstract class CheckConstraint
{
    protected readonly PropertyBuilder PropertyBuilder;

    public CheckConstraint(PropertyBuilder propertyBuilder)
    {
        PropertyBuilder = propertyBuilder;
    }

    public abstract void Configure(IConventionModelBuilder builder);

    public string GetCheckConstraintName()
        => $"CK_" +
           $"{PropertyBuilder.Metadata.DeclaringEntityType.GetTableName()}" +
           $"_" +
           $"{PropertyBuilder.Metadata.GetColumnName()}" +
           $"_" +
           $"{ConstraintTypeName}";

    protected virtual string ConstraintTypeName
        => GetType()
            .Name
            .Replace("Check", "")
            .Replace("Constraint", "");
}

internal class EnumConstraint : CheckConstraint
{
    public EnumConstraint(PropertyBuilder propertyBuilder)
        : base(propertyBuilder)
    {
    }

    public override void Configure(IConventionModelBuilder builder)
    {
        var entityTypeBuilder = builder.Entity(PropertyBuilder.Metadata.DeclaringEntityType.ClrType)!;
        var columnName = PropertyBuilder.Metadata.GetColumnName();

        var converter = PropertyBuilder.Metadata.GetValueConverter();

        // get enum values and convert them to the database type as a linq expression
        var enumValues = Enum.GetValues(PropertyBuilder.Metadata.ClrType)
            .Cast<object>()
            .Select(x => converter?.ConvertToProvider(x) ?? x)
            .Select(x => x is string s ? $"'{s}'" : x.ToString());

        var sql = new StringBuilder();

        if (PropertyBuilder.Metadata.ClrType == typeof(string) && PropertyBuilder.Metadata.IsNullable)
            sql.Append($"\"{columnName}\" IS NULL OR ");

        sql.Append($"\"{columnName}\" IN (");
        sql.AppendJoin(", ", enumValues);
        sql.Append(")");

        entityTypeBuilder.HasCheckConstraint(GetCheckConstraintName(), sql.ToString());
    }
}

public class RegexConstraint : CheckConstraint
{
    private readonly string _regex;
    private readonly string? _name;

    public RegexConstraint(PropertyBuilder propertyBuilder, string regex, string? name = null)
        : base(propertyBuilder)
    {
        _regex = regex;
        _name = name;
    }

    public override void Configure(IConventionModelBuilder builder)
    {
        var entityTypeBuilder = builder.Entity(PropertyBuilder.Metadata.DeclaringEntityType.ClrType)!;
        var columnName = PropertyBuilder.Metadata.GetColumnName();

        var sqlBuilder = new StringBuilder();

        if (PropertyBuilder.Metadata.ClrType == typeof(string) && PropertyBuilder.Metadata.IsNullable)
            sqlBuilder.Append($"\"{columnName}\" IS NULL OR ");

        sqlBuilder.Append($"\"{columnName}\" ~ '{_regex}'");

        entityTypeBuilder.HasCheckConstraint(GetCheckConstraintName(), sqlBuilder.ToString());
    }

    protected override string ConstraintTypeName
    {
        get
        {
            var postFix = "";
            if (string.IsNullOrWhiteSpace(_name))
            {
                postFix = "_" + _name;
            }
            return base.ConstraintTypeName + postFix;
        }
    }
}

public class LengthConstraint : CheckConstraint
{
    private readonly uint _maxLength;
    private readonly uint? _minLength;

    public LengthConstraint(PropertyBuilder propertyBuilder, uint maxLength, uint? minLength)
        : base(propertyBuilder)
    {
        _maxLength = maxLength;
        _minLength = minLength;
    }

    public override void Configure(IConventionModelBuilder builder)
    {
        var entityTypeBuilder = builder.Entity(PropertyBuilder.Metadata.DeclaringEntityType.ClrType)!;
        var columnName = PropertyBuilder.Metadata.GetColumnName();

        var sqlBuilder = new StringBuilder();

        if (PropertyBuilder.Metadata.ClrType == typeof(string) && PropertyBuilder.Metadata.IsNullable)
            sqlBuilder.Append($"\"{columnName}\" IS NULL OR ");

        sqlBuilder.Append($"LENGTH(\"{columnName}\") <= {_maxLength}");
        if (_minLength.HasValue)
        {
            sqlBuilder.Append($" AND LENGTH(\"{columnName}\") >= {_minLength}");
        }

        entityTypeBuilder.HasCheckConstraint(GetCheckConstraintName(), sqlBuilder.ToString());
    }
}

public static class ConstraintsBuilder
{
    private static readonly Dictionary<Type, List<CheckConstraint>> Constraints = new();

    internal static bool TryGetConstraints(Type type, [NotNullWhen(true)] out List<CheckConstraint>? constraints)
    {
        return Constraints.TryGetValue(type, out constraints);
    }

    public static PropertyBuilder<T> HasCustomConstraint<T>(
        this PropertyBuilder<T> propertyBuilder, Func<PropertyBuilder<T>, CheckConstraint> constraintFactory)
    {
        var constraint = constraintFactory(propertyBuilder);
        AddConstraint(propertyBuilder, constraint);
        return propertyBuilder;
    }

    public static PropertyBuilder<string> HasRegexConstraint(this PropertyBuilder<string> propertyBuilder, string regex)
    {
        InternalHasRegexConstraint(propertyBuilder, regex);
        return propertyBuilder;
    }
    
    public static PropertyBuilder<string?> HasNullableRegexConstraint(this PropertyBuilder<string?> propertyBuilder, string regex)
    {
        InternalHasRegexConstraint(propertyBuilder, regex);
        return propertyBuilder;
    }
    
    private static void InternalHasRegexConstraint(this PropertyBuilder propertyBuilder, string regex)
    {
        var constraint = new RegexConstraint(propertyBuilder, regex);
        AddConstraint(propertyBuilder, constraint);
    }

    public static PropertyBuilder<string> HasLengthConstraint(this PropertyBuilder<string> propertyBuilder,
        uint maxLength, uint? minLength = null)
    {
        InternalHasLengthConstraint(propertyBuilder, maxLength, minLength);
        return propertyBuilder;
    }

    public static PropertyBuilder<string?> HasNullableLengthConstraint(this PropertyBuilder<string?> propertyBuilder,
        uint maxLength, uint? minLength = null)
    {
        InternalHasLengthConstraint(propertyBuilder, maxLength, minLength);
        return propertyBuilder;
    }

    private static void InternalHasLengthConstraint(PropertyBuilder propertyBuilder, uint maxLength,
        uint? minLength = null)
    {
        var constraint = new LengthConstraint(propertyBuilder, maxLength, minLength);
        AddConstraint(propertyBuilder, constraint);
    }

    public static PropertyBuilder<T> HasEnumConstraint<T>(this PropertyBuilder<T> propertyBuilder)
        where T : struct, Enum
    {
        InternalHasEnumConstraint(propertyBuilder);
        return propertyBuilder;
    }

    public static PropertyBuilder<T?> HasNullableEnumConstraint<T>(this PropertyBuilder<T?> propertyBuilder)
        where T : struct, Enum
    {
        InternalHasEnumConstraint(propertyBuilder);
        return propertyBuilder;
    }

    private static void InternalHasEnumConstraint(this PropertyBuilder propertyBuilder)
    {
        var constraint = new EnumConstraint(propertyBuilder);
        AddConstraint(propertyBuilder, constraint);
    }

    public static PropertyBuilder<string> HasEmailConstraint(this PropertyBuilder<string> propertyBuilder)
    {
        InternalHasEmailConstraint(propertyBuilder);
        return propertyBuilder;
    }

    public static PropertyBuilder<string?> HasNullableEmailConstraint(this PropertyBuilder<string?> propertyBuilder)
    {
        InternalHasEmailConstraint(propertyBuilder);
        return propertyBuilder;
    }

    public static string EmailRegex => @"^([\w\.\-]+)@([\w\-]+)((\.(\w){2,5})+)$";

    private static void InternalHasEmailConstraint(this PropertyBuilder propertyBuilder)
    {
        var constraint = new RegexConstraint(propertyBuilder, EmailRegex, "Email");
        AddConstraint(propertyBuilder, constraint);
    }

    private static void AddConstraint(PropertyBuilder propertyBuilder, CheckConstraint constraint)
    {
        var entityType = propertyBuilder.Metadata.DeclaringEntityType.ClrType;
        if (!Constraints.ContainsKey(entityType))
        {
            Constraints.Add(entityType, new List<CheckConstraint>());
        }

        Constraints[entityType].Add(constraint);
    }
}