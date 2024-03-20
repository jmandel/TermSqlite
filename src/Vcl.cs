/*
Notes on VCL:

* Removed  "*:" syntax -- no more concept blocks at the beginning of expressions
* Added ECL style designation queries
* Want to add grouping parens around constraints

*/
using Sprache;

using NUnit.Framework;
using FluentAssertions;
using SqlParser;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

[JsonPolymorphic]
[JsonDerivedType(typeof(SegregatedConjunction), typeDiscriminator: "SegregatedConjunction")]
[JsonDerivedType(typeof(SegregatedDisjunction), typeDiscriminator: "SegregatedDisjunctiond")]
[JsonDerivedType(typeof(ExpressionConstraint), typeDiscriminator: "ExpressionConstraint")]
[JsonDerivedType(typeof(ExpressionConstant), typeDiscriminator: "ExpressionConstant")]

public abstract record Expression
{
    public virtual Expression Simplify() => this;
    public virtual Expression RestructureForQuery() => this;

    protected static bool ContainsHierarchy(Expression expression)
    {
        return expression switch
        {
            ExpressionHierarchy => true,
            ExpressionConjunction conjunction => conjunction.Parts.Any(ContainsHierarchy),
            ExpressionDisjunction disjunction => disjunction.Parts.Any(ContainsHierarchy),
            SegregatedConjunction segregatedConjunction => segregatedConjunction.HierarchyParts.Any() || segregatedConjunction.LogicalParts.Any(ContainsHierarchy),
            SegregatedDisjunction segregatedDisjunction => segregatedDisjunction.HierarchyParts.Any() || segregatedDisjunction.LogicalParts.Any(ContainsHierarchy),
            _ => false
        };
    }

}
public record SegregatedConjunction(List<Expression> HierarchyParts, List<Expression> LogicalParts) : Expression{

    public override string ToString()
    {
        string hPartsList = string.Join(", ", HierarchyParts.Select(p => p.ToString()));
        string lPartsList = string.Join(", ", LogicalParts.Select(p => p.ToString()));
        return $"\nSegregatedConjunction {{\n HierarchyParts = [{hPartsList}]\n  LogicalParts = [{lPartsList}] }}";
    }


}

public record SegregatedDisjunction(List<Expression> HierarchyParts, List<Expression> LogicalParts) : Expression{
    public override string ToString()
    {
        string hPartsList = string.Join(", ", HierarchyParts.Select(p => p.ToString()));
        string lPartsList = string.Join(", ", LogicalParts.Select(p => p.ToString()));
        return $"SegregatedDisjunction {{\n HierarchyParts = [{hPartsList}]\n  LogicalParts = [{lPartsList}] }}";
    }


}

public record ExpressionConjunction(List<Expression> Parts) : Expression
{
    public override Expression RestructureForQuery()
    {
        var hierarchyParts = new List<Expression>();
        var logicalParts = new List<Expression>();

        foreach (var part in Parts)
        {
            var restructuredPart = part.RestructureForQuery();
            if (ContainsHierarchy(restructuredPart))
            {
                hierarchyParts.Add(restructuredPart);
            }
            else
            {
                logicalParts.Add(restructuredPart);
            }
        }

        return new SegregatedConjunction(hierarchyParts, logicalParts);
    }

    public override Expression Simplify()
    {
        var simplifiedParts = Parts.Select(p => p.Simplify()).ToList();
        var conjunctionParts = new List<Expression>();
        foreach (var part in simplifiedParts)
        {
            if (part is ExpressionConjunction conjunction)
            {
                conjunctionParts.AddRange(conjunction.Parts);
            }
            else
            {
                conjunctionParts.Add(part);
            }
        }
        return new ExpressionConjunction(conjunctionParts);


    }
    public override string ToString()
    {
        string partsList = string.Join(", ", Parts.Select(p => p.ToString()));
        return $"ExpressionConjunction {{ Parts = [{partsList}] }}";
    }


}

public record ExpressionDisjunction(List<Expression> Parts) : Expression
{
    public override Expression Simplify()
    {
        var simplifiedParts = Parts.Select(p => p.Simplify()).ToList();
        var disjunctionParts = new List<Expression>();
        foreach (var part in simplifiedParts)
        {
            if (part is ExpressionDisjunction disjunction)
            {
                disjunctionParts.AddRange(disjunction.Parts);
            }
            else
            {
                disjunctionParts.Add(part);
            }
        }
        return new ExpressionDisjunction(disjunctionParts);
    }
    public override Expression RestructureForQuery()
    {
        var hierarchyParts = new List<Expression>();
        var logicalParts = new List<Expression>();

        foreach (var part in Parts)
        {
            var restructuredPart = part.RestructureForQuery();
            if (ContainsHierarchy(restructuredPart))
            {
                hierarchyParts.Add(restructuredPart);
            }
            else
            {
                logicalParts.Add(restructuredPart);
            }
        }

        return new SegregatedDisjunction(hierarchyParts, logicalParts);
    }


    public override string ToString()
    {
        string partsList = string.Join(", ", Parts.Select(p => p.ToString()));
        return $"ExpressionDisjunction {{ Parts = [{partsList}] }}";
    }


}

public record ExpressionConstraint(Constraint Constraint) : Expression;

public record ExpressionConstant(string Code) : Expression;

public record ExpressionHierarchy(RelationshipModifier Modifier, Expression Expression) : Expression
{
    public override Expression Simplify()
    {
        var simplifiedExpression = Expression.Simplify();
        if (Modifier == RelationshipModifier.Self)
        {
            return simplifiedExpression;
        }
        return this with { Expression = simplifiedExpression };
    }
}



public enum RelationshipModifier
{
    Self,
    Descendant,
    DescendantOrSelf,
    Ancestor,
    AncestorOrSelf
}



[JsonPolymorphic]
[JsonDerivedType(typeof(ConstraintProperty), typeDiscriminator: "ConstraintProperty")]
[JsonDerivedType(typeof(ConstraintDesignation), typeDiscriminator: "ConstraintDesignation")]
public abstract record Constraint;
public record ConstraintProperty : Constraint
{
    public List<string> OnPath { get; init; }
    public Value Value { get; init; }
    public ConstraintModifier? Modifier { get; init; }
    public ConstraintProperty(List<string> onPath, Value value, ConstraintModifier? modifier = null)
    {
        OnPath = onPath;
        Value = value;
        Modifier = modifier;
    }
    public override string ToString()
    {
        return $"ConstraintProperty {{ OnPath={string.Join(", ", OnPath)}, Modifier={Modifier}, Value-{Value} }} ";
    }
}

public record ConstraintDesignation(string? Language = null, string? UseSystem = null, string? UseCode = null, string? Modifier = null, string? Value = null) : Constraint
{
    public ConstraintDesignation(string[][] vs) : this(
)
    {
    }

    public override string ToString() => $"Designation {{{Value} ({Language ?? "null"}, {UseSystem ?? "null"}, {UseCode ?? "null"}, {Modifier ?? "null"})}}";
}


public enum ConstraintModifier
{
    Regex,
    MemberOf
}

[JsonPolymorphic]
[JsonDerivedType(typeof(ValueConstant), typeDiscriminator: "ValueConstant")]
public abstract record Value;

public record ValueConstant(object Constant) : Value;

public record ValueExpr(Expression Expr) : Value;

public class VCLParser
{
    private static readonly Parser<char> WhiteSpace = Parse.WhiteSpace.Many().Select(_ => ' ');

    private static Parser<T> Ws<T>(Parser<T> parser) =>
        from _ in WhiteSpace
        from item in parser
        from __ in WhiteSpace
        select item;


    // Func<char, bool> isValidChar = c => char.IsLetterOrDigit(c) || c == '_' || c == '-';

    private static readonly Predicate<char> IsValidIdentifierChar = c => char.IsLetterOrDigit(c) || c == '_' || c == '-';

    private static readonly Parser<string> Identifier =
        (
            from _ in Parse.Char('\'')
            from s in Parse.CharExcept('\'').Many().Text()
            from __ in Parse.Char('\'')
            select s
        )
        .Or(Parse.Char(IsValidIdentifierChar, "identifier chars").AtLeastOnce().Select(cs => string.Join("", cs)));
    private static readonly Parser<RelationshipModifier> ParseRelationshipModifier =
        Parse.String("<<").Return(RelationshipModifier.DescendantOrSelf)
            .Or(Parse.String("<").Return(RelationshipModifier.Descendant))
            .Or(Parse.String(">>").Return(RelationshipModifier.AncestorOrSelf))
            .Or(Parse.String(">").Return(RelationshipModifier.Ancestor));

    private static readonly Parser<ConstraintModifier> ParseConstraintModifier =
        Parse.String(":regex").Return(ConstraintModifier.Regex)
            .Or(Parse.String(":memberof").Return(ConstraintModifier.MemberOf));

    private static readonly Parser<Value> ValueConstant =
        Parse.Decimal.Select(d => new ValueConstant(d))
            .Or(Parse.String("true").Return(new ValueConstant(true)))
            .Or(Parse.String("false").Return(new ValueConstant(false)))
            .Or(
                from _ in Parse.Char('\'')
                from s in Parse.CharExcept('\'').Many().Text()
                from __ in Parse.Char('\'')
                select new ValueConstant(s)
            )
            .Or(Parse.LetterOrDigit.AtLeastOnce().Text().Select(s => new ValueConstant(s)));

    private static readonly Parser<Value> Value =
        ValueConstant
            .Or(
                from _ in Parse.Char('(')
                from e in Expr
                from __ in Parse.Char(')')
                select new ValueExpr(e)
            );

    private static readonly Parser<List<string>> PropertyPath =
        Identifier.DelimitedBy(Parse.Char('.')).Select(s => s.ToList());

    public static readonly Parser<Constraint> Constraint =
        (
            from __ in Ws(Parse.String("{{"))
            from vs in (
                from key in Ws(Identifier)
                from mod in Parse.String(":regex").Or(Parse.String(":other")).Optional().Select(v => v.IsDefined switch
                {
                    true => string.Join("", v.Get()),
                    false => ""
                })
                from _ in Ws(Parse.Char('='))
                from value in Ws(ValueConstant)
                select new string[] {key+mod, value switch {
                    ValueConstant(var c) => c.ToString() ?? "",
                    _ => ""
                }}).AtLeastOnce()
            from ___ in Ws(Parse.String("}}"))
            select new ConstraintDesignation(
                Language: vs.FirstOrDefault(pair => pair[0] == "lang")?[1],
                UseSystem: vs.FirstOrDefault(pair => pair[0] == "useSystem")?[1],
                UseCode: vs.FirstOrDefault(pair => pair[0] == "useCode")?[1],
                Modifier: vs.FirstOrDefault(pair => pair[0].StartsWith("term"))?.FirstOrDefault()?.Split(':').Length > 1 ? vs.FirstOrDefault(pair => pair[0].StartsWith("term"))?.FirstOrDefault()?.Split(':').LastOrDefault() : null,
                Value: vs.FirstOrDefault(pair => pair[0].StartsWith("term"))?.LastOrDefault()) as Constraint
        ).Or(
            from path in PropertyPath
            from modifier in ParseConstraintModifier.Optional()
            from _ in Ws(Parse.Char('='))
            from value in ValueConstant //TODO or Value?
            select new ConstraintProperty(path, value, modifier.IsEmpty ? null : modifier.Get()) as Constraint
        );


    //TODO excise this now that we don't managed Disjunction/Conjunction within constraints
    private static readonly Parser<Constraint> Constraints =
        from first in Constraint
        select first;


    // TODO add back support for suffix paths?
    private static readonly Parser<Expression> SimpleExpr =
        from hierarchy in Ws(ParseRelationshipModifier).Optional().Select(h => h.GetOrElse(RelationshipModifier.Self))
        from c in ((
            from _ in Parse.Char('(')
            from e in Expr
            from __ in Parse.Char(')')
            select e
        ).Or(
            from constraint in Constraints
            select new ExpressionConstraint(
                constraint
            )
        ).Or(
            from value in Value
            select value switch
            {
                ValueConstant(string c) => new ExpressionConstant(c),
                ValueExpr(var e) => e,
                _ => throw new Exception("Unexpected value type in expression")
            }
        ))
        select new ExpressionHierarchy(hierarchy, c).Simplify();

    private static readonly Parser<char> Sep =
        Parse.Char(',').Or(Parse.Char(';'));

    public static readonly Parser<Expression> Expr =
        from first in SimpleExpr
        from rest in
            (from sep in Ws(Sep)
             from e in SimpleExpr
             select new { Sep = sep, Expr = e }).Many()
        select rest.Aggregate(
            first,
            (acc, x) =>
            {
                if (x.Sep == ',')
                    return new ExpressionConjunction(new List<Expression> { acc, x.Expr }).Simplify();
                else
                    return new ExpressionDisjunction(new List<Expression> { acc, x.Expr }).Simplify();
            }
        );

}

[TestFixture]
public class VclParserTests
{
    [Test]
    public void TestExpressionConstant()
    {
        string input = "a";
        var result = VCLParser.Expr.Parse(input);
        Assert.That(result, Is.TypeOf<ExpressionConstant>());
    }


    [Test]
    public void TestSimpleExpression()
    {

        string input = "a=1";
        var result = VCLParser.Expr.Parse(input);

        var expected = new ExpressionConstraint(
                Constraint: new ConstraintProperty(new() { "a" }, new ValueConstant("1"), null)
                );

        result.Should().BeEquivalentTo(expected);

        Assert.That(result, Is.TypeOf<ExpressionConstraint>());
    }

    [Test]
    public void TestExpressionConjunction()
    {
        string input = "a,b";
        var result = VCLParser.Expr.Parse(input);
        // Console.WriteLine(result);
        Assert.That(result, Is.TypeOf<ExpressionConjunction>());
    }

    [Test]
    public void TestExpressionDisjunction()
    {
        string input = "a;b";
        var result = VCLParser.Expr.End().Parse(input);
        // Console.WriteLine(result);
        Assert.That(result, Is.TypeOf<ExpressionDisjunction>());
    }

    [Test]
    public void DesNext()
    {
        string input = "(<<RXTERM_FORM=Cap),tty=SBD,{{term=c}}";
        var result = VCLParser.Expr.Parse(input);
        // Console.WriteLine(result);

        string input2 = "a=1,b=2;c=3";

        Expression result2 = VCLParser.Expr.Parse(input2);

        Console.WriteLine($"r2 {result2}");
        Expression result3 = result2.RestructureForQuery();
        Console.WriteLine($"r3 {result3}");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new JsonStringEnumConverter());
        var json = JsonSerializer.Serialize(result3, options);
        Console.WriteLine(json);


        var b = new QueryBuilder();
        var sql = b.Build(result2);
        Console.WriteLine(sql);

        foreach (var p in b.injector.Parameters)
        {
            Console.WriteLine(p);

        }

        var ast = new SqlParser.Parser().ParseSql("" + sql).Single();
        var s = ast.ToSql();
        Console.WriteLine($"SQL: {s}");

    }

}
