/*
Notes on VCL:

* Changed ":" to "!" in modifiers to prevent ambiguity with constraint separators
* Added ECL style designation queries
* Want to add grouping parens around constraints

*/
using Sprache;
using System.Collections.Immutable;
using SqlParser;

using NUnit.Framework;
using FluentAssertions;

public abstract record Expression;

public record ExpressionConjunction(List<Expression> Parts) : Expression
{
    public override string ToString()
    {
        string partsList = string.Join(", ", Parts.Select(p => p.ToString()));
        return $"ExpressionConjunction {{ Parts = [{partsList}] }}";
    }
}
public record ExpressionDisjunction(List<Expression> Parts) : Expression
{
    public override string ToString()
    {
        string partsList = string.Join(", ", Parts.Select(p => p.ToString()));
        return $"ExpressionDisjunction {{ Parts = [{partsList}] }}";
    }
}


public record ExpressionSimple(Concept Concepts, Constraint? Constraint = null) : Expression;

public record Concept
{
    public ConceptRoot Nucleus { get; init; }
    public RelationshipModifier? Hierarchy { get; init; }
    public ImmutableList<string> SuffixPath { get; init; }

    public Concept()
    {
        SuffixPath = ImmutableList<string>.Empty;
        Hierarchy = null;
    }

    public Concept(ConceptRoot nucleus, RelationshipModifier? hierarchy = null, IEnumerable<string>? suffixPath = null)
    {
        Nucleus = nucleus;
        Hierarchy = hierarchy;
        SuffixPath = suffixPath?.ToImmutableList() ?? ImmutableList<string>.Empty;
    }
    public override string ToString()
    {
        string suffixPathString = $" Suffix: ({string.Join(", ", SuffixPath)})";
        string hierarchyString = Hierarchy.HasValue ? $"Hierarchy: {Hierarchy}, " : "";
        return $"Concept {{ Nucleus = {Nucleus}{hierarchyString}{suffixPathString} }}";
    }

}


public abstract record ConceptRoot;

public record ConceptRootAll : ConceptRoot;

public record ConceptRootIdentifier(string Identifier) : ConceptRoot;

public record ConceptRootExpr(Expression Expr) : ConceptRoot;

public enum RelationshipModifier
{
    Descendant,
    DescendantOrSelf,
    Ancestor,
    AncestorOrSelf
}



public abstract record Constraint;
public record ConstraintSimple : Constraint
{
    public List<string> OnPath { get; init; }
    public Value Value { get; init; }
    public ConstraintModifier? Modifier { get; init; }
    public ConstraintSimple(List<string> onPath, Value value, ConstraintModifier? modifier = null)
    {
        OnPath = onPath;
        Value = value;
        Modifier = modifier;
    }
    public override string ToString()
    {
        return $"ConstraintSimple {{ OnPath={string.Join(", ", OnPath)}, Modifier={Modifier}, Value-{Value} }} ";
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


public record ConstraintConjunction : Constraint
{
    public List<Constraint> Parts { get; init; }
    public ConstraintConjunction(List<Constraint> parts)
    {
        Parts = parts;
    }

    public override string ToString()
    {
        string partsList = string.Join(", ", Parts.Select(p => p.ToString()));
        return $"ConstraintConjunction {{ Parts = [{partsList}] }}";
    }

}
public record ConstraintDisjunction : Constraint
{
    public List<Constraint> Parts { get; init; }
    public ConstraintDisjunction(List<Constraint> parts)
    {
        Parts = parts;
    }

    public override string ToString()
    {
        string partsList = string.Join(", ", Parts.Select(p => p.ToString()));
        return $"ConstraintDisjunction {{ Parts = [{partsList}] }}";
    }

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
        Parse.String("!regex").Return(ConstraintModifier.Regex)
            .Or(Parse.String("!memberof").Return(ConstraintModifier.MemberOf));

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
                from mod in Parse.String("!regex").Or(Parse.String("!other")).Optional().Select(v => v.IsDefined switch {
                    true => string.Join("",v.Get()),
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
                Modifier: vs.FirstOrDefault(pair => pair[0].StartsWith("term"))?.FirstOrDefault()?.Split('!').Length > 1 ? vs.FirstOrDefault(pair => pair[0].StartsWith("term"))?.FirstOrDefault()?.Split('!').LastOrDefault() : null,
                Value: vs.FirstOrDefault(pair => pair[0].StartsWith("term"))?.LastOrDefault()) as Constraint
        ).Or(
            from path in PropertyPath
            from modifier in ParseConstraintModifier.Optional()
            from _ in Ws(Parse.Char('='))
            from value in ValueConstant //TODO or Value?
            select new ConstraintSimple(path, value, modifier.IsEmpty ? null : modifier.Get()) as Constraint
        );


    private static readonly Parser<Constraint> Constraints =
        from first in Constraint
        from rest in
            (from sep in Ws(Parse.Char(',').Or(Parse.Char(';')))
             from c in Constraint
             select new { Sep = sep, Constraint = c }).Many()
        select rest.Aggregate(
            first,
            (acc, x) =>
            {
                if (x.Sep == ',')
                    return new ConstraintConjunction(new List<Constraint> { acc, x.Constraint });
                else
                    return new ConstraintDisjunction(new List<Constraint> { acc, x.Constraint });
            }
        );


    private static readonly Parser<Expression> SimpleExpr =
        (
            from concept in Parse.Ref(() => ParseConceptExpr)
            from _ in Ws(Parse.Char(':'))
            from constraint in Constraints
            select new ExpressionSimple(concept, constraint)
            )
        .Or(
                from constraint in Constraints
                select new ExpressionSimple(
                    new Concept { Nucleus = new ConceptRootAll() },
                    constraint
                )
        )
        .Or(
            from concept in Parse.Ref(() => ParseConceptExpr)
            select new ExpressionSimple(concept, null)
        );

    public static readonly Parser<Concept> ParseConceptExpr =
        from hierarchy in ParseRelationshipModifier.Optional()
        from _ in WhiteSpace
        from root in ParseConceptRoot
        from suffixPath in
            (from __ in Ws(Parse.Char('.'))
             from id in Identifier
             select id).Many()
        select new Concept
        {
            Nucleus = root,
            Hierarchy = hierarchy.IsEmpty ? null : hierarchy.Get(),
            SuffixPath = suffixPath.ToImmutableList()
        };


    public static Parser<ConceptRoot> ParseConceptRoot =
        Parse.Char('*').Select(_ => new ConceptRootAll())
            .Or(Identifier.Select(id => new ConceptRootIdentifier(id) as ConceptRoot))
            .Or(
                from _ in Parse.Char('(')
                from e in Parse.Ref(() => Expr)
                from __ in Parse.Char(')')
                select new ConceptRootExpr(e)
            );

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
                    return new ExpressionConjunction(new List<Expression> { acc, x.Expr });
                else
                    return new ExpressionDisjunction(new List<Expression> { acc, x.Expr });
            }
        );

}

[TestFixture]
public class VclParserTests
{
    [Test]
    public void TestSimpleExpression()
    {

        string input = "*";
        var result = VCLParser.Expr.Parse(input);

        var l = new List<string> { "oh", "hai" };
        var concept = new Concept
        {
            Nucleus = new ConceptRootAll(),
            SuffixPath = new[] { "oh", "hai" }.ToImmutableList()
        };
        var concept2 = new Concept
        {
            Nucleus = new ConceptRootAll(),
            SuffixPath = new[] { "oh", "hai" }.ToImmutableList()
        };



        concept.Should().BeEquivalentTo(concept2);

        var expected = new ExpressionSimple(
                Constraint: null,
                Concepts: new Concept
                {
                    Nucleus = new ConceptRootAll(),
                });

        result.Should().BeEquivalentTo(expected);

        Assert.That(result, Is.TypeOf<ExpressionSimple>());
    }

    [Test]
    public void TestExpressionConjunction()
    {
        string input = "a,b";
        var result = VCLParser.Expr.Parse(input);
        Console.WriteLine(result);
        Assert.That(result, Is.TypeOf<ExpressionConjunction>());
    }

    [Test]
    public void TestExpressionDisjunction()
    {
        string input = "a.b;c";
        var result = VCLParser.Expr.End().Parse(input);
        Console.WriteLine(result);
        Assert.That(result, Is.TypeOf<ExpressionDisjunction>());
        string input2 = "(<<'Medications'):'common use'='Treat bacterial infections','administration route'='Oral'";

        Expression result2 = VCLParser.Expr.Parse(input2);
        Console.WriteLine($"r2 {result2:#?}");

        var b = new QueryBuilder();
        var sql = b.BuildSqlSimple(result2 as ExpressionSimple);
        Console.WriteLine(sql);

        foreach (var p in b.injector.Parameters)
        {
            Console.WriteLine(p);

        }

        var ast = new SqlParser.Parser().ParseSql("" + sql).Single();
        var s = ast.ToSql();
        Console.WriteLine($"SQL: {s}");
    }

    [Test]
    public void DesNext()
    {
        // string input = "*:a=1, {{ term!regex='fine' lang=en useSystem='https://ok' useCode=213}}";
        string input = "(<<(*:RXTERM_FORM=Cap)):tty=SBD,{{term=c}}";
        var result = VCLParser.Expr.Parse(input);
        Console.WriteLine(result);
    }

}
