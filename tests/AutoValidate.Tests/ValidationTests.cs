using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace AutoValidate.Tests;

public class ValidationTests
{
    // ── FluentValidation stub (avoids a real package reference in the generator test) ────
    private const string FluentValidationStub = @"
namespace FluentValidation
{
    public abstract class AbstractValidator<T>
    {
        public virtual ValidationResult Validate(T instance) => new ValidationResult();
    }
    public class ValidationResult
    {
        public bool IsValid => true;
        public System.Collections.Generic.List<ValidationFailure> Errors { get; } = new();
        public System.Collections.Generic.IDictionary<string, string[]> ToDictionary()
            => new System.Collections.Generic.Dictionary<string, string[]>();
    }
    public class ValidationFailure { public string ErrorMessage { get; set; } = """"; }
    public interface IValidator<T> { ValidationResult Validate(T instance); }
}
namespace Microsoft.Extensions.DependencyInjection
{
    public enum ServiceLifetime { Singleton = 0, Scoped = 1, Transient = 2 }
    public interface IServiceCollection { }
}
namespace Microsoft.Extensions.Hosting
{
    public interface IHostedService
    {
        System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken ct);
        System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken ct);
    }
}
namespace Microsoft.AspNetCore.Http
{
    public class RouteHandlerBuilder { }
    public class Results { }
    public interface IEndpointFilter { }
    public class EndpointFilterInvocationContext { public System.Collections.Generic.IList<object?> Arguments { get; } = new System.Collections.Generic.List<object?>(); }
    public delegate System.Threading.Tasks.ValueTask<object?> EndpointFilterDelegate(EndpointFilterInvocationContext context);
}
";

    // ── Test helpers ─────────────────────────────────────────────────────────────────────
    private static string? GetGeneratedSource(string userSource, string fileName = "AutoValidate.g.cs")
    {
        var sources = RunGenerator(userSource);
        return sources.TryGetValue(fileName, out var src) ? src : null;
    }

    private static Dictionary<string, string> RunGenerator(string userSource)
    {
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(FluentValidationStub),
            CSharpSyntaxTree.ParseText(userSource)
        };

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new AutoValidateGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var result = driver.GetRunResult();
        var generated = new Dictionary<string, string>();
        foreach (var tree in result.GeneratedTrees)
        {
            var name = System.IO.Path.GetFileName(tree.FilePath);
            generated[name] = tree.GetText().ToString();
        }
        return generated;
    }

    private static IReadOnlyList<Diagnostic> GetDiagnostics(string userSource)
    {
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(FluentValidationStub),
            CSharpSyntaxTree.ParseText(userSource)
        };

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new AutoValidateGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics;
    }

    // ── Attribute emission ────────────────────────────────────────────────────────────────
    [Fact]
    public void AttributeSource_IsEmitted()
    {
        var sources = RunGenerator("");
        Assert.True(sources.ContainsKey("AutoValidate.Attributes.g.cs"),
            "Attribute file should always be emitted");
    }

    [Fact]
    public void AttributeSource_ContainsAllThreeAttributes()
    {
        var sources = RunGenerator("");
        var src = sources["AutoValidate.Attributes.g.cs"];
        Assert.Contains("SkipValidatorAttribute", src);
        Assert.Contains("ValidatorLifetimeAttribute", src);
        Assert.Contains("ValidateOnStartupAttribute", src);
    }

    // ── Convention-based discovery ────────────────────────────────────────────────────────
    [Fact]
    public void SingleValidator_GeneratesAddValidators()
    {
        var src = GetGeneratedSource(@"
using FluentValidation;
public class Order { public string Name { get; set; } = """"; }
public class OrderValidator : AbstractValidator<Order> { }
");
        Assert.NotNull(src);
        Assert.Contains("AddScoped<IValidator<Order>, OrderValidator>()", src);
    }

    [Fact]
    public void MultipleValidators_AllRegistered()
    {
        var src = GetGeneratedSource(@"
using FluentValidation;
public class Order { }
public class Customer { }
public class OrderValidator : AbstractValidator<Order> { }
public class CustomerValidator : AbstractValidator<Customer> { }
");
        Assert.NotNull(src);
        Assert.Contains("IValidator<Order>, OrderValidator", src);
        Assert.Contains("IValidator<Customer>, CustomerValidator", src);
    }

    [Fact]
    public void NoValidators_NoMainFileEmitted()
    {
        var src = GetGeneratedSource("public class Foo { }");
        Assert.Null(src);
    }

    // ── [SkipValidator] ───────────────────────────────────────────────────────────────────
    [Fact]
    public void SkipValidator_ExcludesFromRegistration()
    {
        var src = GetGeneratedSource(@"
using FluentValidation;
using AutoValidate;
public class Order { }
public class Customer { }
[SkipValidator]
public class OrderValidator : AbstractValidator<Order> { }
public class CustomerValidator : AbstractValidator<Customer> { }
");
        Assert.NotNull(src);
        Assert.DoesNotContain("OrderValidator", src);
        Assert.Contains("CustomerValidator", src);
    }

    [Fact]
    public void SkipValidator_AllSkipped_NoMainFileEmitted()
    {
        var src = GetGeneratedSource(@"
using FluentValidation;
using AutoValidate;
public class Order { }
[SkipValidator]
public class OrderValidator : AbstractValidator<Order> { }
");
        Assert.Null(src);
    }

    // ── [ValidatorLifetime] ───────────────────────────────────────────────────────────────
    [Fact]
    public void ValidatorLifetime_Singleton_UsesSingleton()
    {
        var src = GetGeneratedSource(@"
using FluentValidation;
using AutoValidate;
public class Config { }
[ValidatorLifetime(ValidatorLifetime.Singleton)]
public class ConfigValidator : AbstractValidator<Config> { }
");
        Assert.NotNull(src);
        Assert.Contains("AddSingleton<IValidator<Config>, ConfigValidator>()", src);
    }

    [Fact]
    public void ValidatorLifetime_Transient_UsesTransient()
    {
        var src = GetGeneratedSource(@"
using FluentValidation;
using AutoValidate;
public class Request { }
[ValidatorLifetime(ValidatorLifetime.Transient)]
public class RequestValidator : AbstractValidator<Request> { }
");
        Assert.NotNull(src);
        Assert.Contains("AddTransient<IValidator<Request>, RequestValidator>()", src);
    }

    [Fact]
    public void ValidatorLifetime_Default_UsesScoped()
    {
        var src = GetGeneratedSource(@"
using FluentValidation;
public class Order { }
public class OrderValidator : AbstractValidator<Order> { }
");
        Assert.NotNull(src);
        Assert.Contains("AddScoped<IValidator<Order>, OrderValidator>()", src);
    }

    // ── [ValidateOnStartup] ───────────────────────────────────────────────────────────────
    [Fact]
    public void ValidateOnStartup_AddsHostedService()
    {
        var src = GetGeneratedSource(@"
using FluentValidation;
using AutoValidate;
public class AppSettings { }
[ValidateOnStartup]
public class AppSettingsValidator : AbstractValidator<AppSettings> { }
");
        Assert.NotNull(src);
        Assert.Contains("AddHostedService<ValidatorStartupService<AppSettings>>()", src);
        Assert.Contains("ValidatorStartupService<T>", src);
    }

    [Fact]
    public void ValidateOnStartup_NotSet_NoHostedService()
    {
        var src = GetGeneratedSource(@"
using FluentValidation;
public class Order { }
public class OrderValidator : AbstractValidator<Order> { }
");
        Assert.NotNull(src);
        Assert.DoesNotContain("ValidatorStartupService", src);
    }

    // ── WithValidation<T>() ───────────────────────────────────────────────────────────────
    [Fact]
    public void GeneratedFile_ContainsWithValidationHelper()
    {
        var src = GetGeneratedSource(@"
using FluentValidation;
public class Order { }
public class OrderValidator : AbstractValidator<Order> { }
");
        Assert.NotNull(src);
        Assert.Contains("WithValidation<T>", src);
        Assert.Contains("ValidationFilter<T>", src);
    }

    // ── AV001: Duplicate validator ────────────────────────────────────────────────────────
    [Fact]
    public void DuplicateValidator_EmitsAV001Warning()
    {
        var diags = GetDiagnostics(@"
using FluentValidation;
public class Order { }
public class OrderValidatorV1 : AbstractValidator<Order> { }
public class OrderValidatorV2 : AbstractValidator<Order> { }
");
        Assert.Contains(diags, d => d.Id == "AV001");
    }

    [Fact]
    public void DuplicateValidator_OnlyFirstIsRegistered()
    {
        var src = GetGeneratedSource(@"
using FluentValidation;
public class Order { }
public class OrderValidatorA : AbstractValidator<Order> { }
public class OrderValidatorB : AbstractValidator<Order> { }
");
        Assert.NotNull(src);
        // Should contain exactly one registration for Order
        var count = System.Text.RegularExpressions.Regex.Matches(src!, "IValidator<Order>").Count;
        Assert.Equal(1, count);
    }

    // ── Inheritance chain ─────────────────────────────────────────────────────────────────
    [Fact]
    public void IndirectInheritance_ValidatorDiscovered()
    {
        var src = GetGeneratedSource(@"
using FluentValidation;
public class Order { }
public abstract class BaseOrderValidator : AbstractValidator<Order> { }
public class ConcreteOrderValidator : BaseOrderValidator { }
");
        Assert.NotNull(src);
        Assert.Contains("IValidator<Order>, ConcreteOrderValidator", src);
    }

    // ── Namespaced types ──────────────────────────────────────────────────────────────────
    [Fact]
    public void NamespacedTypes_FullyQualifiedNamesUsed()
    {
        var src = GetGeneratedSource(@"
using FluentValidation;
namespace MyApp.Models { public class Order { } }
namespace MyApp.Validators
{
    using MyApp.Models;
    public class OrderValidator : AbstractValidator<Order> { }
}
");
        Assert.NotNull(src);
        Assert.Contains("IValidator<MyApp.Models.Order>", src);
        Assert.Contains("MyApp.Validators.OrderValidator", src);
    }
}
