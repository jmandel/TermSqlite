using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

public class SqlParamInjector
{
    private Dictionary<string, object> _parameters = new Dictionary<string, object>();

    public IReadOnlyDictionary<string, object> Parameters => _parameters;

    public (string, Dictionary<string, string>) With(IDictionary<string, object> parameters, Func<Dictionary<string, string>, string> closure)
    {
        var parameterMapping = AssignUniqueParameterNames(parameters);
        var sql = closure(parameterMapping);
        return (sql, parameterMapping);
    }

    public Dictionary<string, string> With(IDictionary<string, object> parameters)
    {
        return AssignUniqueParameterNames(parameters);
    }

    private Dictionary<string, string> AssignUniqueParameterNames(IDictionary<string, object> parameters)
    {
        var parameterMapping = new Dictionary<string, string>();
        foreach ((var paramName, var v) in parameters)
        {
            var uniqueParamName = GetUniqueParameterName(paramName);
            parameterMapping[paramName] = uniqueParamName;
            _parameters[uniqueParamName] = v;
        }
        return parameterMapping;
    }

    private string GetUniqueParameterName(string paramName)
    {
        var index = 0;
        var uniqueParamName = paramName;
        while (_parameters.ContainsKey(uniqueParamName))
        {
            uniqueParamName = $"{paramName}{++index}";
        }
        return uniqueParamName;
    }
}

[TestFixture]
public class SqlParamInjectorTests
{
    [Test]
    public void With_SingleParameter_ReturnsExpectedResult()
    {
        var sp = new SqlParamInjector();
        var parameters = new Dictionary<string, object> { ["orderId"] = 1 };
        var result = sp.With(parameters, (p) => $"select * from orders where order_id = @{p["orderId"]}");

        Assert.That(Regex.IsMatch(result.Item1, @"@orderId\d*"));
        Assert.That(result.Item2.ContainsKey("orderId"));
        Assert.That(sp.Parameters.Count, Is.EqualTo(1));
    }

    [Test]
    public void With_MultipleParameters_ReturnsExpectedResult()
    {
        var sp = new SqlParamInjector();
        var parameters = new Dictionary<string, object> { ["orderId"] = 1, ["minPrice"] = 100.0 };
        var result = sp.With(parameters, (p) => $"select * from orders where order_id = @{p["orderId"]} and price >= @{p["minPrice"]}");

        Assert.That(Regex.IsMatch(result.Item1, @"@orderId\d*"), Is.True);
        Assert.That(Regex.IsMatch(result.Item1, @"@minPrice\d*"), Is.True);
        Assert.That(result.Item2.ContainsKey("orderId"), Is.True);
        Assert.That(result.Item2.ContainsKey("minPrice"), Is.True);
        Assert.That(sp.Parameters.Count, Is.EqualTo(2));
    }

    [Test]
    public void With_DuplicateParameters_AppendsUniqueIndex()
    {
        var sp = new SqlParamInjector();
        var parameters1 = new Dictionary<string, object> { ["orderId"] = 1 };
        sp.With(parameters1, (p) => $"select * from orders where order_id = @{p["orderId"]}");

        var parameters2 = new Dictionary<string, object> { ["orderId"] = 2 };
        var result = sp.With(parameters2, (p) => $"select * from orders where order_id = @{p["orderId"]}");

        Assert.That(Regex.IsMatch(result.Item1, @"@orderId\d+"), Is.True);
        Assert.That(result.Item2.ContainsKey("orderId"), Is.True);
        Assert.That(sp.Parameters.Count, Is.EqualTo(2));
    }

    [Test]
    public void With_NoParameters_ReturnsOriginalString()
    {
        var sp = new SqlParamInjector();
        var parameters = new Dictionary<string, object>();
        var result = sp.With(parameters, (_) => "select * from orders");

        Assert.That(result.Item1, Is.EqualTo("select * from orders"));
        Assert.That(result.Item2, Is.Empty);
        Assert.That(sp.Parameters.Count, Is.EqualTo(0));
    }

    [Test]
    public void With_OnlyParamsArgument_ReturnsMappedParameters()
    {
        var sp = new SqlParamInjector();
        var parameters = new Dictionary<string, object> { ["orderId"] = 1, ["minPrice"] = 100.0 };
        var result = sp.With(parameters);

        Assert.That(result.ContainsKey("orderId"), Is.True);
        Assert.That(result.ContainsKey("minPrice"), Is.True);
        Assert.That(sp.Parameters.Count, Is.EqualTo(2));
    }

    [Test]
    public void With_OnlyParamsArgument_DuplicateParameters_AppendsUniqueIndex()
    {
        var sp = new SqlParamInjector();
        var parameters1 = new Dictionary<string, object> { ["orderId"] = 1 };
        var map1 = sp.With(parameters1);

        var parameters2 = new Dictionary<string, object> { ["orderId"] = 2 };
        var map2 = sp.With(parameters2);

        Assert.That(sp.Parameters.Count, Is.EqualTo(2));
        Assert.That(sp.Parameters[map1["orderId"]], Is.EqualTo(1));
        Assert.That(sp.Parameters[map2["orderId"]], Is.EqualTo(2));
    }

    [Test]
    public void With_OnlyParamsArgument_NoParameters_ReturnsEmptyDictionary()
    {
        var sp = new SqlParamInjector();
        var parameters = new Dictionary<string, object>();
        var result = sp.With(parameters);

        Assert.That(result, Is.Empty);
        Assert.That(sp.Parameters.Count, Is.EqualTo(0));
    }

}
