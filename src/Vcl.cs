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

public abstract record Expression
{
    public virtual Expression Simplify() => this;


}

public abstract record ExpressionLogical(List<Expression> Parts, Expression? Nucleus = null) : Expression
{
    protected static bool HasHierarchyBelow(Expression expression)
    {
        return expression switch
        {
            ExpressionHierarchy => true,
            ExpressionLogical logical => logical.Parts.Any(HasHierarchyBelow),
            _ => false
        };
    }
    public override Expression Simplify()
    {
        var simplifiedParts = Parts.Select(p => p.Simplify()).ToList();
        var logicalParts = new List<Expression>();

        foreach (var part in simplifiedParts)
        {
            if (part.GetType() == GetType())
            {
                var logical = (ExpressionLogical)part;
                logicalParts.AddRange(logical.Parts);
                if (logical.Nucleus != null)
                {
                    logicalParts.Add(logical.Nucleus);
                }
            }
            else
            {
                logicalParts.Add(part);
            }
        }

        if (Nucleus != null) {
            logicalParts.Add(Nucleus);
        }

        return CreateExpression(logicalParts, null);
    }

    protected abstract ExpressionLogical CreateExpression(List<Expression> parts, Expression? nucleus);

    public override string ToString()
    {
        string partsList = string.Join(", ", Parts.Select(p => p.ToString()));
        return $"{GetType().Name} {{ Nucleus = {Nucleus} , Parts = [{string.Join(" . " , partsList)}]}}";
    }
}

public record ExpressionConjunction(List<Expression> Parts, Expression? Nucleus = null) : ExpressionLogical(Parts, Nucleus)
{
    public override Expression Simplify()
    {

        (var simplifiedParts, _) = (ExpressionConjunction)base.Simplify();

        var hierarchyParts = simplifiedParts.Where(p => HasHierarchyBelow(p)).ToList();

        Expression? nucleus = null;

        if (hierarchyParts.Count == 0)
        {
            nucleus = simplifiedParts.FirstOrDefault();
            if (nucleus != null)
            {
                simplifiedParts.Remove(nucleus);
            }
        }
        else if (hierarchyParts.Count == 1)
        {
            nucleus = hierarchyParts[0];
            simplifiedParts.Remove(nucleus);
        }

        return CreateExpression(simplifiedParts, nucleus);
    }


    protected override ExpressionLogical CreateExpression(List<Expression> parts, Expression? nucleus)
    {
        return new ExpressionConjunction(parts, nucleus);
   }
    public override string ToString()
    {
        return $"Conjunction {base.ToString()}";
    }

}

public record ExpressionDisjunction(List<Expression> Parts, Expression? Nucleus = null) : ExpressionLogical(Parts, Nucleus)
{
    protected override ExpressionLogical CreateExpression(List<Expression> parts, Expression? nucleus)
    {
        return new ExpressionDisjunction(parts, nucleus);
    }
    public override string ToString()
    {
        return $"Disjunction {base.ToString()}";
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

        string input2 = "d=4;(a=1,b=2);c=3";

        Expression result2 = VCLParser.Expr.Parse(input2);
        Console.WriteLine($"r2 {result2}");

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
