using Sprache;
using SqlParser.Tokens;

public record struct FhirConcept
{
    public string? System { get; init; }
    public string? Code { get; init; }
    public string? Display { get; init; }
}

public class SystemNotFoundException : Exception
{
    public SystemNotFoundException(string system) : base($"Cannot find system: {system}")
    {
    }
}

public class VclManager
{
    public static async IAsyncEnumerable<FhirConcept> ExecuteAsync(string codeSystem, string query, SqliteManager sqliteManager)
    {

        Db targetDb = sqliteManager.GetDbByCanonicalSystem(codeSystem) ?? throw new SystemNotFoundException(codeSystem);
        var parsed = VCLParser.Expr.Parse(query);
        var qb = new QueryBuilder { DbName = targetDb.Name };
        await foreach (var row in sqliteManager.QueryAsync(qb.Build(parsed), qb.Parameters, new List<string>() { targetDb.Name }))
        {
            yield return new FhirConcept { System = codeSystem, Code = (string)row["code"], Display = Convert.ToString(row["display"]) ?? "" };
        }
    }
}

public class QueryBuilder
{
    public SqlParamInjector injector = new SqlParamInjector();
    public string DbName { get; set; } = "db";
    public IReadOnlyDictionary<string, object> Parameters => injector.Parameters;
    public string Build(Expression expr)
    {
        var restructuredExpr = expr.RestructureForQuery();
        return $"SELECT * FROM {DbName}.Concepts WHERE id IN ({BuildSql(restructuredExpr, "Start")})";
    }

    private string BuildSql(Expression expr, string context)
    {
        return expr switch
        {
            ExpressionConstraint constraint => BuildSqlExpressionConstraint(constraint, context),
            SegregatedConjunction segregatedConjunction => BuildSqlSegregatedConjunction(segregatedConjunction, context),
            SegregatedDisjunction segregatedDisjunction => BuildSqlSegregatedDisjunction(segregatedDisjunction, context),
            _ => throw new NotImplementedException()
        };
    }

        private string BuildSqlSegregatedConjunction(SegregatedConjunction segregatedConjunction, string context)
    {
        var hierarchySubqueries = segregatedConjunction.HierarchyParts.Select(part => BuildSql(part, "Start")).ToList();
        var hierarchyQuery = hierarchySubqueries.Any() ? $"({string.Join(" INTERSECT ", hierarchySubqueries)})" : $"SELECT id FROM {DbName}.Concepts";
        var logicalQuery = BuildSqlLogicalParts(segregatedConjunction.LogicalParts, "AND");

        return context == "Start"
            ? $"SELECT id FROM ({hierarchyQuery}) AS parent WHERE {logicalQuery}"
            : logicalQuery;
    }


    private string BuildSqlSegregatedDisjunction(SegregatedDisjunction segregatedDisjunction, string context)
    {
        var hierarchySubqueries = segregatedDisjunction.HierarchyParts.Select(part => BuildSql(part, "Start")).ToList();
        var hierarchyQuery = hierarchySubqueries.Any() ? $"({string.Join(" UNION ", hierarchySubqueries)})" : $"SELECT id FROM {DbName}.Concepts";
        var logicalQuery = BuildSqlLogicalParts(segregatedDisjunction.LogicalParts, "OR");

        return context == "Start"
            ? $"SELECT id FROM ({hierarchyQuery}) AS parent WHERE {logicalQuery}"
            : logicalQuery;
    }

private string BuildSqlLogicalParts(List<Expression> logicalParts, string logicalOperator)
{
    var conditions = logicalParts.Select(part => BuildSqlLogicalPart(part)).ToList();
    return $"({string.Join($" {logicalOperator} ", conditions)})";
}

private string BuildSqlLogicalPart(Expression part)
{
    return part switch
    {
        ExpressionConstraint constraint => $"EXISTS ({BuildSqlConstraint(constraint.Constraint)} AND m0.concept_id = parent.id)",
        SegregatedConjunction segregatedConjunction => BuildSqlLogicalParts(segregatedConjunction.LogicalParts, "AND"),
        SegregatedDisjunction segregatedDisjunction => BuildSqlLogicalParts(segregatedDisjunction.LogicalParts, "OR"),
        _ => throw new NotImplementedException()
    };
}


    private string BuildSqlExpressionConstraint(ExpressionConstraint constraint, string context)
    {
        var query = BuildSqlConstraint(constraint.Constraint);

        return context == "Start"
            ? $"SELECT id FROM {DbName}.Concepts AS parent WHERE EXISTS ({query})"
            : query;
    }
 



    private string BuildSubquery(Expression expr)
    {
        return BuildSql(expr, "Start");
    }

    private string BuildSqlExpressionConjunction(List<Expression> exprs)
    {
        var subqueries = exprs.Select(BuildSubquery).ToList();
        return string.Join(" INTERSECT ", subqueries);
    }

    private string BuildSqlExpressionDisjunction(List<Expression> exprs)
    {
        var subqueries = exprs.Select(BuildSubquery).ToList();
        return string.Join(" UNION ", subqueries);
    }

    private string BuildSqlConstraintSimple(ConstraintProperty constraint)
    {
        var constraintValue = constraint.Value switch
        {
            ValueConstant constant => constant.Constant.ToString(),
            // ValueExpr expr => BuildSubquery(expr.Expr),
            _ => throw new ArgumentException("Invalid constraint value type")
        };

        var joinConditions = new List<string>();
        var propertyAliases = new List<string>();
        var conceptAliases = new List<string>();
        var whereConditions = new List<string>();

        var constraintTargetParamResult = injector.With(new Dictionary<string, object> { ["constraintTarget"] = constraintValue! });

        for (int i = 0; i < constraint.OnPath.Count; i++)
        {
            var propertyAlias = $"prop{i}";
            propertyAliases.Add(propertyAlias);

            var propertyCodeParam = $"propertyCode{i}";
            var propertyCodeParamResult = injector.With(new Dictionary<string, object> { [propertyCodeParam] = constraint.OnPath[i] });

            var conceptAlias = $"concept{i}";
            conceptAliases.Add(conceptAlias);


            joinConditions.Add(@$"
            {((i == 0) ?
                    $"ConceptProperty m{i}" :
                    $"JOIN ConceptProperty m{i} ON m{i - 1}.target_concept_id = m{i}.concept_id")}");

            whereConditions.Add($" m{i}.property_code = @{propertyCodeParamResult[propertyCodeParam]}");


            if (i == constraint.OnPath.Count - 1)
            {
                whereConditions.Add($" m{i}.target_value = @{constraintTargetParamResult["constraintTarget"]}");
            }

        }

        var sql = $@"
        select m0.concept_id as id from 
            {string.Join("\n", joinConditions)}
            WHERE
             {string.Join(" AND ", whereConditions)}
    ";

        return sql;
    }


    private string BuildSqlConstraintDesignation(ConstraintDesignation designation)
    {
        return injector.With(new Dictionary<string, object>
        {
            ["language"] = designation.Language!,
            ["useSystem"] = designation.UseSystem!,
            ["useCode"] = designation.UseCode!,
            ["value"] = string.Format("%{0}%", designation.Value)
        }.Where(kv => kv.Value is not null).ToDictionary(), (p) => $@"
        select concept_id as id from {DbName}.ConceptDesignations m0
            where 
                {(designation.Language != null ? "and language = @language" : "")}
                {(designation.UseSystem != null ? "and use_system = @useSystem" : "")}
                {(designation.UseCode != null ? "and use_value = @useCode" : "")}
                {((designation.Language != null || designation.UseSystem != null || designation.UseCode != null)
                    ? " and " : "")} value LIKE @{p["value"]}

        ").Item1;
    }

    private string BuildSqlConstraint(Constraint constraint)
    {
        return constraint switch
        {
            ConstraintProperty simple => BuildSqlConstraintSimple(simple),
            ConstraintDesignation designation => BuildSqlConstraintDesignation(designation),
            _ => throw new NotImplementedException(),
        };
    }

    



    public string BuildSqlExpressionHierarchy(ExpressionHierarchy expr)
    {
        // ConceptRoot nucleus = expr.Concepts.Nucleus;
        string nucleusQuery = BuildSql(expr.Expression, "Start");

        string expandedQuery = expr.Modifier switch
        {
            RelationshipModifier.Descendant => $"select descendant_id as id from {DbName}.MaterializedHierarchy where ancestor_id in ({nucleusQuery})",
            RelationshipModifier.DescendantOrSelf => $"select descendant_id as id from {DbName}.MaterializedHierarchy where ancestor_id in ({nucleusQuery}) UNION {nucleusQuery}",
            RelationshipModifier.Ancestor => $"select ancestor_id as id from {DbName}.MaterializedHierarchy where descendant_id in ({nucleusQuery})",
            RelationshipModifier.AncestorOrSelf => $"select ancestor_id as id from {DbName}.MaterializedHierarchy where descendant_id in ({nucleusQuery}) UNION {nucleusQuery}",
            _ => nucleusQuery
        };

        var query = $@" select id from  ({expandedQuery}) as parent";

        return query;
    }

    public string BuildSqlExpressionConstant(ExpressionConstant expr)
    {
        return injector.With(new Dictionary<string, object> { ["constant"] = expr.Code },
           (p) => @$" select id from  {DbName}.Concepts where code=@{p["constant"]}").Item1;
    }


    public string BuildSqlExpressionConstraint(ExpressionConstraint expr)
    {
        var query = BuildSqlConstraint(expr.Constraint);
        // var query = $@" select id from  {DbName}.Concepts as parent where {conditions}";
        // return query;
        return query;
    }
}
